using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

#nullable enable
namespace NurMarketKassa.Models.Pos;

public sealed class CatalogProductTileVm : INotifyPropertyChanged
{
    private string? _barcode;
    private string? _stockInfo;
    private bool _isFavorite;
    private double _quantity;
    private ImageSource? _thumb;

    public CatalogProductTileVm(
      string id,
      string title,
      string priceLine,
      bool mustWeigh,
      string? imageUrl = null)
    {
        Id = id;
        Title = title;
        PriceLine = priceLine;
        MustWeigh = mustWeigh;
        ImageUrl = imageUrl;
        OnPropertyChanged(nameof(MustWeigh));
    }

    // ────────── существующие свойства ──────────
    public string? Category { get; set; }
    public string? Brand { get; set; }
    public string Id { get; }
    public string Title { get; }
    public string PriceLine { get; }
    public bool MustWeigh { get; set; }
    public string? ImageUrl { get; }
    public string? StatusDisplay { get; set; }
    public string? HotkeyGroupName { get; set; }

    // ────────── новые свойства для фильтрации ──────────
    public DateTime CreatedAt { get; set; }
    public string? ClientName { get; set; }
    public string? Status { get; set; }
    public string? HotkeyGroup { get; set; }

    public string? Barcode
    {
        get => _barcode;
        set
        {
            if (_barcode == value)
                return;
            _barcode = value;
            OnPropertyChanged(nameof(Barcode));
        }
    }

    public string? StockInfo
    {
        get => _stockInfo;
        set
        {
            _stockInfo = value;
            OnPropertyChanged(nameof(StockInfo));
        }
    }

    public bool IsFavorite
    {
        get => _isFavorite;
        set
        {
            _isFavorite = value;
            OnPropertyChanged(nameof(IsFavorite));
        }
    }

    public double Quantity
    {
        get => _quantity;
        set
        {
            _quantity = value;
            OnPropertyChanged(nameof(Quantity));
        }
    }

    public ImageSource? Thumb
    {
        get => _thumb;
        set
        {
            if (_thumb == value)
                return;
            _thumb = value;
            OnPropertyChanged(nameof(Thumb));
        }
    }

    public double PurchasePrice { get; set; }
    public string? Unit { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}