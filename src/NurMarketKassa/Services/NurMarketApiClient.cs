using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using NurMarketKassa.Configuration;
using NurMarketKassa.Models.Auth;
using NurMarketKassa.Models;

namespace NurMarketKassa.Services;

/// <summary>
/// HTTP-клиент к Nur CRM (логика как в PRINTER_EXE_CHAIN/api_client.py JwtClient).
/// </summary>
public sealed class NurMarketApiClient : IDisposable
{
    public const string AuthInvalidHintRu =
        "Сессия недействительна (часто из‑за входа с другого ПК или телефона). " +
        "Нажмите «Выйти» в кассе и войдите снова.";

    private readonly HttpClient _http;
    private readonly JsonSerializerOptions _jsonWrite = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private readonly JsonSerializerOptions _jsonRead = new() { PropertyNameCaseInsensitive = true };
    /// <summary>Исключает гонки при входе и ручном refresh.</summary>
    private readonly SemaphoreSlim _loginMutex = new(1, 1);
    /// <summary>Параллельные GET/POST к API (раньше один глобальный замок сильно замедлял каталог).</summary>
    private readonly SemaphoreSlim _httpSlots = new(Math.Min(12, Math.Max(4, Environment.ProcessorCount * 2)), Math.Min(12, Math.Max(4, Environment.ProcessorCount * 2)));
    /// <summary>Серийный refresh токена при 401 из параллельных запросов.</summary>
    private readonly SemaphoreSlim _refreshSync = new(1, 1);

    public string? AccessToken { get; private set; }
    public string? RefreshToken { get; private set; }
    public JsonElement UserPayload { get; private set; }
    public string? ActiveBranchId { get; private set; }

    public NurMarketApiClient(HttpClient http, AppSettings settings)
    {
        _http = http;
        var baseUrl = settings.ApiBaseUrl.Trim().TrimEnd('/') + "/";
        _http.BaseAddress = new Uri(baseUrl, UriKind.Absolute);
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        UserPayload = default;
    }

