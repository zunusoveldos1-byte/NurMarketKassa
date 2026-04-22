using System.IO;
using System.Linq;
using System.Text.Json;

namespace NurMarketKassa.Services;

public static class OfflinePendingSalesStore
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };
    private static readonly object SyncRoot = new();
    private static List<OfflineSaleEntry>? _cache;

    private static string FilePath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "NurMarketKassa",
            "offline_sales_pending.json");

    public static List<OfflineSaleEntry> LoadAll()
    {
        lock (SyncRoot)
        {
            if (_cache != null)
                return Clone(_cache);

            try
            {
                if (!File.Exists(FilePath))
                {
                    _cache = new List<OfflineSaleEntry>();
                    return new List<OfflineSaleEntry>();
                }

                _cache = JsonSerializer.Deserialize<List<OfflineSaleEntry>>(File.ReadAllText(FilePath), JsonOpts)
                         ?? new List<OfflineSaleEntry>();
                return Clone(_cache);
            }
            catch
            {
                _cache = new List<OfflineSaleEntry>();
                return new List<OfflineSaleEntry>();
            }
        }
    }

    public static void SaveAll(List<OfflineSaleEntry> items)
    {
        lock (SyncRoot)
        {
            var dir = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(items, JsonOpts));
            _cache = Clone(items);
        }
    }

    public static void Append(OfflineSaleEntry entry)
    {
        var all = LoadAll();
        all.Add(entry);
        SaveAll(all);
    }

    public static int PendingCount => LoadAll().Count(IsPendingLike);

    public static int SyncedCount => LoadAll().Count(s =>
        string.Equals(s.Status, OfflineSaleEntry.Synced, StringComparison.OrdinalIgnoreCase));

    public static int FailedCount => LoadAll().Count(s =>
        string.Equals(s.Status, OfflineSaleEntry.Failed, StringComparison.OrdinalIgnoreCase));

    public static IReadOnlyList<OfflineSaleEntry> LoadPendingForSync() =>
        LoadAll()
            .Where(IsPendingLike)
            .OrderBy(x => x.CreatedAt)
            .ToList();

    public static OfflineSaleEntry? TryGetById(string id) =>
        LoadAll().FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));

    public static void Update(string id, Action<OfflineSaleEntry> update)
    {
        var all = LoadAll();
        var entry = all.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
        if (entry == null)
            return;
        update(entry);
        SaveAll(all);
    }

    public static void MarkSyncing(string id)
    {
        Update(id, entry =>
        {
            entry.Status = OfflineSaleEntry.Syncing;
            entry.LastAttemptAt = DateTimeOffset.Now;
            entry.LastError = null;
        });
    }

    public static void MarkSynced(string id, string? saleId = null)
    {
        Update(id, entry =>
        {
            entry.Status = OfflineSaleEntry.Synced;
            entry.SyncedAt = DateTimeOffset.Now;
            entry.LastAttemptAt = entry.SyncedAt;
            entry.LastError = null;
            if (!string.IsNullOrWhiteSpace(saleId))
                entry.SyncedSaleId = saleId.Trim();
        });
    }

    public static void MarkFailed(string id, string error, bool retryable)
    {
        Update(id, entry =>
        {
            entry.Status = retryable ? OfflineSaleEntry.PendingSync : OfflineSaleEntry.Failed;
            entry.LastAttemptAt = DateTimeOffset.Now;
            entry.LastError = string.IsNullOrWhiteSpace(error) ? "Неизвестная ошибка синхронизации." : error.Trim();
        });
    }

    private static bool IsPendingLike(OfflineSaleEntry sale) =>
        string.Equals(sale.Status, OfflineSaleEntry.PendingSync, StringComparison.OrdinalIgnoreCase)
        || string.Equals(sale.Status, OfflineSaleEntry.Syncing, StringComparison.OrdinalIgnoreCase);

    private static List<OfflineSaleEntry> Clone(List<OfflineSaleEntry> items) =>
        items.Select(x => new OfflineSaleEntry
        {
            Id = x.Id,
            CreatedAt = x.CreatedAt,
            Status = x.Status,
            PaymentMethod = x.PaymentMethod,
            CashReceived = x.CashReceived,
            CartId = x.CartId,
            ShiftId = x.ShiftId,
            BranchId = x.BranchId,
            CashboxId = x.CashboxId,
            CartJson = x.CartJson,
            LastAttemptAt = x.LastAttemptAt,
            SyncedAt = x.SyncedAt,
            LastError = x.LastError,
            SyncedSaleId = x.SyncedSaleId,
        }).ToList();
}
