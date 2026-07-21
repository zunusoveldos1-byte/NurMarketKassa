using System.Windows.Input;
using NurMarketKassa.Ui.Shared;

namespace NurMarketKassa.ViewModels.Main;

/// <summary>Верхняя панель: меню, пользователь, действия смены и настроек.</summary>
public sealed class MainToolbarViewModel : ViewModelBase
{
    private readonly IAppSession _session;
    private string _userTitle = "Касса";

    private readonly Func<Task>? _openShiftHandler;
    private readonly Func<Task>? _closeShiftHandler;

    public MainToolbarViewModel(
        IAppSession session,
        MainStatusViewModel status,
        Action toggleSideMenu,
        Action? toggleTheme = null,
        Action? toggleKeyboard = null,
        Func<Task>? openShiftHandler = null,
        Func<Task>? closeShiftHandler = null)
    {
        _session = session;
        Status = status;
        _openShiftHandler = openShiftHandler;
        _closeShiftHandler = closeShiftHandler;

        ToggleSideMenuCommand = new RelayCommand(toggleSideMenu);
        ToggleThemeCommand = new RelayCommand(() => toggleTheme?.Invoke());
        ToggleKeyboardCommand = new RelayCommand(() => toggleKeyboard?.Invoke());
        OpenShiftCommand = new AsyncRelayCommand(OpenShiftAsync, () => CanOpenShift);
        CloseShiftCommand = new AsyncRelayCommand(CloseShiftAsync, () => CanCloseShift);

        RefreshUserTitle();
    }

    public MainStatusViewModel Status { get; }

    public string UserTitle
    {
        get => _userTitle;
        private set => SetProperty(ref _userTitle, value);
    }

    public bool CanOpenShift => !_session.IsShiftOpen;
    public bool CanCloseShift => _session.IsShiftOpen;

    public ICommand ToggleSideMenuCommand { get; }
    public ICommand ToggleThemeCommand { get; }
    public ICommand ToggleKeyboardCommand { get; }
    public ICommand OpenShiftCommand { get; }
    public ICommand CloseShiftCommand { get; }

    public void RefreshUserTitle()
    {
        UserTitle = string.IsNullOrWhiteSpace(_session.PosCashboxDisplayName)
            ? "Касса — Nur Market"
            : _session.PosCashboxDisplayName!;
    }

    public void NotifyShiftStateChanged()
    {
        OnPropertyChanged(nameof(CanOpenShift));
        OnPropertyChanged(nameof(CanCloseShift));
        (OpenShiftCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (CloseShiftCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
    }

    private Task OpenShiftAsync() =>
        _openShiftHandler?.Invoke() ?? Task.CompletedTask;

    private Task CloseShiftAsync() =>
        _closeShiftHandler?.Invoke() ?? Task.CompletedTask;
}
