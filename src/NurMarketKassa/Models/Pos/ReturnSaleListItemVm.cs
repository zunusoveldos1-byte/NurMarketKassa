namespace NurMarketKassa.Models.Pos;

/// <summary>Строка в списке продаж (для выбора чека возврата).</summary>
public sealed class ReturnSaleListItemVm
{
    public required string SaleId { get; init; }
    public required string Summary { get; init; }
}
