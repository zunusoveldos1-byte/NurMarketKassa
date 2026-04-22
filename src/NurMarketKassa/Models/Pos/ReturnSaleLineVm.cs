using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace NurMarketKassa.Models.Pos;

/// <summary>Строка чека для возврата (с выбором галочкой).</summary>
public sealed class ReturnSaleLineVm : INotifyPropertyChanged
{
    private bool _isSelected;

    public required string LineId { get; init; }
    public required string Title { get; init; }
    public required string SubLine { get; init; }
    /// <summary>Строка вида «Сумма: 12,34 сом».</summary>
    public required string LineSumText { get; init; }
    public bool CanReturn { get; init; }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (!CanReturn && value)
                return;
            if (_isSelected == value)
                return;
            _isSelected = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
