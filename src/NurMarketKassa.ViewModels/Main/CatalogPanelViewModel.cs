using System.Collections.ObjectModel;
using System.Windows.Input;

namespace NurMarketKassa.ViewModels.Main;

/// <summary>Левая зона: каталог товаров (вкладки, поиск, сетка).</summary>
public sealed class CatalogPanelViewModel : ViewModelBase
{
    private int _selectedTabIndex;
    private string _searchText = "";
    private bool _isLoading;
    private string _statusText = "Каталог будет загружен после подключения сервисов.";
    private string _productCountText = "";

    public CatalogPanelViewModel()
    {
        ClearSearchCommand = new RelayCommand(ClearSearch, () => !string.IsNullOrWhiteSpace(SearchText));
        RefreshCatalogCommand = new AsyncRelayCommand(RefreshCatalogAsync, () => !IsLoading);
        OpenFilterCommand = new RelayCommand(() => { /* модуль «Фильтр» */ });
        OpenWarehouseCommand = new RelayCommand(() => { /* модуль «Склад» */ });
        Products.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasProducts));
    }

    public ObservableCollection<CatalogTabVm> Tabs { get; } =
    [
        new CatalogTabVm("Все товары", "\uE8B7"),
        new CatalogTabVm("Весовые", "\uE9D9"),
        new CatalogTabVm("Штучные", "\uE7B8"),
    ];

    /// <summary>Плейсхолдер до подключения <see cref="Catalog.CatalogViewModel"/>.</summary>
    public ObservableCollection<CatalogProductPlaceholderVm> Products { get; } = new();

    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set => SetProperty(ref _selectedTabIndex, value);
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (!SetProperty(ref _searchText, value ?? ""))
                return;
            (ClearSearchCommand as RelayCommand)?.RaiseCanExecuteChanged();
            // Модуль «Каталог» — debounced search через CatalogViewModel.
        }
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (!SetProperty(ref _isLoading, value))
                return;
            (RefreshCatalogCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        }
    }

    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value ?? "");
    }

    public string ProductCountText
    {
        get => _productCountText;
        set => SetProperty(ref _productCountText, value ?? "");
    }

    public bool HasProducts => Products.Count > 0;

    public ICommand ClearSearchCommand { get; }
    public ICommand RefreshCatalogCommand { get; }
    public ICommand OpenFilterCommand { get; }
    public ICommand OpenWarehouseCommand { get; }

    private void ClearSearch() => SearchText = "";

    private async Task RefreshCatalogAsync()
    {
        IsLoading = true;
        try
        {
            // Модуль «Каталог» — ICatalogCacheService + CatalogViewModel.
            await Task.Delay(300).ConfigureAwait(true);
            StatusText = "Обновление каталога будет доступно после регистрации ICatalogCacheService.";
        }
        finally
        {
            IsLoading = false;
        }
    }
}

public sealed class CatalogTabVm
{
    public CatalogTabVm(string title, string iconGlyph)
    {
        Title = title;
        IconGlyph = iconGlyph;
    }

    public string Title { get; }
    public string IconGlyph { get; }
}

public sealed class CatalogProductPlaceholderVm
{
    public string Title { get; init; } = "";
    public string PriceLine { get; init; } = "";
    public string StockInfo { get; init; } = "";
}
