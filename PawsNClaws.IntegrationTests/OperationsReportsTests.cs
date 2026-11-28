using System.Net;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using PdfSharp.Pdf.IO;
using PawsNClaws.Domain.Entities;
using PawsNClaws.Domain.Enums;
using PawsNClaws.Infastructure.Identity.Local;
using PawsNClaws.Infastructure.Persistence.Local;

namespace PawsNClaws.IntegrationTests;

public sealed class OperationsReportsTests(LocalAppFactory app) : IClassFixture<LocalAppFactory>
{
    [Fact]
    public async Task Reports_use_Philippine_boundaries_and_reconcile_historical_balances()
    {
        var token = TestContext.Current.CancellationToken;
        var manager = await app.CreateEmployeeAsync(LocalRoles.Manager);
        var admin = await app.CreateEmployeeAsync(LocalRoles.Admin);
        var cashier = await app.CreateEmployeeAsync(LocalRoles.Cashier);

        // Midnight on November 1 in Manila and the exclusive next-day cutoff.
        var start = new DateTime(2026, 10, 31, 16, 0, 0, DateTimeKind.Utc);
        var end = start.AddDays(1);
        var oldBill = new GroomingBill { VisitId = Guid.NewGuid(), IssuedAtUtc = start.AddHours(-4), Total = 300m, HomeDepositAmount = 50m };
        var newBill = new GroomingBill { VisitId = Guid.NewGuid(), IssuedAtUtc = start, Total = 100m };
        var nextDayBill = new GroomingBill { VisitId = Guid.NewGuid(), IssuedAtUtc = end, Total = 70m };

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();
        db.AddRange(
            new RetailSale { Id = Guid.NewGuid(), RecordedAtUtc = start.AddSeconds(-1), VoidedAtUtc = start, Total = 20m,
                Tendered = 20m, PaymentMethod = RetailPaymentMethod.Online, OnlinePaymentReference = "REPORT-ONLINE-1",
                VatRate = 0.12m, VatAmount = 2.14m },
            new RetailSale { Id = Guid.NewGuid(), RecordedAtUtc = start, VoidedAtUtc = end, Total = 100m,
                Tendered = 100m, VatRate = 0.12m, VatAmount = 10.71m },
            new RetailSale { Id = Guid.NewGuid(), RecordedAtUtc = end, Total = 180m, Tendered = 180m,
                PaymentMethod = RetailPaymentMethod.Online, OnlinePaymentReference = "REPORT-ONLINE-2" },
            oldBill, newBill, nextDayBill,
            new GroomingCashPayment { Id = Guid.NewGuid(), VisitId = oldBill.VisitId, RecordedAtUtc = start.AddSeconds(-1), Amount = 40m },
            new GroomingCashPayment { Id = Guid.NewGuid(), VisitId = oldBill.VisitId, RecordedAtUtc = start, Amount = 60m, Tendered = 100m },
            new GroomingCashPayment { Id = Guid.NewGuid(), VisitId = oldBill.VisitId, RecordedAtUtc = end, Amount = 150m },
            new OffsiteBooking { ConfirmedAtUtc = start, VerifiedDeposit = 50m },
            new WalkInEntry { ArrivedAtUtc = start.AddMinutes(-20), CalledAtUtc = start },
            new WalkInEntry { ArrivedAtUtc = start.AddHours(1), CalledAtUtc = start.AddHours(1).AddMinutes(10) },
            new CashDrawerShift { ClosedAtUtc = start, ExpectedAtClose = 100m, CountedCash = 90m },
            new CashDrawerShift { ClosedAtUtc = start.AddHours(2), ExpectedAtClose = 100m, CountedCash = 105m });
        await db.SaveChangesAsync(token);

        using var managerClient = app.CreateBrowser();
        using var managerSignIn = await LocalAppFactory.SignInAsync(managerClient, manager);
        using var adminClient = app.CreateBrowser();
        using var adminSignIn = await LocalAppFactory.SignInAsync(adminClient, admin);
        using var cashierClient = app.CreateBrowser();
        using var cashierSignIn = await LocalAppFactory.SignInAsync(cashierClient, cashier);

        const string report = "/Reports/Operations";
        const string day = "?date=2026-11-01&monthly=false";
        const string month = "?date=2026-11-15&monthly=true";
        using var page = await managerClient.GetAsync(report + day, token);
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.True(page.Headers.CacheControl?.NoStore);
        var html = WebUtility.HtmlDecode(await page.Content.ReadAsStringAsync(token))!;
        Assert.Contains("Download PDF", html);
        Assert.DoesNotContain("Download CSV", html);
        Assert.Contains("Philippine time", html);
        Assert.Contains(">PHP 250.00</td>", html);
        Assert.Contains(">15 min</td>", html);
        AssertMetric(html, "Retail sales before refunds", "PHP 100.00");
        AssertMetric(html, "Retail refunds recorded", "PHP 20.00");
        AssertMetric(html, "Retail sales less refunds recorded", "PHP 80.00");
        AssertMetric(html, "Retail VAT recorded on sales", "PHP 10.71");
        AssertMetric(html, "Retail VAT recorded on refunds", "PHP 2.14");
        AssertMetric(html, "Retail VAT less refunds recorded", "PHP 8.57");
        AssertMetric(html, "Cash sales", "PHP 100.00");
        AssertMetric(html, "Cash refunds", "PHP 0.00");
        AssertMetric(html, "Net cash retail", "PHP 100.00");
        AssertMetric(html, "Online sales", "PHP 0.00");
        AssertMetric(html, "Online refunds", "PHP 20.00");
        AssertMetric(html, "Net online retail", "PHP -20.00");
        AssertMetric(html, "Grooming billed", "PHP 100.00");
        AssertMetric(html, "Grooming cash received", "PHP 60.00");
        AssertMetric(html, "Verified home-service deposits", "PHP 50.00");
        AssertMetric(html, "Cash drawer shortages", "PHP 10.00");
        AssertMetric(html, "Cash drawer overages", "PHP 5.00");
        AssertMetric(html, "Walk-ins added", "1");
        AssertMetric(html, "Walk-ins called", "2");

        using var export = await adminClient.GetAsync(report + "/Export" + day, token);
        Assert.Equal(HttpStatusCode.OK, export.StatusCode);
        Assert.True(export.Headers.CacheControl?.NoStore);
        await AssertRestrictedPdfAsync(export, "operations-2026-11-01.pdf", "2026-11-01", "PHP -20.00", token);

        var monthly = WebUtility.HtmlDecode(await managerClient.GetStringAsync(report + month, token))!;
        AssertMetric(monthly, "Retail sales before refunds", "PHP 280.00");
        AssertMetric(monthly, "Retail refunds recorded", "PHP 120.00");
        AssertMetric(monthly, "Cash sales", "PHP 100.00");
        AssertMetric(monthly, "Cash refunds", "PHP 100.00");
        AssertMetric(monthly, "Net cash retail", "PHP 0.00");
        AssertMetric(monthly, "Online sales", "PHP 180.00");
        AssertMetric(monthly, "Online refunds", "PHP 20.00");
        AssertMetric(monthly, "Net online retail", "PHP 160.00");
        AssertMetric(monthly, "Grooming cash received", "PHP 210.00");
        AssertMetric(monthly, "Outstanding grooming balance at period end", "PHP 170.00");
        AssertMetric(monthly, "Retail sales without saved VAT details", "1");
        using var monthlyExport = await managerClient.GetAsync(report + "/Export" + month, token);
        await AssertRestrictedPdfAsync(monthlyExport, "operations-2026-11.pdf", "2026-11-01 to 2026-11-30", "PHP 160.00", token);

        using var invalid = await managerClient.GetAsync(report + "?date=bad-date", token);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        using var denied = await cashierClient.GetAsync(report + day, token);
        Assert.Contains("/Account/AccessDenied", denied.Headers.Location?.OriginalString);
        using var deniedExport = await cashierClient.GetAsync(report + "/Export" + day, token);
        Assert.Contains("/Account/AccessDenied", deniedExport.Headers.Location?.OriginalString);
        using var queue = await managerClient.GetAsync(report + "/Sms", token);
        Assert.Equal(HttpStatusCode.OK, queue.StatusCode);
        using var deniedQueue = await cashierClient.GetAsync(report + "/Sms", token);
        Assert.Contains("/Account/AccessDenied", deniedQueue.Headers.Location?.OriginalString);
    }

