using System.Net;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using PawsNClaws.Application.Features.Inventory;
using PawsNClaws.Domain.Entities;
using PawsNClaws.Domain.Enums;
using PawsNClaws.Infastructure.Features.Inventory;
using PawsNClaws.Infastructure.Identity.Local;
using PawsNClaws.Infastructure.Persistence.Local;
using SkiaSharp;

namespace PawsNClaws.IntegrationTests;

public sealed class InventoryWorkflowTests : IAsyncLifetime
{
    private readonly LocalDbContext _db = new(new DbContextOptionsBuilder<LocalDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);
    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("paws-inventory-");
    private InventoryService Service => new(_db, new InventoryEvidenceStore(_directory.FullName), NullLogger<InventoryService>.Instance);

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        await _db.DisposeAsync();
        _directory.Delete(recursive: true);
    }

    [Fact]
    public async Task Partial_receipt_and_disposal_hold_preserve_balances_and_evidence_ownership()
    {
        var ct = TestContext.Current.CancellationToken;
        var product = await AddProductAsync(ct);
        var staffId = Guid.NewGuid();
        var staff = Actor(staffId, LocalRoles.InventoryStaff,
            Permissions.InventoryView, Permissions.InventoryReceive, Permissions.InventorySubmit);
        var manager = Actor(Guid.NewGuid(), LocalRoles.Manager, Permissions.InventoryView,
            Permissions.InventoryReview, Permissions.InventoryAdjust);
        var service = Service;
        var receipt = new ReceivingCommand(Guid.NewGuid(), product.Id, "Supplier", "Invoice", "LOT-1", null,
            10, 8, 2, null, "Rejected at receiving");
        using var receiptPhoto = Photo();
        await service.ReceiveAsync(staff, receipt, [new("receipt.png", receiptPhoto)], ct);
        await service.ReceiveAsync(staff, receipt, [], ct);
        var batch = await _db.Set<InventoryBatch>().SingleAsync(ct);
        Assert.Equal(8, batch.OnHand);

        var disposal = new InventoryCommand(Guid.NewGuid(), InventoryAction.Submit, product.Id,
            batch.Id, null, null, 3, StockOutReason.Defective, "Broken packaging");
        using var disposalPhoto = Photo();
        await service.ExecuteAsync(staff, disposal, [new("disposal.png", disposalPhoto)], ct);
        await service.ExecuteAsync(staff, disposal, [], ct);
        var request = await _db.StockOutRequests.Include(x => x.Evidence).SingleAsync(ct);
        Assert.Equal(3, batch.Quarantined);
        Assert.Equal(8, (await _db.InventoryStocks.SingleAsync(ct)).QuantityOnHand);
        Assert.Single(await _db.InventoryMovements.ToListAsync(ct));
        var photoId = Assert.Single(request.Evidence).Id;
        var otherStaff = Actor(Guid.NewGuid(), LocalRoles.InventoryStaff, Permissions.InventoryView);
        Assert.Null(await service.OpenEvidenceAsync(otherStaff, "request", photoId, ct));
        var download = await service.OpenEvidenceAsync(manager, "request", photoId, ct);
        Assert.NotNull(download);
        await download.Content.DisposeAsync();

        var review = new InventoryCommand(Guid.NewGuid(), InventoryAction.Approve, product.Id,
            null, request.Id, null, 0, null, null);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ExecuteAsync(
            Actor(staffId, LocalRoles.Manager, Permissions.InventoryReview), review, [], ct));
        await service.ExecuteAsync(manager, review, [], ct);
        await service.ExecuteAsync(manager, review, [], ct);
        var posted = await _db.InventoryMovements.SingleAsync(x => x.StockOutRequestId == request.Id, ct);
        Assert.Equal(-3, posted.QuantityChange);
        Assert.Equal(5, (await _db.InventoryStocks.SingleAsync(ct)).QuantityOnHand);
        Assert.Equal(5, (await _db.Set<InventoryBatch>().SingleAsync(ct)).OnHand);
        Assert.Equal(StockOutRequestStatus.Approved, (await _db.StockOutRequests.SingleAsync(ct)).Status);
        Assert.Empty(await _db.InventoryMovementEvidence.ToListAsync(ct));
    }

    [Fact]
    public async Task Usage_uses_earliest_usable_expiry_and_posts_estimate_only_once()
    {
        var ct = TestContext.Current.CancellationToken;
        var product = await AddProductAsync(ct, consumable: true);
        var manager = Actor(Guid.NewGuid(), LocalRoles.Manager,
            Permissions.InventoryReceive, Permissions.InventoryUse, Permissions.InventoryAdjust);
        var service = Service;
        var today = DateOnly.FromDateTime(DateTime.Today);
        foreach (var (lot, expiry) in new[] { ("A", today.AddDays(10)), ("B", today.AddDays(1)), ("C", today) })
        {
            await service.ReceiveAsync(manager, new(Guid.NewGuid(), product.Id, "Supplier", null, lot,
                expiry, 0.0105m, 0.0105m, 0, null, null, "L"), [], ct);
        }

        var expired = await _db.Set<InventoryBatch>().SingleAsync(x => x.Lot == "C", ct);
        expired.ExpiryDate = today.AddDays(-1);
        await _db.SaveChangesAsync(ct);
        var estimate = new InventoryCommand(Guid.NewGuid(), InventoryAction.EstimateUsage, product.Id,
            null, null, null, 9, null, "JOB-1");
        await service.ExecuteAsync(manager, estimate, [], ct);
        Assert.Equal(31.5m, (await _db.InventoryStocks.SingleAsync(ct)).QuantityOnHand);
        var use = estimate with { OperationId = Guid.NewGuid(), Action = InventoryAction.Use, MovementId = estimate.OperationId };
        await service.ExecuteAsync(manager, use, [], ct);
        await service.ExecuteAsync(manager, use, [], ct);
        await Assert.ThrowsAsync<InventoryRuleException>(() =>
            service.ExecuteAsync(manager, use with { OperationId = Guid.NewGuid() }, [], ct));
        var movement = await _db.InventoryMovements.SingleAsync(x => x.UsageEstimateId == estimate.OperationId, ct);
        var earliest = await _db.Set<InventoryBatch>().SingleAsync(x => x.Lot == "B", ct);
        Assert.Equal(1.5m, earliest.OnHand);
        Assert.Equal(10.5m, (await _db.Set<InventoryBatch>().SingleAsync(x => x.Lot == "C", ct)).OnHand);
        await service.ExecuteAsync(manager, use with { OperationId = Guid.NewGuid(), Action = InventoryAction.ReverseUsage,
            MovementId = movement.Id }, [], ct);
        Assert.Equal(31.5m, (await _db.InventoryStocks.SingleAsync(ct)).QuantityOnHand);
    }

    [Fact]
    public async Task Invalid_photos_and_nonmanagement_adjustments_are_rejected_without_changes()
    {
        var ct = TestContext.Current.CancellationToken;
        var product = await AddProductAsync(ct);
        var staff = Actor(Guid.NewGuid(), LocalRoles.InventoryStaff, Permissions.InventoryReceive, Permissions.InventoryAdjust);
        var service = Service;
        var adjustment = new InventoryCommand(Guid.NewGuid(), InventoryAction.Adjust, product.Id,
            null, null, null, -1, null, "Unapproved");
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ExecuteAsync(staff, adjustment, [], ct));
        using var invalidPhoto = new MemoryStream([0xff, 0xd8, 0xff, 0x00]);
        await Assert.ThrowsAsync<InventoryRuleException>(() => service.ReceiveAsync(staff,
            new(Guid.NewGuid(), product.Id, "Supplier", null, null, null, 2, 1, 1, null, "Damaged"),
            [new("invalid.jpg", invalidPhoto)], ct));
        Assert.Empty(await _db.Set<StockReceipt>().ToListAsync(ct));
        Assert.Empty(_directory.EnumerateFiles());
        await Assert.ThrowsAsync<InventoryRuleException>(() => service.ReceiveAsync(staff,
            new(Guid.NewGuid(), product.Id, "Supplier", null, null, null, 0.5m, 0.5m, 0, null, null), [], ct));
    }

    private async Task<Product> AddProductAsync(CancellationToken ct, bool consumable = false)
    {
        var product = new Product { Name = "Test inventory item", SKU = "TEST-ITEM", Unit = consumable ? "mL" : "Each",
            IsSellable = !consumable, IsConsumable = consumable };
        _db.Products.Add(product);
        await _db.SaveChangesAsync(ct);
        return product;
    }

    private static ClaimsPrincipal Actor(Guid id, string role, params string[] permissions) =>
        new(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, id.ToString()), new Claim(ClaimTypes.Role, role) }
            .Concat(permissions.Select(permission => new Claim(LocalRoleSeeder.PermissionClaimType, permission))), "Test"));

    private static MemoryStream Photo()
    {
        using var bitmap = new SKBitmap(2, 2);
        bitmap.Erase(SKColors.Orange);
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        return new MemoryStream(encoded.ToArray());
    }
}

