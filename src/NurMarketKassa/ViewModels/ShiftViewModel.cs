using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using NurMarketKassa.Services;


namespace NurMarketKassa.ViewModels
{
    public class ShiftViewModel : INotifyPropertyChanged
    {
        private readonly Func<Task> _openAction;
        private readonly Func<Task> _closeAction;
        private readonly Func<string> _cashierNameProvider;
        private readonly Func<decimal> _balanceProvider;
        private bool _isShiftOpen;
        private bool _isBusy;

        public ShiftViewModel(
            Func<Task> openAction,
            Func<Task> closeAction,
            Func<string> cashierNameProvider,
            Func<decimal> balanceProvider)
        {
            _openAction = openAction;
            _closeAction = closeAction;
            _cashierNameProvider = cashierNameProvider;
            _balanceProvider = balanceProvider;

            OpenShiftCommand = new AsyncRelayCommand(
                OpenShiftAsync,
                () => !IsShiftOpen && !IsBusy);
            CloseShiftCommand = new AsyncRelayCommand(
                CloseShiftAsync,
                () => IsShiftOpen && !IsBusy);
        }

        public bool IsShiftOpen
        {
            get => _isShiftOpen;
            set { _isShiftOpen = value; OnPropertyChanged(); }
        }

        public bool IsBusy
        {
            get => _isBusy;
            set { _isBusy = value; OnPropertyChanged(); CommandManager.InvalidateRequerySuggested(); }
        }

        public ICommand OpenShiftCommand { get; }
        public ICommand CloseShiftCommand { get; }

        public Task CheckActiveShiftAsync()
        {
            IsShiftOpen = !string.IsNullOrEmpty(App.ActiveShiftId);
            return Task.CompletedTask;
        }

        private async Task OpenShiftAsync()
        {
            if (IsBusy) return;
            var owner = Application.Current.MainWindow;
            if (PosMessageBox.Show(owner, "Открыть новую смену?", "Смена",
                    MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            IsBusy = true;
            try
            {
                await _openAction();
                IsShiftOpen = true;

                _ = AnalyticsService.SendAsync(new AnalyticsRecord
                {
                    ActionType = "Open",
                    Timestamp = DateTime.UtcNow,
                    CashierName = _cashierNameProvider(),
                    Balance = _balanceProvider(),
                    Device = Environment.MachineName
                });
            }
            catch (Exception ex)
            {
                PosMessageBox.Show(owner, ex.Message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Hand);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task CloseShiftAsync()
        {
            if (IsBusy) return;
            var owner = Application.Current.MainWindow;
            if (PosMessageBox.Show(owner, "Вы действительно хотите закрыть смену?", "Закрытие смены",
                    MessageBoxButton.YesNo, MessageBoxImage.Exclamation) != MessageBoxResult.Yes)
                return;

            IsBusy = true;
            try
            {
                await _closeAction();
                IsShiftOpen = false;

                _ = AnalyticsService.SendAsync(new AnalyticsRecord
                {
                    ActionType = "Close",
                    Timestamp = DateTime.UtcNow,
                    CashierName = _cashierNameProvider(),
                    Balance = _balanceProvider(),
                    Device = Environment.MachineName
                });
            }
            catch (Exception ex)
            {
                PosMessageBox.Show(owner, ex.Message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Hand);
            }
            finally
            {
                IsBusy = false;
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}