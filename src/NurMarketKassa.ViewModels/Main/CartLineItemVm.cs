using System.Globalization;
using System.Windows.Input;

namespace NurMarketKassa.ViewModels.Main;

/// <summary>
/// Этот файл описывает одну строку чека для панели корзины Avalonia-кассы:
/// название товара, количество, цену и сумму позиции.
/// </summary>
public sealed class CartLineItemVm : ViewModelBase
{
    private double _quantity;
    private double _lineTotal;

    public string ItemId { get; init; } = "";
    public string Title { get; init; } = "";
    public string Unit { get; init; } = "шт";
    public double UnitPrice { get; init; }
    public bool IsWeight { get; init; }

    public ICommand? RemoveCommand { get; init; }
    public ICommand? IncreaseCommand { get; init; }
    public ICommand? DecreaseCommand { get; init; }
    public ICommand? WeighCommand { get; init; }
    public ICommand? DiscountCommand { get; init; }

    public double Quantity
    {
        get => _quantity;
        set
        {
            if (!SetProperty(ref _quantity, value))
                return;
            LineTotal = UnitPrice * _quantity;
            OnPropertyChanged(nameof(QuantityDisplay));
            OnPropertyChanged(nameof(PriceQuantityLine));
        }
    }

    public double LineTotal
    {
        get => _lineTotal;
        set
        {
            if (!SetProperty(ref _lineTotal, value))
                return;
            OnPropertyChanged(nameof(LineTotalDisplay));
            OnPropertyChanged(nameof(LineTotalAmount));
        }
    }

    public string QuantityDisplay => IsWeight
        ? Quantity.ToString("0.000", CultureInfo.InvariantCulture)
        : Quantity.ToString("0.###", CultureInfo.InvariantCulture);

    public string UnitPriceDisplay => $"{UnitPrice.ToString("0.00", CultureInfo.InvariantCulture)} сом";

    /// <summary>Подпись вида «0.500 кг × 115.71 сом» или «1 шт × 150.00 сом».</summary>
    public string PriceQuantityLine =>
        $"{QuantityDisplay} {Unit} × {UnitPrice.ToString("0.00", CultureInfo.InvariantCulture)} сом";

    public string LineTotalDisplay => $"{LineTotal.ToString("0.00", CultureInfo.InvariantCulture)} сом";
    public string LineTotalAmount => LineTotal.ToString("0.00", CultureInfo.InvariantCulture);
}
