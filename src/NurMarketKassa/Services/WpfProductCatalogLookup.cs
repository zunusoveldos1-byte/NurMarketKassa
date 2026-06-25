using NurMarketKassa.Core.Contracts;
using NurMarketKassa.Core.Domain;
using NurMarketKassa.Models.Pos;

namespace NurMarketKassa.Services;

public sealed class WpfProductCatalogLookup : IProductCatalogLookup
{
    private readonly LocalProductRepository _catalog = LocalProductRepository.Instance;

    public CatalogProductInfo? FindByBarcode(string barcode)
    {
        var code = (barcode ?? "").Trim();
        if (code.Length == 0)
            return null;

        var tile = _catalog.TryGetTileByBarcode(code);
        return tile == null ? null : ToInfo(tile);
    }

    public CatalogProductInfo? FindByEmbeddedCode(string embeddedProductCode)
    {
        var code = (embeddedProductCode ?? "").Trim();
        if (code.Length == 0)
            return null;

        var normalized = code.TrimStart('0');
        if (normalized.Length == 0)
            normalized = code;

        foreach (var product in _catalog.LoadAllTiles())
        {
            var bc = product.Barcode?.Trim();
            if (string.IsNullOrEmpty(bc))
                continue;

            if (bc == code
                || bc.EndsWith(code, StringComparison.Ordinal)
                || bc.EndsWith(normalized, StringComparison.Ordinal)
                || bc.TrimStart('0') == normalized)
            {
                return ToInfo(product);
            }
        }

        return null;
    }

    private static CatalogProductInfo ToInfo(CatalogProductTileVm product) =>
        new(product.Id, product.Title, product.MustWeigh, product.Barcode, product.Quantity);
}
