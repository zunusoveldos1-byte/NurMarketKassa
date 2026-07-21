namespace NurMarketKassa.Services;

public sealed class StockLineStatus
{
    public double Warehouse { get; init; }
    public double Reserved { get; init; }
    public double Available { get; init; }
    public double LineQty { get; init; }
    public bool IsInsufficient { get; init; }
}
