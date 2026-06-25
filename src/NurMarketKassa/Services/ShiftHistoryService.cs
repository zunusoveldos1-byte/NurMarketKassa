using System.Globalization;
using System.Text.Json;
using NurMarketKassa.Models;

namespace NurMarketKassa.Services;

public static class ShiftHistoryService
{
    public static async Task<IReadOnlyList<ShiftHistoryEntry>> LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var payload = await App.ShiftApi.ConstructionShiftsListAsync(cancellationToken).ConfigureAwait(false);
            return Parse(payload);
        }
        catch
        {
            return Array.Empty<ShiftHistoryEntry>();
        }
    }

    public static IReadOnlyList<ShiftHistoryEntry> Parse(JsonElement payload)
    {
        var list = new List<ShiftHistoryEntry>();
        foreach (var row in EnumerateRows(payload))
        {
            if (row.ValueKind != JsonValueKind.Object)
                continue;

            var id = CartDisplayHelper.TryCartId(row) ?? "";
            if (string.IsNullOrEmpty(id))
                continue;

            var isOpen = RowLooksLikeOpenShift(row);
            list.Add(new ShiftHistoryEntry
            {
                ShiftNumber = id,
                OpenedAt = TryReadDate(row, "opened_at", "open_time", "started_at", "created_at"),
                ClosedAt = TryReadDate(row, "closed_at", "close_time", "ended_at", "finished_at"),
                Cashier = TryReadCashier(row),
                Status = isOpen ? "Активна" : "Закрыта",
                Revenue = TryReadRevenue(row),
            });
        }

        return list
            .OrderByDescending(s => s.OpenedAt ?? DateTime.MinValue)
            .ToList();
    }

    private static IEnumerable<JsonElement> EnumerateRows(JsonElement data)
    {
        if (data.ValueKind == JsonValueKind.Array)
        {
            foreach (var el in data.EnumerateArray())
                yield return el;
            yield break;
        }

        if (data.ValueKind == JsonValueKind.Object &&
            data.TryGetProperty("results", out var results) &&
            results.ValueKind == JsonValueKind.Array)
        {
            foreach (var el in results.EnumerateArray())
                yield return el;
        }
    }

    private static bool RowLooksLikeOpenShift(JsonElement row)
    {
        if (row.TryGetProperty("is_open", out var open) && open.ValueKind == JsonValueKind.True)
            return true;

        if (row.TryGetProperty("status", out var status) && IsOpenStatus(status))
            return true;

        if (row.TryGetProperty("state", out var state) && IsOpenStatus(state))
            return true;

        return false;
    }

    private static bool IsOpenStatus(JsonElement v)
    {
        if (v.ValueKind != JsonValueKind.String)
            return false;
        var s = v.GetString()?.Trim().ToLowerInvariant() ?? "";
        return s is "open" or "active" or "opened" or "in_progress";
    }

    private static DateTime? TryReadDate(JsonElement row, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!row.TryGetProperty(key, out var v))
                continue;

            if (v.ValueKind == JsonValueKind.String &&
                DateTime.TryParse(v.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var dt))
                return dt;

            if (v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var unix) && unix > 1_000_000_000)
                return DateTimeOffset.FromUnixTimeSeconds(unix).LocalDateTime;
        }

        return null;
    }

    private static string TryReadCashier(JsonElement row)
    {
        foreach (var key in new[] { "cashier", "user", "operator", "opened_by" })
        {
            if (!row.TryGetProperty(key, out var v))
                continue;

            if (v.ValueKind == JsonValueKind.String)
            {
                var s = v.GetString()?.Trim();
                if (!string.IsNullOrEmpty(s))
                    return s;
            }

            if (v.ValueKind == JsonValueKind.Object)
            {
                foreach (var sub in new[] { "name", "full_name", "username", "id" })
                {
                    if (v.TryGetProperty(sub, out var sv) && sv.ValueKind == JsonValueKind.String)
                    {
                        var s = sv.GetString()?.Trim();
                        if (!string.IsNullOrEmpty(s))
                            return s;
                    }
                }
            }
        }

        return App.CurrentUserId ?? "—";
    }

    private static decimal TryReadRevenue(JsonElement row)
    {
        foreach (var key in new[] { "revenue", "total_sales", "sales_total", "total_amount", "turnover" })
        {
            if (TryReadDecimal(row, key) is { } v)
                return v;
        }

        if (row.TryGetProperty("totals", out var totals) && totals.ValueKind == JsonValueKind.Object)
        {
            foreach (var key in new[] { "revenue", "sales", "total" })
            {
                if (TryReadDecimal(totals, key) is { } v)
                    return v;
            }
        }

        return 0m;
    }

    private static decimal? TryReadDecimal(JsonElement obj, string prop)
    {
        if (!obj.TryGetProperty(prop, out var v))
            return null;

        if (v.ValueKind == JsonValueKind.Number && v.TryGetDecimal(out var d))
            return d;

        if (v.ValueKind == JsonValueKind.String &&
            decimal.TryParse(v.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out d))
            return d;

        return null;
    }
}