    private static void AssertMetric(string html, string label, string value) =>
        Assert.Matches($""">{System.Text.RegularExpressions.Regex.Escape(label)}</th>\s*<td class="text-end">{System.Text.RegularExpressions.Regex.Escape(value)}</td>""", html);

    private static async Task AssertRestrictedPdfAsync(HttpResponseMessage response, string fileName,
        string date, string metric, CancellationToken token)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("attachment", response.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Equal(fileName, response.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
        var bytes = await response.Content.ReadAsByteArrayAsync(token);
        Assert.StartsWith("%PDF-", Encoding.ASCII.GetString(bytes, 0, 5));
        using var pdf = PdfReader.Open(new MemoryStream(bytes), PdfDocumentOpenMode.Import);
        Assert.False(pdf.SecuritySettings.PermitModifyDocument);
        Assert.False(pdf.SecuritySettings.PermitAnnotations);
        Assert.False(pdf.SecuritySettings.PermitFormsFill);
        Assert.False(pdf.SecuritySettings.PermitAssembleDocument);
        Assert.True(pdf.SecuritySettings.PermitPrint);
        Assert.Throws<PdfReaderException>(() => PdfReader.Open(new MemoryStream(bytes), PdfDocumentOpenMode.Modify));
        Assert.Equal(1, pdf.PageCount);
        var page = pdf.Pages[0];
        var content = Encoding.Latin1.GetString(page.Contents.CreateSingleContent().Stream.UnfilteredValue);
        Assert.Contains(date, content);
        Assert.Contains("Retail VAT less refunds recorded", content);
        Assert.Contains("Retail sales and refunds by payment method", content);
        Assert.Contains("Net cash retail", content);
        Assert.Contains("Net online retail", content);
        Assert.Contains("Inventory movements", content);
        Assert.Contains(metric, content);
    }
}
