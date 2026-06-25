using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using NurMarketKassa.Models;
using NurMarketKassa.Models.Pos;
using NurMarketKassa.Services;

namespace NurMarketKassa.Views;

public partial class FilterWindow : Window
{
  private static readonly string[] CatalogKinds = ["Все товары", "Весовые", "Штучные", "Только избранные ★"];
  private const string AllCategoriesLabel = "Все категории";
  private const string AllBrandsLabel = "Все бренды";

  public List<CatalogProductTileVm>? FilteredTiles { get; private set; }
  public FilterCriteria? Result { get; private set; }
  public bool FullCatalogReloaded { get; private set; }

  public FilterWindow()
  {
    InitializeComponent();
    CatalogKindCombo.ItemsSource = CatalogKinds;
    CatalogKindCombo.SelectedIndex = 0;
    LoadCategoryAndBrandOptions();
    RestoreFilterState();
  }

  private void LoadCategoryAndBrandOptions()
  {
    var repo = LocalProductRepository.Instance;
    var categories = new List<string> { AllCategoriesLabel };
    categories.AddRange(repo.GetDistinctCategories());
    CategoryCombo.ItemsSource = categories;
    CategoryCombo.SelectedIndex = 0;

    var brands = new List<string> { AllBrandsLabel };
    brands.AddRange(repo.GetDistinctBrands());
    BrandCombo.ItemsSource = brands;
    BrandCombo.SelectedIndex = 0;
  }

  private void RestoreFilterState()
  {
    var prefs = UserPreferences.Instance;
    SearchBox.Text = prefs.LastFilterSearchQuery ?? string.Empty;

    var kind = MapPrefsKindToUi(prefs.LastFilterCatalogKind);
    CatalogKindCombo.SelectedItem = CatalogKinds.Contains(kind) ? kind : CatalogKinds[0];

    if (!string.IsNullOrWhiteSpace(prefs.LastFilterCategory))
      CategoryCombo.SelectedItem = prefs.LastFilterCategory;
    if (!string.IsNullOrWhiteSpace(prefs.LastFilterBrand))
      BrandCombo.SelectedItem = prefs.LastFilterBrand;

    PriceMinBox.Text = prefs.LastFilterPriceMin?.ToString("0.##", CultureInfo.InvariantCulture) ?? string.Empty;
    PriceMaxBox.Text = prefs.LastFilterPriceMax?.ToString("0.##", CultureInfo.InvariantCulture) ?? string.Empty;
  }

  private void Apply_Click(object sender, RoutedEventArgs e)
  {
    Result = BuildCriteria();
    SavePreferences(Result);
    FilteredTiles = CatalogCacheService.ApplySqlFilter(Result).ToList();
    FullCatalogReloaded = false;
    DialogResult = true;
  }

  private void Reset_Click(object sender, RoutedEventArgs e)
  {
    ClearForm();
    ClearPreferences();
    CatalogCacheService.LoadFromDatabase();
    Result = null;
    FilteredTiles = null;
    FullCatalogReloaded = true;
    DialogResult = true;
  }

  private void Cancel_Click(object sender, RoutedEventArgs e)
  {
    DialogResult = false;
  }

  private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
  {
    if (e.ClickCount == 1)
      DragMove();
  }

  private void ClearForm()
  {
    SearchBox.Text = string.Empty;
    CatalogKindCombo.SelectedIndex = 0;
    CategoryCombo.SelectedIndex = 0;
    BrandCombo.SelectedIndex = 0;
    PriceMinBox.Text = string.Empty;
    PriceMaxBox.Text = string.Empty;
  }

  private FilterCriteria BuildCriteria()
  {
    var kind = CatalogKindCombo.SelectedItem as string ?? CatalogKinds[0];
    var category = CategoryCombo.SelectedItem as string;
    var brand = BrandCombo.SelectedItem as string;

    return new FilterCriteria
    {
      SearchQuery = string.IsNullOrWhiteSpace(SearchBox.Text) ? null : SearchBox.Text.Trim(),
      CatalogKind = kind,
      Category = category is AllCategoriesLabel or null ? null : category,
      Brand = brand is AllBrandsLabel or null ? null : brand,
      PriceMin = TryParsePrice(PriceMinBox.Text),
      PriceMax = TryParsePrice(PriceMaxBox.Text),
      OnlyFavorite = kind == "Только избранные ★",
      OnlyWeight = kind == "Весовые",
      OnlyPiece = kind == "Штучные"
    };
  }

  private static double? TryParsePrice(string? text)
  {
    if (string.IsNullOrWhiteSpace(text))
      return null;

    var normalized = text.Trim().Replace(',', '.');
    return double.TryParse(normalized, NumberStyles.Any, CultureInfo.InvariantCulture, out var value)
      ? value
      : null;
  }

  private static void SavePreferences(FilterCriteria criteria)
  {
    var prefs = UserPreferences.Instance;
    prefs.LastFilterSearchQuery = criteria.SearchQuery;
    prefs.LastFilterCatalogKind = criteria.CatalogKind ?? CatalogKinds[0];
    prefs.LastFilterCategory = criteria.Category;
    prefs.LastFilterBrand = criteria.Brand;
    prefs.LastFilterPriceMin = criteria.PriceMin;
    prefs.LastFilterPriceMax = criteria.PriceMax;
    prefs.LastFilterOnlyFavorite = criteria.OnlyFavorite;
    prefs.LastFilterOnlyWeight = criteria.OnlyWeight;
    prefs.LastFilterOnlyPiece = criteria.OnlyPiece;
    prefs.SaveToDisk();
  }

  private static void ClearPreferences()
  {
    var prefs = UserPreferences.Instance;
    prefs.LastFilterSearchQuery = null;
    prefs.LastFilterCatalogKind = CatalogKinds[0];
    prefs.LastFilterCategory = null;
    prefs.LastFilterBrand = null;
    prefs.LastFilterPriceMin = null;
    prefs.LastFilterPriceMax = null;
    prefs.LastFilterOnlyFavorite = false;
    prefs.LastFilterOnlyWeight = false;
    prefs.LastFilterOnlyPiece = false;
    prefs.SaveToDisk();
  }

  private static string MapPrefsKindToUi(string? saved)
  {
    if (string.IsNullOrWhiteSpace(saved))
      return CatalogKinds[0];

    return saved switch
    {
      "Все" => CatalogKinds[0],
      "Избранные" => "Только избранные ★",
      _ => saved
    };
  }
}
