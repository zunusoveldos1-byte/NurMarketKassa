using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using NurMarketKassa.Models;
using NurMarketKassa.Services;

namespace NurMarketKassa.Views;

public partial class ShiftsHistoryWindow : Window, INotifyPropertyChanged
{
    private bool _isLoading;

    public ObservableCollection<ShiftHistoryEntry> Shifts { get; } = new();

    public bool IsLoading
    {
        get => _isLoading;
        set { _isLoading = value; OnPropertyChanged(); }
    }

    public ShiftsHistoryWindow()
    {
        InitializeComponent();
        DataContext = this;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private async void Window_Loaded(object sender, RoutedEventArgs e) => await LoadShiftsAsync();

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await LoadShiftsAsync();

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private async Task LoadShiftsAsync()
    {
        IsLoading = true;
        try
        {
            var items = await ShiftHistoryService.LoadAsync().ConfigureAwait(true);
            Shifts.Clear();
            foreach (var item in items)
                Shifts.Add(item);
        }
        catch (Exception ex)
        {
            PosMessageBox.Show(this, $"Не удалось загрузить историю смен: {ex.Message}", "Смена",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            IsLoading = false;
        }
    }
}
