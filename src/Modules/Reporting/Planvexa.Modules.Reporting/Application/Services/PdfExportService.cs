namespace Planvexa.Modules.Reporting.Application.Services;

using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using Planvexa.Modules.Reporting.Authorization;

/// <summary>
/// PDF export for the Portfolio summary. Package choice: PDFsharp (MIT, empira
/// Software GmbH) — verified current terms before adding (AGENTS.md rule 15): plain MIT, no revenue
/// threshold. QuestPDF was considered but rejected: as of its 2026 "Community License" (v3.0) it is a
/// source-available commercial licence gated on the user's annual gross revenue (assemblies over
/// USD 1,000,000/year must buy a paid tier) — not an unconditionally permissive dependency, despite
/// being nicknamed "Community MIT" in places. Hand-rolling PDF binary output (the brief's other named
/// option) is not reasonable for real output, so a real library is used, matching the brief's guidance.
/// Font resolution: PDFsharp 6's Core build ships no fonts and needs an <c>IFontResolver</c> to draw any
/// text at all (<see cref="GlobalFontSettings.UseWindowsFontsUnderWindows"/> alone was not sufficient in
/// practice — XFont still threw "No appropriate font found"). <see cref="CrossPlatformFontFileResolver"/>
/// uses Segoe UI on Windows and DejaVu Sans on Linux. The API image installs <c>fonts-dejavu-core</c> so the
/// production container and Linux CI use the same deterministic fallback.
/// </summary>
public sealed class PdfExportService(ReportingServiceContext ctx, PortfolioService portfolio) : ReportingServiceBase(ctx)
{
    static PdfExportService()
    {
        GlobalFontSettings.FontResolver ??= new CrossPlatformFontFileResolver();
    }

    private sealed class CrossPlatformFontFileResolver : IFontResolver
    {
        private const string RegularFace = "PlanvexaSans";
        private const string BoldFace = "PlanvexaSans#b";

        private static readonly string[] RegularFontPaths =
        [
            @"C:\Windows\Fonts\segoeui.ttf",
            "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
        ];

        private static readonly string[] BoldFontPaths =
        [
            @"C:\Windows\Fonts\segoeuib.ttf",
            "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf",
        ];

        public byte[]? GetFont(string faceName)
        {
            var paths = faceName == BoldFace ? BoldFontPaths : RegularFontPaths;
            var path = paths.FirstOrDefault(File.Exists)
                ?? throw new FileNotFoundException(
                    $"No PDF export font was found. Checked: {string.Join(", ", paths)}");
            return File.ReadAllBytes(path);
        }

        public FontResolverInfo ResolveTypeface(string familyName, bool isBold, bool isItalic)
            => new(isBold ? BoldFace : RegularFace);
    }

    public async Task<byte[]> PortfolioPdfAsync(DateTimeOffset? fromUtc, DateTimeOffset? toUtc, CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        ReportingAuthorizer.EnsureManage((await AccessAsync(workspaceId, ct))?.Role);

        var rows = await portfolio.GetAsync(fromUtc, toUtc, ct);

        var titleFont = new XFont("Segoe UI", 16, XFontStyleEx.Bold);
        var headerFont = new XFont("Segoe UI", 10, XFontStyleEx.Bold);
        var bodyFont = new XFont("Segoe UI", 10, XFontStyleEx.Regular);
        string[] headers = ["Space", "Total", "Completed", "Health %", "Logged Hrs", "Risks", "Milestones"];
        double[] columnX = [40, 220, 280, 350, 420, 500, 560];

        using var document = new PdfDocument();
        var rowsPerPage = 0;
        PdfPage page = null!;
        XGraphics? gfx = null;
        double y = 0;

        // ponytail: fixed rows-per-page pagination (no dynamic row-height/orphan handling) — good enough
        // for a portfolio summary (dozens of Spaces, not thousands); revisit with a real layout engine if
        // a workspace's Space count ever makes that a real constraint.
        const int maxRowsPerPage = 35;
        foreach (var row in rows)
        {
            if (gfx is null || rowsPerPage >= maxRowsPerPage)
            {
                gfx?.Dispose();
                page = document.AddPage();
                gfx = XGraphics.FromPdfPage(page);
                y = 40;
                gfx.DrawString("Portfolio Summary", titleFont, XBrushes.Black, new XPoint(40, y));
                y += 30;
                for (var i = 0; i < headers.Length; i++)
                {
                    gfx.DrawString(headers[i], headerFont, XBrushes.Black, new XPoint(columnX[i], y));
                }

                y += 20;
                rowsPerPage = 0;
            }

            gfx!.DrawString(row.Label, bodyFont, XBrushes.Black, new XPoint(columnX[0], y));
            gfx.DrawString(row.TotalTasks.ToString(), bodyFont, XBrushes.Black, new XPoint(columnX[1], y));
            gfx.DrawString(row.CompletedTasks.ToString(), bodyFont, XBrushes.Black, new XPoint(columnX[2], y));
            gfx.DrawString($"{row.HealthPercent}%", bodyFont, XBrushes.Black, new XPoint(columnX[3], y));
            gfx.DrawString(row.LoggedHours.ToString("0.##"), bodyFont, XBrushes.Black, new XPoint(columnX[4], y));
            gfx.DrawString(row.Risks.Count.ToString(), bodyFont, XBrushes.Black, new XPoint(columnX[5], y));
            gfx.DrawString(row.Milestones.Count.ToString(), bodyFont, XBrushes.Black, new XPoint(columnX[6], y));
            y += 18;
            rowsPerPage++;
        }

        if (gfx is null)
        {
            page = document.AddPage();
            gfx = XGraphics.FromPdfPage(page);
            gfx.DrawString("Portfolio Summary (no spaces)", titleFont, XBrushes.Black, new XPoint(40, 40));
        }

        gfx.Dispose();

        using var stream = new MemoryStream();
        document.Save(stream);
        return stream.ToArray();
    }
}
