using NurMarketKassa.Core.Contracts;

namespace NurMarketKassa.Services;

public sealed class WpfStockAuditWriter : IStockAuditWriter
{
    public void LogStock(string productId, double quantity, string reason) =>
        App.AuditDb.LogStock(productId, quantity, reason);
}
