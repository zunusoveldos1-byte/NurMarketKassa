using System.Net.Http;

namespace NurMarketKassa.Services;

/// <summary>Фоновая проверка версии каталога каждые 2 минуты.</summary>
public sealed class CatalogBackgroundSyncService : IDisposable
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan RemindAfterPostpone = TimeSpan.FromMinutes(10);

    private readonly CancellationTokenSource _cts = new();
    private Task? _loopTask;
    private CatalogVersionInfo? _pendingRemoteVersion;
    private DateTimeOffset? _postponedUntil;
    private CatalogSyncButtonState _buttonState = CatalogSyncButtonState.Idle;

    public event EventHandler? UpdateAvailable;
    public event EventHandler<CatalogSyncButtonState>? ButtonStateChanged;

    public CatalogVersionInfo? PendingRemoteVersion => _pendingRemoteVersion;

    public CatalogSyncButtonState ButtonState => _buttonState;

    public void Start()
    {
        if (_loopTask != null)
            return;

        _loopTask = RunLoopAsync(_cts.Token);
        PosLogger.Log("CatalogBackgroundSyncService started (interval 2 min)", "CATALOG");
    }

    public void NotifyPostponed()
    {
        _postponedUntil = DateTimeOffset.Now.Add(RemindAfterPostpone);
        PosLogger.Log($"Catalog update postponed until {_postponedUntil:HH:mm:ss}", "CATALOG");
    }

    public void ClearPendingUpdate()
    {
        _pendingRemoteVersion = null;
        _postponedUntil = null;
        SetButtonState(CatalogSyncButtonState.Idle);
    }

    public void SetButtonState(CatalogSyncButtonState state)
    {
        if (_buttonState == state)
            return;
        _buttonState = state;
        ButtonStateChanged?.Invoke(this, state);
    }

    public async Task CheckNowAsync(CancellationToken ct = default)
    {
        if (OfflineModeHelper.UseLocalOperations)
            return;

        try
        {
            var remote = await CatalogCacheService.FetchRemoteVersionAsync(ct).ConfigureAwait(false);
            if (remote == null || remote.IsEmpty)
                return;

            if (CatalogCacheService.IsSameVersion(remote))
            {
                if (_pendingRemoteVersion == null)
                    SetButtonState(CatalogSyncButtonState.Idle);
                return;
            }

            if (string.IsNullOrWhiteSpace(CatalogCacheService.LocalCatalogVersionToken))
            {
                CatalogCacheService.SaveLocalVersionToken(remote.Token);
                PosLogger.Log($"Catalog version bootstrapped: {remote.Token}", "CATALOG");
                return;
            }

            if (_postponedUntil.HasValue && DateTimeOffset.Now < _postponedUntil.Value)
                return;

            _pendingRemoteVersion = remote;
            SetButtonState(CatalogSyncButtonState.UpdateAvailable);
            PosLogger.Log($"Catalog version changed: local={CatalogCacheService.LocalCatalogVersionToken} remote={remote.Token}", "CATALOG");
            UpdateAvailable?.Invoke(this, EventArgs.Empty);
        }
        catch (HttpRequestException)
        {
            /* офлайн — без предупреждения */
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            PosLogger.Log($"Catalog version check failed: {ex.Message}", "CATALOG");
        }
    }

    private async Task RunLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(CheckInterval);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await CheckNowAsync(ct).ConfigureAwait(false);
                await timer.WaitForNextTickAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                PosLogger.Log($"Catalog background loop: {ex.Message}", "CATALOG");
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(30), ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        try
        {
            _loopTask?.Wait(TimeSpan.FromSeconds(2));
        }
        catch
        {
            /* ignore */
        }

        _cts.Dispose();
    }
}
