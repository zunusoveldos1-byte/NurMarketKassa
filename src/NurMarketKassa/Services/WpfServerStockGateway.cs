using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using NurMarketKassa.Core.Contracts;
using NurMarketKassa.Services.Api;

namespace NurMarketKassa.Services;

public sealed class WpfServerStockGateway : IServerStockGateway
{
    private readonly ICatalogApiService _catalogApi;

    public WpfServerStockGateway(ICatalogApiService catalogApi) => _catalogApi = catalogApi;

    public async Task<double?> GetServerStockAsync(string productId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(productId))
            return null;

        try
        {
            var detail = await _catalogApi.ProductsDetailAsync(productId, cancellationToken).ConfigureAwait(false);
            if (detail is not { } el)
                return null;

            var mustWeigh = CartDisplayHelper.ProductMustWeigh(el);
            return StockSyncService.ResolveStockQuantity(el, mustWeigh);
        }
        catch
        {
            return await TryReadFromAgentListAsync(productId, cancellationToken).ConfigureAwait(false);
        }
    }

    public Task<bool> TrySetServerStockAsync(
        string productId,
        double quantity,
        CancellationToken cancellationToken = default) =>
        _catalogApi.TrySetProductStockAsync(productId, quantity, cancellationToken);

    private async Task<double?> TryReadFromAgentListAsync(string productId, CancellationToken cancellationToken)
    {
        var list = await _catalogApi.GetAgentProductsAsync(cancellationToken).ConfigureAwait(false);
        foreach (var el in list)
        {
            if (!el.TryGetProperty("id", out var idEl))
                continue;

            var id = idEl.ValueKind == JsonValueKind.Number
                ? idEl.GetInt32().ToString()
                : (idEl.GetString() ?? "");

            if (!string.Equals(id, productId, StringComparison.OrdinalIgnoreCase))
                continue;

            if (el.TryGetProperty("quantity", out var qEl) && qEl.TryGetDouble(out var qty))
                return qty;
        }

        return null;
    }
}
