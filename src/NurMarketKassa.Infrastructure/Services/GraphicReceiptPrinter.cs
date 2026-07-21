namespace NurMarketKassa.Services;

public static class GraphicReceiptPrinter
{
    public static void Print(string text, GraphicReceiptSettings settings)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidOperationException("Текст чека пуст.");

        if (string.IsNullOrWhiteSpace(settings.DevicePath))
            throw new InvalidOperationException("Не указан порт принтера.");

        var imageBytes = GraphicReceiptGenerator.GenerateReceiptImage(text, settings);
        PrinterPortService.SendRawBytes(settings.DevicePath, imageBytes, settings.RetryCount);
    }
}
