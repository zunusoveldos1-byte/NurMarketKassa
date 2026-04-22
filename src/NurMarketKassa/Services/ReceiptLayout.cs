namespace NurMarketKassa.Services;

/// <summary>Ширина текста чека в символах под термоленту 58 мм (стандартно 32 колонки ESC/POS).</summary>
internal static class ReceiptLayout
{
    internal static int CharWidth
    {
        get
        {
            try
            {
                var v = Environment.GetEnvironmentVariable("DESKTOP_MARKET_RECEIPT_WIDTH")?.Trim();
                if (int.TryParse(v, out var w) && w is >= 24 and <= 48)
                    return w;
            }
            catch
            {
                /* ignore */
            }

            return 32;
        }
    }
}
