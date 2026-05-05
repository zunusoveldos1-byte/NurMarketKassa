using System;
using System.Windows;
using System.Windows.Input;

#nullable disable

namespace NurMarketKassa.ViewModels
{
    public sealed class LoginViewModel
    {
        private readonly Func<MessageBoxResult> _confirmExit;
        private readonly Action _exitApplication;

        public LoginViewModel(Func<MessageBoxResult> confirmExit, Action exitApplication)
        {
            _confirmExit = confirmExit ?? throw new ArgumentNullException(nameof(confirmExit));
            _exitApplication = exitApplication ?? throw new ArgumentNullException(nameof(exitApplication));
            ExitApplicationCommand = new RelayCommand(ExitApplication);
        }

        public ICommand ExitApplicationCommand { get; }

        private void ExitApplication()
        {
            var result = _confirmExit();
            if (result is not MessageBoxResult.Yes)
                return;

            _exitApplication();
        }
    }
}