public sealed class InventoryScreenTests(LocalAppFactory app) : IClassFixture<LocalAppFactory>
{
    [Fact]
    public async Task Inventory_staff_can_see_the_workspace_and_posts_require_antiforgery()
    {
        var ct = TestContext.Current.CancellationToken;
        var staff = await app.CreateEmployeeAsync(LocalRoles.InventoryStaff);
        using var client = app.CreateBrowser();
        using var login = await LocalAppFactory.SignInAsync(client, staff);
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        using var page = await client.GetAsync("/Inventory", ct);
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Contains("Stock Out requests", await page.Content.ReadAsStringAsync(ct));
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>());
        using var post = await client.PostAsync("/Inventory/Post", form, ct);
        Assert.Equal(HttpStatusCode.BadRequest, post.StatusCode);
    }

    [Fact]
    public async Task Inventory_staff_can_receive_through_the_screen_but_cannot_adjust_stock()
    {
        var ct = TestContext.Current.CancellationToken;
        var staff = await app.CreateEmployeeAsync(LocalRoles.InventoryStaff);
        var product = new Product { Name = "Screen receiving item", SKU = $"SCREEN-{Guid.NewGuid():N}" };
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();
            db.Products.Add(product);
            await db.SaveChangesAsync(ct);
        }

        using var client = app.CreateBrowser();
        using var login = await LocalAppFactory.SignInAsync(client, staff);
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        var token = await LocalAppFactory.GetAntiforgeryTokenAsync(client, "/Inventory");
        using var receipt = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["OperationId"] = Guid.NewGuid().ToString(),
            ["ProductId"] = product.Id.ToString(),
            ["Supplier"] = "Screen test supplier",
            ["Delivered"] = "8",
            ["Accepted"] = "8",
            ["Rejected"] = "0",
            ["__RequestVerificationToken"] = token
        });
        using var received = await client.PostAsync("/Inventory/Receive", receipt, ct);
        Assert.Equal(HttpStatusCode.Redirect, received.StatusCode);
        using var page = await client.GetAsync("/Inventory", ct);
        var html = await page.Content.ReadAsStringAsync(ct);
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Contains("Inventory operation recorded.", html);
        Assert.Contains("Screen test supplier", html);

        using var adjustment = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["OperationId"] = Guid.NewGuid().ToString(),
            ["ProductId"] = product.Id.ToString(),
            ["Action"] = "Adjust",
            ["Quantity"] = "-1",
            ["Reason"] = "Unauthorized adjustment",
            ["__RequestVerificationToken"] = token
        });
        using var denied = await client.PostAsync("/Inventory/Post", adjustment, ct);
        Assert.Equal(HttpStatusCode.Redirect, denied.StatusCode);
        Assert.StartsWith("/Account/AccessDenied", denied.Headers.Location!.PathAndQuery);
        await using var verification = app.Services.CreateAsyncScope();
        var persisted = verification.ServiceProvider.GetRequiredService<LocalDbContext>();
        Assert.Equal(8m, await persisted.InventoryStocks.Where(x => x.ProductId == product.Id)
            .Select(x => x.QuantityOnHand).SingleAsync(ct));
    }
}
