using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using NurMarketKassa.Services;
using NurMarketKassa.Views;

#nullable disable

namespace NurMarketKassa.Views
{
    public partial class FilterWindow : Window, INotifyPropertyChanged
    {
        private ObservableCollection<string> _categories = new ObservableCollection<string>();
        private string _selectedCategory;
        private ObservableCollection<string> _brands = new ObservableCollection<string>();
        private ObservableCollection<string> _filteredBrands = new ObservableCollection<string>();
        private string _selectedBrand;
        private bool _onlyWeight;
        private bool _onlyInStock;
        private bool _onlyFavorite;

        public ObservableCollection<string> Categories
        {
            get => _categories;
            set { _categories = value; OnPropertyChanged(); }
        }

        public string SelectedCategory
        {
            get => _selectedCategory;
            set { _selectedCategory = value; OnPropertyChanged(); UpdateBrands(); }
        }

        public ObservableCollection<string> Brands
        {
            get => _brands;
            set { _brands = value; OnPropertyChanged(); UpdateBrands(); }
        }

        public ObservableCollection<string> FilteredBrands
        {
            get => _filteredBrands;
            set { _filteredBrands = value; OnPropertyChanged(); }
        }

        public string SelectedBrand
        {
            get => _selectedBrand;
            set { _selectedBrand = value; OnPropertyChanged(); }
        }

        public bool OnlyWeight
        {
            get => _onlyWeight;
            set { _onlyWeight = value; OnPropertyChanged(); }
        }

        public bool OnlyInStock
        {
            get => _onlyInStock;
            set { _onlyInStock = value; OnPropertyChanged(); }
        }

        public bool OnlyFavorite
        {
            get => _onlyFavorite;
            set { _onlyFavorite = value; OnPropertyChanged(); }
        }

        public ObservableCollection<string> Statuses { get; } = new ObservableCollection<string>
        {
            "Оплачен", "Возврат", "В обработке", "Отменён"
        };

        public ObservableCollection<string> HotkeyGroups { get; } = new ObservableCollection<string>
        {
            "Основные", "Напитки", "Закуски"
        };

        public ObservableCollection<string> Clients { get; set; } = new ObservableCollection<string>();

        public FilterWindow()
        {
            InitializeComponent();
            DataContext = this;

            if (Categories.Count == 0)
                Categories = new ObservableCollection<string> { "Овощи", "Фрукты", "Напитки", "Хлеб" };

            if (Brands.Count == 0)
            {
                Brands = new ObservableCollection<string> { "Legenda", "Простоквашино", "Coca-Cola" };
                UpdateBrands();
            }
        }

        private void Apply_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void UpdateBrands()
        {
            if (string.IsNullOrEmpty(SelectedCategory) || Brands == null)
                FilteredBrands = new ObservableCollection<string>(Brands ?? Enumerable.Empty<string>());
            else
                FilteredBrands = new ObservableCollection<string>(Brands);
        }

        public FilterCriteria GetFilterCriteria()
        {
            return new FilterCriteria
            {
                DateFrom = DateFromPicker.SelectedDate,
                DateTo = DateToPicker.SelectedDate,
                Category = SelectedCategory,
                Brand = SelectedBrand,
                Client = ClientCombo.SelectedItem?.ToString(),
                Status = StatusCombo.SelectedItem as string,
                HotkeyGroup = HotkeyGroupCombo.SelectedItem as string,
                OnlyWeight = OnlyWeight,
                OnlyInStock = OnlyInStock,
                OnlyFavorite = OnlyFavorite
            };
        }

        private void Reset_Click(object sender, RoutedEventArgs e)
        {
            DateFromPicker.SelectedDate = null;
            DateToPicker.SelectedDate = null;
            SelectedCategory = null;
            SelectedBrand = null;
            ClientCombo.SelectedIndex = -1;
            StatusCombo.SelectedIndex = -1;
            HotkeyGroupCombo.SelectedIndex = -1;
            OnlyWeight = false;
            OnlyInStock = false;
            OnlyFavorite = false;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var prefs = UserPreferences.Instance;
            if (!string.IsNullOrEmpty(prefs.LastFilterCategory))
                CategoryCombo.SelectedItem = prefs.LastFilterCategory;
            if (!string.IsNullOrEmpty(prefs.LastFilterBrand))
                BrandCombo.SelectedItem = prefs.LastFilterBrand;
        }

        protected override void OnClosed(EventArgs e)
        {
            var prefs = UserPreferences.Instance;
            prefs.LastFilterCategory = (CategoryCombo.SelectedItem as string) ?? "";
            prefs.LastFilterBrand = (BrandCombo.SelectedItem as string) ?? "";
            prefs.SaveToDisk();
            base.OnClosed(e);
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}