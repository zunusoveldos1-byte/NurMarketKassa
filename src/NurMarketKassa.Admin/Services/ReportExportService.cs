using ClosedXML.Excel;
using NurMarketKassa.Services;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace NurMarketKassa.Admin.Services;

public sealed class ReportExportService : IReportExportService
{
    private readonly MySqlMonitorService _monitor;

    public ReportExportService(MySqlMonitorService monitor)
    {
        _monitor = monitor;
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public async Task ExportSalesSummaryAsync(DateTime from, DateTime to, string filePath, bool usePdf)
    {
        var rows = await _monitor.GetSalesSummaryAsync(from, to.AddDays(1)).ConfigureAwait(false);
        if (usePdf)
            ExportSalesPdf(rows, from, to, filePath);
        else
            ExportSalesExcel(rows, from, to, filePath);
    }

    public async Task ExportStockLedgerAsync(DateTime from, DateTime to, string filePath, bool usePdf)
    {
        var rows = await _monitor.GetStockLedgerAsync(from, to.AddDays(1)).ConfigureAwait(false);
        if (usePdf)
            ExportLedgerPdf(rows, from, to, filePath);
        else
            ExportLedgerExcel(rows, from, to, filePath);
    }

    private static void ExportSalesExcel(
        IReadOnlyList<MySqlMonitorService.SalesSummaryRow> rows,
        DateTime from,
        DateTime to,
        string filePath)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Продажи");

        sheet.Cell(1, 1).Value = "NurMarketKassa — сводка продаж";
        sheet.Range(1, 1, 1, 3).Merge().Style.Font.SetBold().Font.SetFontSize(14);

        sheet.Cell(2, 1).Value = $"Период: {from:dd.MM.yyyy} — {to:dd.MM.yyyy}";

        sheet.Cell(4, 1).Value = "Дата";
        sheet.Cell(4, 2).Value = "Чеков";
        sheet.Cell(4, 3).Value = "Сумма, сом";
        sheet.Range(4, 1, 4, 3).Style.Font.SetBold();

        var rowIndex = 5;
        var totalChecks = 0;
        decimal totalAmount = 0;

        foreach (var row in rows)
        {
            sheet.Cell(rowIndex, 1).Value = row.Date;
            sheet.Cell(rowIndex, 1).Style.DateFormat.Format = "dd.MM.yyyy";
            sheet.Cell(rowIndex, 2).Value = row.SaleCount;
            sheet.Cell(rowIndex, 3).Value = row.TotalAmount;
            sheet.Cell(rowIndex, 3).Style.NumberFormat.Format = "#,##0.00";
            totalChecks += row.SaleCount;
            totalAmount += row.TotalAmount;
            rowIndex++;
        }

        sheet.Cell(rowIndex, 1).Value = "ИТОГО";
        sheet.Cell(rowIndex, 2).Value = totalChecks;
        sheet.Cell(rowIndex, 3).Value = totalAmount;
        sheet.Range(rowIndex, 1, rowIndex, 3).Style.Font.SetBold();

        sheet.Columns().AdjustToContents();
        workbook.SaveAs(filePath);
    }

    private static void ExportLedgerExcel(
        IReadOnlyList<MySqlMonitorService.StockLedgerRow> rows,
        DateTime from,
        DateTime to,
        string filePath)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("stock_ledger");

        sheet.Cell(1, 1).Value = "NurMarketKassa — журнал остатков";
        sheet.Range(1, 1, 1, 6).Merge().Style.Font.SetBold().Font.SetFontSize(14);
        sheet.Cell(2, 1).Value = $"Период: {from:dd.MM.yyyy} — {to:dd.MM.yyyy}";

        var headers = new[] { "Время", "Товар ID", "Дельта", "Причина", "Ссылка", "Устройство" };
        for (var i = 0; i < headers.Length; i++)
            sheet.Cell(4, i + 1).Value = headers[i];
        sheet.Range(4, 1, 4, headers.Length).Style.Font.SetBold();

        var rowIndex = 5;
        foreach (var row in rows)
        {
            sheet.Cell(rowIndex, 1).Value = row.CreatedAt;
            sheet.Cell(rowIndex, 1).Style.DateFormat.Format = "dd.MM.yyyy HH:mm:ss";
            sheet.Cell(rowIndex, 2).Value = row.ProductId;
            sheet.Cell(rowIndex, 3).Value = row.Delta;
            sheet.Cell(rowIndex, 4).Value = row.Reason;
            sheet.Cell(rowIndex, 5).Value = row.ReferenceId;
            sheet.Cell(rowIndex, 6).Value = row.DeviceName;
            rowIndex++;
        }

