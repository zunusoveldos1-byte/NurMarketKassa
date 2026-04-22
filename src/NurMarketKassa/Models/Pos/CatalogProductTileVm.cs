using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace NurMarketKassa.Models.Pos;

public sealed class CatalogProductTileVm : INotifyPropertyChanged
{
    public CatalogProductTileVm(string id, string title, string priceLine, bool mustWeigh, string? imageUrl = null)
    {
        Id = id;
        Title = title;
        PriceLine = priceLine;
        MustWeigh = mustWeigh;
        ImageUrl = imageUrl;
    }

    public string Id { get; }
    public string Title { get; }
    public string PriceLine { get; }
    public bool MustWeigh { get; }

    /// <summary>Абсолютный URL превью (если есть в list API).</summary>
    public string? ImageUrl { get; }

    private string? _barcode;
    public string? Barcode
    {
        get => _barcode;
        set
        {
            if (_barcode == value) return;
            _barcode = value;
            OnPropertyChanged();
        }
    }

    private ImageSource? _thumb;
    public ImageSource? Thumb
    {
        get => _thumb;
        set
        {
            if (ReferenceEquals(_thumb, value)) return;
            _thumb = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}