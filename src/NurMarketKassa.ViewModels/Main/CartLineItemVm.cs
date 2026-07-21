namespace NurMarketKassa.ViewModels.Main;

/// <summary>Строка чека для панели корзины (Avalonia).</summary>
public sealed class CartLineItemVm : ViewModelBase
{
    private double _quantity;
    private double _lineTotal;

    public string ItemId { get; init; } = "";
    public string Title { get; init; } = "";
    public string Unit { get; init; } = "шт";
    public double UnitPrice { get; init; }

    public double Quantity
    {
        get => _quantity;
        set
        {
            if (!SetProperty(ref _quantity, value))
                return;
            LineTotal = UnitPrice * _quantity;
        }
    }

    public double LineTotal
    {
        get => _lineTotal;
        private set => SetProperty(ref _lineTotal, value);
    }

    public string QuantityDisplay => Quantity.ToString("0.###");
    public string LineTotalDisplay => $"{LineTotal:0.00} сом";
}
