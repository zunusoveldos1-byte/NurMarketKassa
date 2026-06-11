using System;
using System.Threading;

namespace NurMarketKassa.Services
{
    public static class GraphicReceiptPrinter
    {
        public static void Print(string text, GraphicReceiptSettings settings)
        {
            if (string.IsNullOrWhiteSpace(text))
                throw new InvalidOperationException("Текст чека пуст.");

            if (string.IsNullOrWhiteSpace(settings.DevicePath))
                throw new InvalidOperationException("Не указан порт принтера.");

            byte[] imageBytes = GraphicReceiptGenerator.GenerateReceiptImage(text, settings);

            int retries = Math.Clamp(settings.RetryCount, 1, 8);
            Exception? last = null;

            for (int i = 0; i < retries; i++)
            {
                try
                {
                    RawPrinterHelper.SendBytesToPrinter(settings.DevicePath, imageBytes);
                    return;
                }
                catch (Exception ex)
                {
                    last = ex;
                    Thread.Sleep(70 + 60 * i);
                }
            }

            throw new InvalidOperationException($"Не удалось напечатать графический чек: {last?.Message}", last);
        }
    }
}