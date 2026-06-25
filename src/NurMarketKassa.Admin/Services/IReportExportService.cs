namespace NurMarketKassa.Admin.Services;

public interface IReportExportService
{
    Task ExportSalesSummaryAsync(DateTime from, DateTime to, string filePath, bool usePdf);

    Task ExportStockLedgerAsync(DateTime from, DateTime to, string filePath, bool usePdf);
}