        sheet.Columns().AdjustToContents();
        workbook.SaveAs(filePath);
    }

    private static void ExportSalesPdf(
        IReadOnlyList<MySqlMonitorService.SalesSummaryRow> rows,
        DateTime from,
        DateTime to,
        string filePath)
    {
        var totalChecks = rows.Sum(r => r.SaleCount);
        var totalAmount = rows.Sum(r => r.TotalAmount);

        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(32);
                page.DefaultTextStyle(x => x.FontSize(10));

                page.Header().Column(col =>
                {
                    col.Item().Text("NurMarketKassa").Bold().FontSize(18).FontColor(Colors.Blue.Darken2);
                    col.Item().Text("Сводка продаж").FontSize(12);
                    col.Item().Text($"Период: {from:dd.MM.yyyy} — {to:dd.MM.yyyy}").FontColor(Colors.Grey.Darken1);
                });

                page.Content().PaddingVertical(12).Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.RelativeColumn(2);
                        columns.RelativeColumn();
                        columns.RelativeColumn();
                    });

                    table.Header(header =>
                    {
                        header.Cell().Background(Colors.Grey.Lighten3).Padding(4).Text("Дата").Bold();
                        header.Cell().Background(Colors.Grey.Lighten3).Padding(4).Text("Чеков").Bold();
                        header.Cell().Background(Colors.Grey.Lighten3).Padding(4).Text("Сумма, сом").Bold();
                    });

                    foreach (var row in rows)
                    {
                        table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(4)
                            .Text(row.Date.ToString("dd.MM.yyyy"));
                        table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(4)
                            .Text(row.SaleCount.ToString());
                        table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(4)
                            .Text(row.TotalAmount.ToString("N2"));
                    }

                    table.Cell().Padding(4).Text("ИТОГО").Bold();
                    table.Cell().Padding(4).Text(totalChecks.ToString()).Bold();
                    table.Cell().Padding(4).Text(totalAmount.ToString("N2")).Bold();
                });

                page.Footer().AlignCenter().Text(text =>
                {
                    text.Span("Страница ");
                    text.CurrentPageNumber();
                    text.Span(" из ");
                    text.TotalPages();
                });
            });
        }).GeneratePdf(filePath);
    }

    private static void ExportLedgerPdf(
        IReadOnlyList<MySqlMonitorService.StockLedgerRow> rows,
        DateTime from,
        DateTime to,
        string filePath)
    {
        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(24);
                page.DefaultTextStyle(x => x.FontSize(9));

                page.Header().Column(col =>
                {
                    col.Item().Text("NurMarketKassa").Bold().FontSize(18).FontColor(Colors.Blue.Darken2);
                    col.Item().Text("Журнал stock_ledger").FontSize(12);
                    col.Item().Text($"Период: {from:dd.MM.yyyy} — {to:dd.MM.yyyy}").FontColor(Colors.Grey.Darken1);
                });

                page.Content().PaddingVertical(10).Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.RelativeColumn(2);
                        columns.RelativeColumn(2);
                        columns.RelativeColumn();
                        columns.RelativeColumn();
                        columns.RelativeColumn(2);
                        columns.RelativeColumn();
                    });

                    table.Header(header =>
                    {
                        foreach (var title in new[] { "Время", "Товар ID", "Дельта", "Причина", "Ссылка", "Устройство" })
                            header.Cell().Background(Colors.Grey.Lighten3).Padding(3).Text(title).Bold();
                    });

                    foreach (var row in rows)
                    {
                        table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(3)
                            .Text(row.CreatedAt.ToString("dd.MM.yyyy HH:mm"));
                        table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(3).Text(row.ProductId);
                        table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(3)
                            .Text(row.Delta.ToString("0.###"));
                        table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(3).Text(row.Reason);
                        table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(3).Text(row.ReferenceId);
                        table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(3).Text(row.DeviceName);
                    }
                });

                page.Footer().AlignCenter().Text(text =>
                {
                    text.Span("Страница ");
                    text.CurrentPageNumber();
                    text.Span(" из ");
                    text.TotalPages();
                });
            });
        }).GeneratePdf(filePath);
    }
}
