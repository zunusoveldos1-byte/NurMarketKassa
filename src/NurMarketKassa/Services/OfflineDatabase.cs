using System.IO;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace NurMarketKassa.Services;

/// <summary>SQLite-хранилище офлайн-очереди (таблица pending_sales).</summary>
public static class OfflineDatabase
{
    private static readonly string DataDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data");
    private static readonly string DbPath = Path.Combine(DataDirectory, "offline.db");
    private static readonly object InitLock = new();
    private static readonly ReaderWriterLockSlim DbLock = new(LockRecursionPolicy.NoRecursion);
    private static bool _initialized;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static string DatabasePath => DbPath;

    public static void EnsureSchema()
    {
        lock (InitLock)
        {
            if (_initialized)
                return;

            Directory.CreateDirectory(DataDirectory);
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS pending_sales (
                    id TEXT PRIMARY KEY NOT NULL,
                    created_at TEXT NOT NULL,
                    receipt_number TEXT,
                    json_data TEXT NOT NULL,
                    sync_status TEXT NOT NULL
                );

                CREATE INDEX IF NOT EXISTS idx_pending_sales_status ON pending_sales(sync_status);
                CREATE INDEX IF NOT EXISTS idx_pending_sales_created ON pending_sales(created_at);
                """;
            command.ExecuteNonQuery();

            MigrateLegacyJsonIfNeeded(connection);
            _initialized = true;
        }
    }

    public static List<OfflineSaleEntry> LoadAll()
    {
        EnsureSchema();
        DbLock.EnterReadLock();
        try
        {
            var list = new List<OfflineSaleEntry>();
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT json_data FROM pending_sales ORDER BY created_at ASC";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var json = reader.GetString(0);
                var entry = JsonSerializer.Deserialize<OfflineSaleEntry>(json, JsonOpts);
                if (entry != null)
                    list.Add(entry);
            }

            return list;
        }
        finally
        {
            DbLock.ExitReadLock();
        }
    }

    public static void SaveAll(IReadOnlyList<OfflineSaleEntry> items)
    {
        EnsureSchema();
        DbLock.EnterWriteLock();
        try
        {
            using var connection = OpenConnection();
            using var tx = connection.BeginTransaction();

            var incomingIds = new HashSet<string>(
                items.Select(i => i.Id),
                StringComparer.OrdinalIgnoreCase);

            var toDelete = new List<string>();
            using (var existingCmd = connection.CreateCommand())
            {
                existingCmd.Transaction = tx;
                existingCmd.CommandText = "SELECT id FROM pending_sales";
                using var reader = existingCmd.ExecuteReader();
                while (reader.Read())
                {
                    var id = reader.GetString(0);
                    if (!incomingIds.Contains(id))
                        toDelete.Add(id);
                }
            }

            foreach (var id in toDelete)
            {
                using var delete = connection.CreateCommand();
                delete.Transaction = tx;
                delete.CommandText = "DELETE FROM pending_sales WHERE id = $id";
                delete.Parameters.AddWithValue("$id", id);
                delete.ExecuteNonQuery();
            }

            foreach (var entry in items)
                UpsertEntry(connection, tx, entry);

            tx.Commit();
        }
        finally
        {
            DbLock.ExitWriteLock();
        }
    }

    private static void UpsertEntry(SqliteConnection connection, SqliteTransaction tx, OfflineSaleEntry entry)
    {
        using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = """
            INSERT INTO pending_sales (id, created_at, receipt_number, json_data, sync_status)
            VALUES ($id, $createdAt, $receiptNumber, $jsonData, $syncStatus)
            ON CONFLICT(id) DO UPDATE SET
                created_at = excluded.created_at,
                receipt_number = excluded.receipt_number,
                json_data = excluded.json_data,
                sync_status = excluded.sync_status
            """;
        command.Parameters.AddWithValue("$id", entry.Id);
        command.Parameters.AddWithValue("$createdAt", entry.CreatedAt.ToString("O"));
        command.Parameters.AddWithValue("$receiptNumber", BuildReceiptNumber(entry));
        command.Parameters.AddWithValue("$jsonData", JsonSerializer.Serialize(entry, JsonOpts));
        command.Parameters.AddWithValue("$syncStatus", entry.Status ?? OfflineSaleEntry.PendingSync);
        command.ExecuteNonQuery();
    }

    public static void Append(OfflineSaleEntry entry)
    {
        EnsureSchema();
        DbLock.EnterWriteLock();
        try
        {
            using var connection = OpenConnection();
            using var tx = connection.BeginTransaction();
            UpsertEntry(connection, tx, entry);
            tx.Commit();
        }
        finally
        {
            DbLock.ExitWriteLock();
        }
    }

    public static void UpdateEntry(OfflineSaleEntry entry)
    {
        EnsureSchema();
        DbLock.EnterWriteLock();
        try
        {
            using var connection = OpenConnection();
            using var tx = connection.BeginTransaction();
            UpsertEntry(connection, tx, entry);
            tx.Commit();
        }
        finally
        {
            DbLock.ExitWriteLock();
        }
    }

    public static void RemoveIds(IEnumerable<string> ids)
    {
        EnsureSchema();
        var set = ids.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (set.Count == 0)
            return;

        DbLock.EnterWriteLock();
        try
        {
            using var connection = OpenConnection();
            using var tx = connection.BeginTransaction();
            foreach (var id in set)
            {
                using var command = connection.CreateCommand();
                command.Transaction = tx;
                command.CommandText = "DELETE FROM pending_sales WHERE id = $id";
                command.Parameters.AddWithValue("$id", id);
                command.ExecuteNonQuery();
            }

            tx.Commit();
        }
        finally
        {
            DbLock.ExitWriteLock();
        }
    }

    private static SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection($"Data Source={DbPath}");
        connection.Open();
        return connection;
    }

    private static string BuildReceiptNumber(OfflineSaleEntry entry) =>
        entry.CreatedAt.LocalDateTime.ToString("yyyyMMdd-HHmmss");

    private static void MigrateLegacyJsonIfNeeded(SqliteConnection connection)
    {
        using var countCmd = connection.CreateCommand();
        countCmd.CommandText = "SELECT COUNT(*) FROM pending_sales";
        var count = Convert.ToInt64(countCmd.ExecuteScalar());
        if (count > 0)
            return;

        var legacyPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "NurMarketKassa",
            "offline_sales_pending.json");
        if (!File.Exists(legacyPath))
            return;

        try
        {
            var legacy = JsonSerializer.Deserialize<List<OfflineSaleEntry>>(File.ReadAllText(legacyPath), JsonOpts)
                         ?? new List<OfflineSaleEntry>();
            if (legacy.Count == 0)
                return;

            foreach (var entry in legacy)
            {
                using var insert = connection.CreateCommand();
                insert.CommandText = """
                    INSERT OR IGNORE INTO pending_sales (id, created_at, receipt_number, json_data, sync_status)
                    VALUES ($id, $createdAt, $receiptNumber, $jsonData, $syncStatus)
                    """;
                insert.Parameters.AddWithValue("$id", entry.Id);
                insert.Parameters.AddWithValue("$createdAt", entry.CreatedAt.ToString("O"));
                insert.Parameters.AddWithValue("$receiptNumber", BuildReceiptNumber(entry));
                insert.Parameters.AddWithValue("$jsonData", JsonSerializer.Serialize(entry, JsonOpts));
                insert.Parameters.AddWithValue("$syncStatus", entry.Status ?? OfflineSaleEntry.PendingSync);
                insert.ExecuteNonQuery();
            }

            var backup = legacyPath + ".migrated";
            File.Move(legacyPath, backup, overwrite: true);
            PosLogger.Log($"Мигрировано {legacy.Count} офлайн-чеков в SQLite: {DbPath}", "OFFLINE");
        }
        catch (Exception ex)
        {
            PosLogger.Log($"Миграция offline_sales_pending.json: {ex.Message}", "OFFLINE");
        }
    }
}
