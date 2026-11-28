using System.Globalization;
using System.Security.Cryptography;
using PdfSharp;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using PawsNClaws.Local.Models;

namespace PawsNClaws.Local.Reports;

internal static class OperationsReportPdf
{
    static OperationsReportPdf()
    {
        GlobalFontSettings.UseWindowsFontsUnderWindows = true;
    }

    internal static byte[] Render(OperationsReportPage report)
    {
        using var document = new PdfDocument();
        document.Info.Title = "Paws N' Claws Operations Report";
        document.Info.Author = "Paws N' Claws";
        document.SecuritySettings.OwnerPassword = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        document.SecuritySettings.PermitModifyDocument = false;
        document.SecuritySettings.PermitAnnotations = false;
        document.SecuritySettings.PermitFormsFill = false;
        document.SecuritySettings.PermitAssembleDocument = false;

        var page = document.AddPage();
        page.Size = PageSize.A4;
        using (var canvas = XGraphics.FromPdfPage(page))
        {
            var title = new XFont("Arial", 24, XFontStyleEx.Bold);
            var heading = new XFont("Arial", 12, XFontStyleEx.Bold);
            var body = new XFont("Arial", 10);
            var ink = new XSolidBrush(XColor.FromArgb(32, 48, 57));
            var accent = new XSolidBrush(XColor.FromArgb(22, 97, 91));
            const double margin = 44;
            var width = page.Width.Point - 2 * margin;
            var y = 48d;

            canvas.DrawString("Paws N' Claws", heading, accent, margin, y);
            canvas.DrawString("Operations report", title, ink, margin, y += 30);
            var dates = report.FirstDay == report.LastDay
                ? $"{report.FirstDay:yyyy-MM-dd}"
                : $"{report.FirstDay:yyyy-MM-dd} to {report.LastDay:yyyy-MM-dd}";
            canvas.DrawString($"{dates} | Philippine time", body, ink, margin, y += 22);

            DrawSection("Sales, payments, and drawer variances", report.Money);
            DrawSection("Retail sales and refunds by payment method", report.RetailPayments);
            DrawSection("Shop activity", report.Activity);

            void DrawSection(string label, IEnumerable<ReportMetric> rows)
            {
                y += 30;
                canvas.DrawString(label, heading, accent, margin, y);
                foreach (var row in rows)
                {
                    var value = report.FormatMetric(row, CultureInfo.InvariantCulture);
                    y += 18;
                    canvas.DrawString(row.Label, body, ink, margin, y);
                    canvas.DrawString(value, body, ink, margin + width - canvas.MeasureString(value, body).Width, y);
                    canvas.DrawLine(XPens.Gainsboro, margin, y + 5, margin + width, y + 5);
                }
            }
        }
        using var output = new MemoryStream();
        document.Save(output, closeStream: false);
        return output.ToArray();
    }
}