    /// <summary>Проверка доступности API (аналог can_reach_api).</summary>
    public async Task<bool> CanReachApiAsync(CancellationToken ct = default)
    {
        foreach (var path in new[] { "", "api/users/auth/login/" })
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, path);
                using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                if ((int)resp.StatusCode < 500)
                    return true;
            }
            catch
            {
                /* next */
            }
        }

        return false;
    }

    /// <summary>GET список товаров агента с остатками (для склада).</summary>
    public async Task<List<JsonElement>> GetAgentProductsAsync(CancellationToken ct = default)
    {
        foreach (var path in new[]
                 {
                     "api/main/agents/me/products/",
                     "api/main/products/agent-stock/",
                     "api/main/agents/products/",
                 })
        {
            try
            {
                var data = await RequestAsync(HttpMethod.Get, path, null, null, ct).ConfigureAwait(false);
                var list = UnwrapList(data);
                if (list.Count > 0)
                    return list;
            }
            catch (ApiException ex) when (ex.StatusCode is 404 or 405)
            {
                /* next path */
            }
        }

        return new List<JsonElement>();
    }

    /// <summary>Синхронизация статуса «избранный» с сайтом.</summary>
    public async Task<bool> SetProductFavoriteAsync(string productId, bool isFavorite, CancellationToken ct = default)
    {
        var pid = productId.Trim();
        if (pid.Length == 0)
            return false;

        var escaped = Uri.EscapeDataString(pid);
        var bodyFavorite = new Dictionary<string, string> { ["is_favorite"] = isFavorite ? "true" : "false" };
        var bodyToggle = new Dictionary<string, string>();

        var attempts = new List<(HttpMethod Method, string Path, Dictionary<string, string>? Body)>
        {
            (HttpMethod.Patch, $"api/main/products/{escaped}/", bodyFavorite),
            (HttpMethod.Patch, $"api/main/products/list/{escaped}/", bodyFavorite),
            (HttpMethod.Post, $"api/main/products/{escaped}/favorite/", bodyToggle),
            (HttpMethod.Post, $"api/main/products/list/{escaped}/favorite/", bodyToggle),
            (HttpMethod.Post, $"api/main/products/{escaped}/toggle-favorite/", bodyToggle),
            (HttpMethod.Put, $"api/main/products/{escaped}/favorite/", bodyFavorite),
        };

        foreach (var (method, path, body) in attempts)
        {
            try
            {
                await RequestAsync(method, path, body, null, ct).ConfigureAwait(false);
                return true;
            }
            catch (ApiException ex) when (ex.StatusCode is 404 or 405)
            {
                /* next */
            }
        }

        return false;
    }

    public async Task<JsonElement> LoginAsync(string email, string password, CancellationToken ct = default)
    {
        await _loginMutex.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var body = new LoginRequest { Email = email.Trim(), Password = password };
            using var content = new StringContent(JsonSerializer.Serialize(body, _jsonWrite), Encoding.UTF8, "application/json");
            using var resp = await _http.PostAsync("api/users/auth/login/", content, ct).ConfigureAwait(false);
            var text = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                JsonElement? payload = TryParse(text);
                throw new ApiException(ApiErrorParser.Parse(resp, text), (int)resp.StatusCode, payload);
            }

            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement.Clone();
            ApplyLoginResponse(root);
            return root;
        }
        finally
        {
            _loginMutex.Release();
        }
    }

    public async Task<bool> RefreshAccessAsync(CancellationToken ct = default)
    {
        await _loginMutex.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (string.IsNullOrEmpty(RefreshToken))
                return false;
            var body = new RefreshRequest { Refresh = RefreshToken };
            using var content = new StringContent(JsonSerializer.Serialize(body, _jsonWrite), Encoding.UTF8, "application/json");
            using var resp = await _http.PostAsync("api/users/auth/refresh/", content, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                return false;
            var text = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            if (root.TryGetProperty("access", out var acc) && acc.ValueKind == JsonValueKind.String)
            {
                AccessToken = acc.GetString();
                return true;
            }

            return false;
        }
        finally
        {
            _loginMutex.Release();
        }
    }

    public void ClearSession()
    {
        AccessToken = null;
        RefreshToken = null;
        UserPayload = default;
        ActiveBranchId = null;
    }

    /// <summary>GET /api/users/profile/</summary>
    public Task<JsonElement> GetProfileAsync(CancellationToken ct = default) =>
        RequestAsync(HttpMethod.Get, "api/users/profile/", null, null, ct);

    /// <summary>
    /// Универсальный запрос с Bearer и query branch=… (как branch_params() в Python).
    /// </summary>
    /// <param name="requestTimeout">Ограничение времени запроса (например scan 22 с).</param>
    /// 

    /// <summary>
    /// Оптимизированный метод для потоковой десериализации без создания промежуточных строк.
    /// Заменяет RequestAsync в горячих путях (каталог, поиск).
    /// </summary>
    public async Task<T?> RequestDataAsync<T>(
        HttpMethod method,
        string relativePath,
        object? jsonBody,
        IReadOnlyDictionary<string, string>? query,
        CancellationToken ct = default,
        TimeSpan? requestTimeout = null)
    {
        if (string.IsNullOrEmpty(AccessToken))
            throw new ApiException(AuthInvalidHintRu, 401);

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (requestTimeout.HasValue)
            linked.CancelAfter(requestTimeout.Value);

        await _httpSlots.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            var uri = BuildUri(relativePath, query);
            using var req = new HttpRequestMessage(method, uri);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AccessToken);
            if (jsonBody is not null)
            {
                var json = JsonSerializer.Serialize(jsonBody, _jsonWrite);
                req.Content = new StringContent(json, Encoding.UTF8, "application/json");
            }

            using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, linked.Token).ConfigureAwait(false);

            // Если 401, пробуем обновить токен и повторить
            if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized && !string.IsNullOrEmpty(RefreshToken))
            {
                if (await RefreshAccessUnlockedAsync(linked.Token).ConfigureAwait(false))
                {
                    // Повторяем запрос с новым токеном
                    req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AccessToken);
                    using var retryResp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, linked.Token).ConfigureAwait(false);
                    retryResp.EnsureSuccessStatusCode();
                    using var retryStream = await retryResp.Content.ReadAsStreamAsync(linked.Token).ConfigureAwait(false);
                    return await JsonSerializer.DeserializeAsync<T>(retryStream, _jsonRead, linked.Token).ConfigureAwait(false);
                }
                ClearSession();
                throw new ApiException(AuthInvalidHintRu, 401);
            }

            resp.EnsureSuccessStatusCode();

            // Потоковая десериализация (без лишних аллокаций)
            using var stream = await resp.Content.ReadAsStreamAsync(linked.Token).ConfigureAwait(false);
            return await JsonSerializer.DeserializeAsync<T>(stream, _jsonRead, linked.Token).ConfigureAwait(false);
        }
        finally
        {
            _httpSlots.Release();
        }
    }

    public async Task<JsonElement> RequestAsync(
        HttpMethod method,
        string relativePath,
        object? jsonBody,
        IReadOnlyDictionary<string, string>? query,
        CancellationToken ct = default,
        TimeSpan? requestTimeout = null)
    {
        if (string.IsNullOrEmpty(AccessToken))
            throw new ApiException(AuthInvalidHintRu, 401);

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (requestTimeout.HasValue)
            linked.CancelAfter(requestTimeout.Value);

        await _httpSlots.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            return await SendOnceAsync(method, relativePath, jsonBody, query, retryRefresh: true, linked.Token)
                .ConfigureAwait(false);
        }
        finally
        {
            _httpSlots.Release();
        }
    }

    private async Task<JsonElement> SendOnceAsync(
        HttpMethod method,
        string relativePath,
        object? jsonBody,
        IReadOnlyDictionary<string, string>? query,
        bool retryRefresh,
        CancellationToken ct)
    {
        var uri = BuildUri(relativePath, query);
        using var req = new HttpRequestMessage(method, uri);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AccessToken);
        if (jsonBody is not null)
        {
            var json = JsonSerializer.Serialize(jsonBody, _jsonWrite);
            req.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        var text = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized && retryRefresh && !string.IsNullOrEmpty(RefreshToken))
        {
            if (await RefreshAccessUnlockedAsync(ct).ConfigureAwait(false))
                return await SendOnceAsync(method, relativePath, jsonBody, query, retryRefresh: false, ct).ConfigureAwait(false);
            ClearSession();
        }
        else if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized && string.IsNullOrEmpty(RefreshToken))
        {
            ClearSession();
        }

        if (!resp.IsSuccessStatusCode)
        {
            var msg = resp.StatusCode == System.Net.HttpStatusCode.Unauthorized
                ? AuthInvalidHintRu
                : ApiErrorParser.Parse(resp, text);
            JsonElement? payload = TryParse(text);
            throw new ApiException(msg, (int)resp.StatusCode, payload);
        }

        if (string.IsNullOrWhiteSpace(text))
            return default;

        using var doc = JsonDocument.Parse(text);
        return doc.RootElement.Clone();
    }


    private async Task<bool> RefreshAccessUnlockedAsync(CancellationToken ct)
    {
        await _refreshSync.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (string.IsNullOrEmpty(RefreshToken))
                return false;
            var body = new RefreshRequest { Refresh = RefreshToken };
            using var content = new StringContent(JsonSerializer.Serialize(body, _jsonWrite), Encoding.UTF8, "application/json");
            using var resp = await _http.PostAsync("api/users/auth/refresh/", content, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                return false;
            var text = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            if (root.TryGetProperty("access", out var acc) && acc.ValueKind == JsonValueKind.String)
            {
                AccessToken = acc.GetString();
                return true;
            }

            return false;
        }
        finally
        {
            _refreshSync.Release();
        }
    }

    private Uri BuildUri(string relativePath, IReadOnlyDictionary<string, string>? query)
    {
        var path = relativePath.TrimStart('/');
        var qs = new List<string>();
        if (!string.IsNullOrEmpty(ActiveBranchId))
            qs.Add("branch=" + Uri.EscapeDataString(ActiveBranchId));
        if (query is not null)
        {
            foreach (var kv in query)
                qs.Add(Uri.EscapeDataString(kv.Key) + "=" + Uri.EscapeDataString(kv.Value ?? ""));
        }

        var rel = qs.Count == 0 ? path : path + "?" + string.Join("&", qs);
        return new Uri(_http.BaseAddress!, rel);
    }

    private void ApplyLoginResponse(JsonElement root)
    {
        if (root.TryGetProperty("access", out var a) && a.ValueKind == JsonValueKind.String)
            AccessToken = a.GetString();
        if (root.TryGetProperty("refresh", out var r) && r.ValueKind == JsonValueKind.String)
            RefreshToken = r.GetString();

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (var prop in root.EnumerateObject())
            {
                if (prop.NameEquals("access") || prop.NameEquals("refresh"))
                    continue;
                prop.WriteTo(writer);
            }

            writer.WriteEndObject();
        }

        var bytes = stream.ToArray();
        if (bytes.Length > 2)
        {
            using var doc = JsonDocument.Parse(bytes);
            UserPayload = doc.RootElement.Clone();
        }
        else
            UserPayload = default;

        SyncBranchFromUser();
    }

    /// <summary>
    /// После GET /api/users/profile/ — в JWT часто нет филиала; запросы с ?branch= должны использовать id из профиля.
    /// </summary>
    public void ApplyBranchFromProfile(JsonElement profile)
    {
        var bid = TryExtractBranchId(profile);
        if (!string.IsNullOrEmpty(bid))
            ActiveBranchId = bid;
    }

    private void SyncBranchFromUser() => ActiveBranchId = TryExtractBranchId(UserPayload);

    private static string? TryExtractBranchId(JsonElement user)
    {
        if (user.ValueKind != JsonValueKind.Object)
            return null;

        if (user.TryGetProperty("primary_branch_id", out var pb) && pb.ValueKind == JsonValueKind.String)
        {
            var s = pb.GetString();
            if (!string.IsNullOrEmpty(s))
                return s;
        }

        if (user.TryGetProperty("branch_ids", out var bids) && bids.ValueKind == JsonValueKind.Array)
        {
            foreach (var el in bids.EnumerateArray())
            {
                if (el.ValueKind == JsonValueKind.String)
                {
                    var s = el.GetString();
                    if (!string.IsNullOrEmpty(s))
                        return s;
                }
            }
        }

        return null;
    }

    private static JsonElement? TryParse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        try
        {
            using var d = JsonDocument.Parse(text);
            return d.RootElement.Clone();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>GET /api/construction/cashboxes/ — при 404 возвращает [], как в Python.</summary>
    public async Task<JsonElement> ConstructionCashboxesListAsync(CancellationToken ct = default)
    {
        try
        {
            return await RequestAsync(HttpMethod.Get, "api/construction/cashboxes/", null, null, ct).ConfigureAwait(false);
        }
        catch (ApiException ex) when (ex.StatusCode == 404)
        {
            using var d = JsonDocument.Parse("[]");
            return d.RootElement.Clone();
        }
    }

    /// <summary>GET /api/construction/shifts/</summary>
    public Task<JsonElement> ConstructionShiftsListAsync(CancellationToken ct = default) =>
        RequestAsync(HttpMethod.Get, "api/construction/shifts/", null, null, ct);

    /// <summary>POST открытия смены — два URL и два варианта тела (как construction_shift_open).</summary>
    public async Task<JsonElement> ConstructionShiftOpenAsync(
        string cashboxId,
        string openingCash = "0.00",
        CancellationToken ct = default)
    {
        var paths = new[] { "api/construction/shifts/open/", "api/construction/shift/open/" };
        var payloads = new[]
        {
            new Dictionary<string, string> { ["cashbox"] = cashboxId.Trim(), ["opening_cash"] = openingCash.Trim() },
            new Dictionary<string, string> { ["cashbox_id"] = cashboxId.Trim(), ["opening_cash"] = openingCash.Trim() },
        };

        ApiException? last = null;
        foreach (var path in paths)
        {
            for (var i = 0; i < payloads.Length; i++)
            {
                try
                {
                    return await RequestAsync(HttpMethod.Post, path, payloads[i], null, ct).ConfigureAwait(false);
                }
                catch (ApiException e)
                {
                    last = e;
                    if (e.StatusCode == 404)
                        break;
                    if (e.StatusCode == 400 && i + 1 < payloads.Length)
                        continue;
                    throw;
                }
            }
        }

        if (last != null)
            throw last;
        throw new ApiException("Не удалось открыть смену", 404);
    }

    /// <summary>POST закрытия смены — два URL (как construction_shift_close).</summary>
    public async Task<JsonElement> ConstructionShiftCloseAsync(
        string shiftId,
        string? closingCash = null,
        CancellationToken ct = default)
    {
        var sid = Uri.EscapeDataString(shiftId.Trim());
        var paths = new[]
        {
            $"api/construction/shifts/{sid}/close/",
            $"api/construction/shift/{sid}/close/",
        };

        var body = new Dictionary<string, string>();
        if (!string.IsNullOrWhiteSpace(closingCash))
            body["closing_cash"] = closingCash.Trim();

        ApiException? last = null;
        foreach (var path in paths)
        {
            try
            {
                return await RequestAsync(HttpMethod.Post, path, body, null, ct).ConfigureAwait(false);
            }
            catch (ApiException e)
            {
                last = e;
                if (e.StatusCode == 404)
                    continue;
                throw;
            }
        }

        if (last != null)
            throw last;
        throw new ApiException("Не удалось закрыть смену", 404);
    }

    /// <summary>POST /api/main/pos/sales/start/</summary>
    public Task<JsonElement> PosSalesStartAsync(string? cashboxId = null, CancellationToken ct = default)
    {
        var body = new Dictionary<string, string>();
        if (!string.IsNullOrWhiteSpace(cashboxId))
            body["cashbox_id"] = cashboxId.Trim();
        return RequestAsync(HttpMethod.Post, "api/main/pos/sales/start/", body, null, ct);
    }

    /// <summary>GET /api/main/pos/carts/{id}/</summary>
    public Task<JsonElement> PosCartGetAsync(string cartId, CancellationToken ct = default)
    {
        var id = Uri.EscapeDataString(cartId.Trim());
        return RequestAsync(HttpMethod.Get, $"api/main/pos/carts/{id}/", null, null, ct);
    }

    /// <summary>POST /api/main/pos/sales/{id}/scan/ — таймаут как в Python (3+22 с).</summary>
    public Task<JsonElement> PosScanAsync(string cartId, string barcode, string? quantity = null, CancellationToken ct = default)
    {
        var id = Uri.EscapeDataString(cartId.Trim());
        var body = new Dictionary<string, string> { ["barcode"] = barcode.Trim() };
        if (!string.IsNullOrEmpty(quantity))
            body["quantity"] = quantity;
        return RequestAsync(HttpMethod.Post, $"api/main/pos/sales/{id}/scan/", body, null, ct, TimeSpan.FromSeconds(22));
    }

    /// <summary>PATCH /api/main/pos/carts/{cart}/items/{item}/</summary>
    public Task<JsonElement> PosCartItemPatchAsync(
        string cartId,
        string itemId,
        IReadOnlyDictionary<string, string> body,
        CancellationToken ct = default)
    {
        var c = Uri.EscapeDataString(cartId.Trim());
        var i = Uri.EscapeDataString(itemId.Trim());
        return RequestAsync(HttpMethod.Patch, $"api/main/pos/carts/{c}/items/{i}/", body, null, ct);
    }

    /// <summary>DELETE /api/main/pos/carts/{cart}/items/{item}/</summary>
    public Task<JsonElement> PosCartItemDeleteAsync(string cartId, string itemId, CancellationToken ct = default)
    {
        var c = Uri.EscapeDataString(cartId.Trim());
        var i = Uri.EscapeDataString(itemId.Trim());
        return RequestAsync(HttpMethod.Delete, $"api/main/pos/carts/{c}/items/{i}/", null, null, ct);
    }

    /// <summary>
    /// POST checkout — два URL, таймаут до 90 с; при 400 без cash_received для безнала — повтор с 0.00 (как pos_checkout).
    /// </summary>
    public async Task<JsonElement> PosCheckoutAsync(
        string cartId,
        Dictionary<string, string> body,
        CancellationToken ct = default)
    {
        var id = Uri.EscapeDataString(cartId.Trim());
        var paths = new[]
        {
            $"api/main/pos/sales/{id}/checkout/",
            $"api/main/pos/carts/{id}/checkout/",
        };
        var timeout = TimeSpan.FromSeconds(90);
        ApiException? last404 = null;

        foreach (var path in paths)
        {
            try
            {
                return await RequestAsync(HttpMethod.Post, path, body, null, ct, timeout).ConfigureAwait(false);
            }
            catch (ApiException e)
            {
                if (e.StatusCode == 404)
                {
                    last404 = e;
                    continue;
                }

                var pm = body.GetValueOrDefault("payment_method") ?? "";
                if (e.StatusCode == 400
                    && !body.ContainsKey("cash_received")
                    && !string.Equals(pm, "cash", StringComparison.OrdinalIgnoreCase))
                {
                    var retry = new Dictionary<string, string>(body) { ["cash_received"] = "0.00" };
                    try
                    {
                        return await RequestAsync(HttpMethod.Post, path, retry, null, ct, timeout).ConfigureAwait(false);
                    }
                    catch (ApiException)
                    {
                        throw e;
                    }
                }

                throw;
            }
        }

        if (last404 != null)
            throw last404;
        throw new ApiException("Checkout: пустой список путей", 500);
    }

    /// <summary>GET /api/main/pos/sales/{id}/receipt/ — текст чека для печати.</summary>
    public Task<JsonElement> PosSaleReceiptAsync(string saleId, CancellationToken ct = default)
    {
        var id = Uri.EscapeDataString(saleId.Trim());
        return RequestAsync(HttpMethod.Get, $"api/main/pos/sales/{id}/receipt/", null, null, ct);
    }

    /// <summary>PATCH /api/main/pos/carts/{id}/ — скидка на чек и др.</summary>
    public Task<JsonElement> PosCartPatchAsync(string cartId, IReadOnlyDictionary<string, string> body, CancellationToken ct = default)
    {
        var c = Uri.EscapeDataString(cartId.Trim());
        return RequestAsync(HttpMethod.Patch, $"api/main/pos/carts/{c}/", body, null, ct);
    }

    /// <summary>POST /api/main/pos/sales/{id}/add-item/ — как pos_add_item (таймаут до 28 с).</summary>
    public Task<JsonElement> PosAddItemAsync(
        string cartId,
        string productId,
        string? quantity = null,
        string? unitPrice = null,
        string? discountTotal = null,
        CancellationToken ct = default)
    {
        var id = Uri.EscapeDataString(cartId.Trim());
        var body = new Dictionary<string, string> { ["product_id"] = productId.Trim() };
        if (!string.IsNullOrWhiteSpace(quantity))
            body["quantity"] = quantity.Trim();
        if (!string.IsNullOrWhiteSpace(unitPrice))
            body["unit_price"] = unitPrice.Trim();
        if (!string.IsNullOrWhiteSpace(discountTotal))
            body["discount_total"] = discountTotal.Trim();
        return RequestAsync(
            HttpMethod.Post,
            $"api/main/pos/sales/{id}/add-item/",
            body,
            null,
            ct,
            TimeSpan.FromSeconds(28));
    }

    /// <summary>Поиск товаров по названию (как products_search).</summary>
    /// <summary>Поиск товаров (быстрый, через потоковый парсинг).</summary>
    public async Task<List<ProductDto>> ProductsSearchAsync(string query, int limit = 40, CancellationToken ct = default)
    {
        var q = (query ?? "").Trim();
        if (q.Length == 0) return new List<ProductDto>();

        limit = Math.Clamp(limit, 1, 20000);
        var list = new List<ProductDto>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var page = 1;
        var hasNext = true;

        while (hasNext && list.Count < limit)
        {
            ct.ThrowIfCancellationRequested();
            var qs = new Dictionary<string, string>
            {
                ["search"] = q,
                ["page"] = page.ToString(CultureInfo.InvariantCulture),
                ["page_size"] = "300",
            };

            var response = await RequestDataAsync<ApiListResponse<ProductDto>>(
                HttpMethod.Get, "api/main/products/list/", null, qs, ct).ConfigureAwait(false);

            if (response?.Results == null || response.Results.Count == 0)
                break;

            foreach (var item in response.Results)
            {
                if (string.IsNullOrEmpty(item.Id) || !seen.Add(item.Id))
                    continue;
                list.Add(item);
                if (list.Count >= limit)
                    break;
            }

            hasNext = response.Results.Count >= 300;
            page++;
            if (page > 100)
                break;
        }

        return list;
    }

    /// <summary>Полный каталог с пагинацией (все SKU, до limit).</summary>
    public async Task<List<JsonElement>> ProductsCatalogAsync(int limit, int maxPages, CancellationToken ct = default)
    {
        var outList = new List<JsonElement>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var paths = new[] { "api/main/products/list/", "api/main/products/" };
        ApiException? last404 = null;
        limit = Math.Max(1, limit);
        maxPages = maxPages <= 0 ? 100 : Math.Max(1, maxPages);
        const int pageSize = 300;

        foreach (var path in paths)
        {
            outList.Clear();
            seen.Clear();

            try
            {
                var page = 1;
                var hasNext = true;

                while (hasNext && outList.Count < limit && page <= maxPages)
                {
                    ct.ThrowIfCancellationRequested();
                    var qs = new Dictionary<string, string>
                    {
                        ["page"] = page.ToString(CultureInfo.InvariantCulture),
                        ["page_size"] = pageSize.ToString(CultureInfo.InvariantCulture),
                    };

                    var data = await RequestAsync(HttpMethod.Get, path, null, qs, ct).ConfigureAwait(false);
                    var batch = UnwrapList(data);
                    if (batch.Count == 0)
                        break;

                    foreach (var p in batch)
                    {
                        var pid = TryProductIdString(p);
                        if (string.IsNullOrEmpty(pid) || !seen.Add(pid))
                            continue;
                        outList.Add(p);
                        if (outList.Count >= limit)
                            return outList;
                    }

                    hasNext = HasNextPage(data, batch.Count, pageSize);
                    page++;
                }

                if (outList.Count > 0)
                    return outList;
            }
            catch (ApiException e)
            {
                last404 = e;
                if (e.StatusCode != 404)
                    throw;
            }
        }

        if (last404 != null && outList.Count == 0)
            throw last404;
        return outList;
    }

    private static bool HasNextPage(JsonElement data, int batchCount, int pageSize)
    {
        if (data.ValueKind == JsonValueKind.Object)
        {
            if (data.TryGetProperty("next", out var next) && next.ValueKind == JsonValueKind.String)
            {
                var url = next.GetString();
                return !string.IsNullOrWhiteSpace(url);
            }

            if (data.TryGetProperty("count", out var countEl) && countEl.TryGetInt32(out var total))
            {
                // handled by caller via batch loop
            }
        }

        return batchCount >= pageSize;
    }

    /// <summary>Карточка товара с картинками (как products_detail).</summary>
    public async Task<JsonElement?> ProductsDetailAsync(string productId, CancellationToken ct = default)
    {
        var pid = Uri.EscapeDataString(productId.Trim());
        if (pid.Length == 0)
            return null;
        foreach (var path in new[] { $"api/main/products/{pid}/", $"api/main/products/list/{pid}/" })
        {
            try
            {
                var data = await RequestAsync(HttpMethod.Get, path, null, null, ct).ConfigureAwait(false);
                if (data.ValueKind != JsonValueKind.Object)
                    continue;
                if (data.TryGetProperty("data", out var inner) && inner.ValueKind == JsonValueKind.Object &&
                    inner.TryGetProperty("id", out _))
                    return inner.Clone();
                if (data.TryGetProperty("id", out _))
                    return data.Clone();
            }
            catch (ApiException e)
            {
                if (e.StatusCode is 404 or 405 or 410)
                    continue;
                return null;
            }
        }

        return null;
    }

    /// <summary>Список продаж (для выбора чека возврата). Пробует типовые GET с пагинацией.</summary>
    public async Task<List<JsonElement>> PosSalesListAsync(
        int page,
        int pageSize,
        string? cashboxId = null,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 5, 80);
        var pageStr = page.ToString(CultureInfo.InvariantCulture);
        var sizeStr = pageSize.ToString(CultureInfo.InvariantCulture);

        var queries = new List<Dictionary<string, string>>
        {
            new() { ["page"] = pageStr, ["page_size"] = sizeStr },
            new() { ["page"] = pageStr, ["limit"] = sizeStr },
            new() { ["page"] = pageStr, ["page_size"] = sizeStr, ["ordering"] = "-created_at" },
            new() { ["page"] = pageStr, ["page_size"] = sizeStr, ["ordering"] = "-id" },
        };

        if (!string.IsNullOrWhiteSpace(cashboxId))
        {
            var cb = cashboxId.Trim();
            queries.Add(new Dictionary<string, string> { ["page"] = pageStr, ["page_size"] = sizeStr, ["cashbox_id"] = cb });
            queries.Add(new Dictionary<string, string> { ["page"] = pageStr, ["limit"] = sizeStr, ["cashbox_id"] = cb });
        }

        var paths = new[] { "api/main/pos/sales/", "api/main/pos/sales/list/", "api/main/pos/sale/list/" };

        ApiException? last = null;
        var sawEmptySuccess = false;
        foreach (var path in paths)
        {
            foreach (var qs in queries)
            {
                try
                {
                    var data = await RequestAsync(HttpMethod.Get, path, null, qs, ct).ConfigureAwait(false);
                    var root = UnwrapListRootElement(data);
                    var list = UnwrapList(root);
                    if (list.Count > 0)
                        return list;
                    sawEmptySuccess = true;
                }
                catch (ApiException e)
                {
                    last = e;
                    if (e.StatusCode is 404 or 405 or 410)
                        break;
                    if (e.StatusCode == 400)
                        continue;
                    throw;
                }
            }
        }

        if (sawEmptySuccess)
            return new List<JsonElement>();
        if (last != null)
            throw last;
        return new List<JsonElement>();
    }

    private static JsonElement UnwrapListRootElement(JsonElement data)
    {
        if (data.ValueKind == JsonValueKind.Object && data.TryGetProperty("data", out var inner))
        {
            if (inner.ValueKind is JsonValueKind.Array or JsonValueKind.Object)
                return inner.Clone();
        }

        return data.Clone();
    }

    /// <summary>GET карточки продажи со строками (типовые пути).</summary>
    public async Task<JsonElement> PosSaleGetAsync(string saleId, CancellationToken ct = default)
    {
        var id = Uri.EscapeDataString(saleId.Trim());
        if (id.Length == 0)
            throw new ApiException("Укажите номер продажи (UUID или id из чека).", 400);

        var paths = new[]
        {
            $"api/main/pos/sales/{id}/",
            $"api/main/pos/sales/{id}",
        };

        ApiException? last = null;
        foreach (var path in paths)
        {
            try
            {
                var data = await RequestAsync(HttpMethod.Get, path, null, null, ct).ConfigureAwait(false);
                return UnwrapDataObject(data);
            }
            catch (ApiException e)
            {
                last = e;
                if (e.StatusCode is 404 or 405 or 410)
                    continue;
                throw;
            }
        }

        if (last != null)
            throw last;
        throw new ApiException("Чек не найден.", 404);
    }

    private static JsonElement UnwrapDataObject(JsonElement data)
    {
        if (data.ValueKind == JsonValueKind.Object && data.TryGetProperty("data", out var inner) &&
            inner.ValueKind == JsonValueKind.Object)
            return inner.Clone();
        return data.Clone();
    }

    /// <summary>Возврат одной позиции чека (частичный возврат по строке).</summary>
    public async Task<JsonElement> PosSaleLineRefundAsync(string saleId, string lineItemId, string? note, CancellationToken ct = default)
    {
        var sid = Uri.EscapeDataString(saleId.Trim());
        var lid = Uri.EscapeDataString(lineItemId.Trim());
        if (sid.Length == 0 || lid.Length == 0)
            throw new ApiException("Укажите продажу и строку чека.", 400);

        var n = (note ?? "").Trim();
        var bodyVariants = new List<Dictionary<string, string>>
        {
            new() { ["note"] = n, ["reason"] = n, ["comment"] = n },
            new() { ["reason"] = n, ["note"] = n },
            new() { ["note"] = n },
            new() { ["reason"] = n },
            new() { ["comment"] = n },
            new(),
        };

        // Тело с id строки (для эндпоинтов без id в пути)
        bodyVariants.Add(new Dictionary<string, string> { ["line_item_id"] = lineItemId.Trim(), ["note"] = n, ["reason"] = n });
        bodyVariants.Add(new Dictionary<string, string> { ["item_id"] = lineItemId.Trim(), ["note"] = n });
        bodyVariants.Add(new Dictionary<string, string> { ["sale_line_id"] = lineItemId.Trim(), ["reason"] = n });

        var paths = new[]
        {
            $"api/main/pos/sales/{sid}/items/{lid}/refund/",
            $"api/main/pos/sales/{sid}/items/{lid}/return/",
            $"api/main/pos/sales/{sid}/lines/{lid}/refund/",
            $"api/main/pos/sales/{sid}/line-items/{lid}/refund/",
            $"api/main/pos/sales/{sid}/refund-line/",
            $"api/main/pos/sales/{sid}/return-line/",
            $"api/main/pos/sale-lines/{lid}/refund/",
            $"api/main/pos/sale-items/{lid}/refund/",
        };

        var timeout = TimeSpan.FromSeconds(90);
        ApiException? last = null;
        foreach (var path in paths)
        {
            foreach (var body in bodyVariants)
            {
                try
                {
                    return await RequestAsync(HttpMethod.Post, path, body, null, ct, timeout).ConfigureAwait(false);
                }
                catch (ApiException e)
                {
                    last = e;
                    if (e.StatusCode is 404 or 405 or 410)
                        break;
                    if (e.StatusCode == 400)
                        continue;
                    throw;
                }
            }
        }

        if (last != null)
            throw last;
        throw new ApiException("Возврат строки: ни один из известных адресов API не ответил успешно.", 500);
    }

    /// <summary>
    /// Пробует оформить возврат / аннуляцию продажи по типовым путям API (совместимость с разными версиями CRM).
    /// </summary>
    public async Task<JsonElement> PosSaleRefundOrVoidAsync(string saleId, string? note, CancellationToken ct = default)
    {
        var id = Uri.EscapeDataString(saleId.Trim());
        if (id.Length == 0)
            throw new ApiException("Укажите номер продажи (UUID или id из чека).", 400);

        var n = (note ?? "").Trim();
        var bodyVariants = new[]
        {
            new Dictionary<string, string> { ["note"] = n, ["reason"] = n },
            new Dictionary<string, string> { ["note"] = n },
            new Dictionary<string, string> { ["reason"] = n },
            new Dictionary<string, string>(),
        };

        var paths = new[]
        {
            $"api/main/pos/sales/{id}/refund/",
            $"api/main/pos/sales/{id}/void/",
            $"api/main/pos/sales/{id}/return/",
            $"api/main/pos/sales/{id}/cancel/",
        };

        ApiException? last = null;
        foreach (var path in paths)
        {
            foreach (var body in bodyVariants)
            {
                try
                {
                    return await RequestAsync(HttpMethod.Post, path, body, null, ct, TimeSpan.FromSeconds(90))
                        .ConfigureAwait(false);
                }
                catch (ApiException e)
                {
                    last = e;
                    if (e.StatusCode is 404 or 405 or 410)
                        break;
                    if (e.StatusCode == 400)
                        continue;
                    throw;
                }
            }
        }

        if (last != null)
            throw last;
        throw new ApiException("Возврат: ни один из известных адресов API не ответил успешно.", 500);
    }

    /// <summary>Скачивание бинарника с авторизацией (превью с того же API).</summary>
    public async Task<byte[]?> DownloadAuthorizedAsync(string absoluteUrl, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(absoluteUrl))
            return null;
        if (string.IsNullOrEmpty(AccessToken))
            return null;

        await _httpSlots.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, absoluteUrl);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AccessToken);
            using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                return null;
            return await resp.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _httpSlots.Release();
        }
    }

    private static List<JsonElement> UnwrapList(JsonElement data)
    {
        var list = new List<JsonElement>();
        if (data.ValueKind == JsonValueKind.Array)
        {
            foreach (var el in data.EnumerateArray())
                list.Add(el.Clone());
            return list;
        }

        if (data.ValueKind == JsonValueKind.Object &&
            data.TryGetProperty("results", out var r) &&
            r.ValueKind == JsonValueKind.Array)
        {
            foreach (var el in r.EnumerateArray())
                list.Add(el.Clone());
        }

        return list;
    }

    public async Task<JsonElement> GetAsync(string relativePath, IReadOnlyDictionary<string, string>? query = null, CancellationToken ct = default)
    {
        return await RequestAsync(HttpMethod.Get, relativePath, null, query, ct).ConfigureAwait(false);
    }

    private static string? TryProductIdString(JsonElement p)
    {
        if (p.ValueKind != JsonValueKind.Object || !p.TryGetProperty("id", out var id))
            return null;
        return id.ValueKind switch
        {
            JsonValueKind.String => string.IsNullOrWhiteSpace(id.GetString()) ? null : id.GetString(),
            JsonValueKind.Number => id.GetRawText(),
            _ => null,
        };
    }

    public void Dispose()
    {
        _loginMutex.Dispose();
        _httpSlots.Dispose();
        _refreshSync.Dispose();
    }
}
