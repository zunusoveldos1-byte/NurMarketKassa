using NurMarketKassa.Services;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace NurMarketKassa.ViewModels
{
    public class BankAccount
    {
        public string BankName { get; set; } = "";
        public string QrCodeImagePath { get; set; } = "";
        public string LogoPath { get; set; } = "";
    }

    public class CheckoutViewModel : INotifyPropertyChanged
    {
        private readonly double _totalDue;
        private string _paymentMethod = "cash";
        private string _cashReceived = "";
        private string _errorMessage = "";
        private bool _isPrintReceiptEnabled = true;
        private BankAccount? _selectedBank;
        private ObservableCollection<BankAccount> _banks = new();
        private string _changeDisplay = "";

        public CheckoutViewModel(double totalDue)
        {
            _totalDue = totalDue;
            TotalDueString = $"К оплате: {totalDue.ToString("0.00", CultureInfo.InvariantCulture)} сом";
            CashReceived = totalDue.ToString("0.00", CultureInfo.InvariantCulture);

            PayCommand = new RelayCommand(ExecutePay, () => CanExecutePay(null)); // ← исправлено
            CancelCommand = new RelayCommand(() => RequestClose?.Invoke(false));

            LoadBanks();
            UpdatePaymentMode();
        }

        // ---------- Свойства ----------
        public string TotalDueString { get; }

        public bool IsCash
        {
            get => _paymentMethod == "cash";
            set { if (value) PaymentMethod = "cash"; }
        }

        public bool IsTransfer
        {
            get => _paymentMethod == "transfer";
            set { if (value) PaymentMethod = "transfer"; }
        }

        public string PaymentMethod
        {
            get => _paymentMethod;
            private set
            {
                if (_paymentMethod == value) return;
                _paymentMethod = value;
                OnPropertyChanged();
                UpdatePaymentMode();
                ErrorMessage = "";
                CommandManager.InvalidateRequerySuggested(); // обновить кнопку
            }
        }

        public string CashReceived
        {
            get => _cashReceived;
            set
            {
                if (_cashReceived == value) return;
                _cashReceived = value;
                OnPropertyChanged();
                UpdateChangeDisplay();
                ErrorMessage = "";
                CommandManager.InvalidateRequerySuggested();
            }
        }

        public string ChangeDisplay
        {
            get => _changeDisplay;
            private set { _changeDisplay = value; OnPropertyChanged(); }
        }

        public string ErrorMessage
        {
            get => _errorMessage;
            set
            {
                _errorMessage = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasError));
            }
        }

        public bool IsPrintReceiptEnabled
        {
            get => _isPrintReceiptEnabled;
            set { _isPrintReceiptEnabled = value; OnPropertyChanged(); }
        }

        public bool IsCashMode => _paymentMethod == "cash";
        public bool IsBankSelectionVisible => _paymentMethod == "transfer";

        public ObservableCollection<BankAccount> Banks
        {
            get => _banks;
            set { _banks = value; OnPropertyChanged(); }
        }

        public BankAccount? SelectedBank
        {
            get => _selectedBank;
            set
            {
                _selectedBank = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(QrCodePath));
                OnPropertyChanged(nameof(HasQrCode));
                ErrorMessage = "";
                CommandManager.InvalidateRequerySuggested();
            }
        }

        public string? QrCodePath =>
            string.IsNullOrEmpty(SelectedBank?.QrCodeImagePath) ? null : SelectedBank.QrCodeImagePath;

        public bool HasQrCode => !string.IsNullOrEmpty(QrCodePath);
        public bool HasChangeDisplay => !string.IsNullOrEmpty(ChangeDisplay);
        public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

        // Команды
        public ICommand PayCommand { get; }
        public ICommand CancelCommand { get; }

        public event Action<bool>? RequestClose;

        private bool CanExecutePay(object? _)
        {
            if (PaymentMethod == "cash")
            {
                // наличные: сумма ≥ итого
                string normalized = CheckoutValidation.NormalizeDecimal(CashReceived);
                if (string.IsNullOrEmpty(normalized) ||
                    !double.TryParse(normalized, NumberStyles.Any, CultureInfo.InvariantCulture, out double cash))
                    return false;
                return cash >= _totalDue - 1E-09;
            }
            else // безнал
            {
                return SelectedBank != null && HasQrCode;
            }
        }

        private void ExecutePay()
        {
            if (PaymentMethod == "cash")
            {
                string? error = CheckoutValidation.ValidateCashReceived(CashReceived, _totalDue);
                if (error != null)
                {
                    ErrorMessage = error;
                    return;
                }
            }
            else
            {
                if (SelectedBank == null)
                {
                    ErrorMessage = "Выберите банк для безналичной оплаты.";
                    return;
                }
                if (!HasQrCode)
                {
                    ErrorMessage = $"Для банка \"{SelectedBank.BankName}\" не загружен QR‑код.\nПожалуйста, загрузите QR‑код в настройках.";
                    return;
                }
            }
            RequestClose?.Invoke(true);
        }

        private void UpdatePaymentMode()
        {
            OnPropertyChanged(nameof(IsCash));
            OnPropertyChanged(nameof(IsTransfer));
            OnPropertyChanged(nameof(IsCashMode));
            OnPropertyChanged(nameof(IsBankSelectionVisible));
            UpdateChangeDisplay();
        }

        private void UpdateChangeDisplay()
        {
            if (!IsCash)
            {
                ChangeDisplay = "";
                OnPropertyChanged(nameof(HasChangeDisplay));
                return;
            }

            string normalized = CheckoutValidation.NormalizeDecimal(CashReceived);
            if (string.IsNullOrEmpty(normalized) ||
                !double.TryParse(normalized, NumberStyles.Any, CultureInfo.InvariantCulture, out double cash))
            {
                ChangeDisplay = "";
                OnPropertyChanged(nameof(HasChangeDisplay));
                return;
            }

            if (cash > _totalDue + 1E-09)
            {
                double change = cash - _totalDue;
                ChangeDisplay = $"Сдача: {change.ToString("0.00", CultureInfo.InvariantCulture)} сом";
            }
            else
            {
                ChangeDisplay = "";
            }
            OnPropertyChanged(nameof(HasChangeDisplay));
        }

        private void LoadBanks()
        {
            var prefs = UserPreferences.Instance;
            prefs.BankQrPaths ??= new Dictionary<string, string>();

            var bankNames = new[] { "Элкарт", "MBank", "ФинкаБанк" };
            var logoNames = new Dictionary<string, string>
            {
                { "Элкарт", "Elkart-logo.png" },
                { "MBank", "Mbank-logo.png" },
                { "ФинкаБанк", "Finca-logo.png" }
            };

            var list = new ObservableCollection<BankAccount>();
            foreach (var name in bankNames)
            {
                string qrPath = prefs.BankQrPaths.TryGetValue(name, out var path) ? path : "";
                string logoPath = $"pack://application:,,,/Assets/{logoNames[name]}";

                list.Add(new BankAccount
                {
                    BankName = name,
                    QrCodeImagePath = qrPath,
                    LogoPath = logoPath
                });
            }

            Banks = list;
            if (Banks.Count > 0)
                SelectedBank = Banks[0];
        }

        // INPC
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}