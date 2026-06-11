using System.IO;
using System.Text.Json;
using NurMarketKassa.Configuration;
using System.Windows;

namespace NurMarketKassa.Services
{
    public static class ReceiptPrintService
    {
        public static void PrintReceipt(
            string cartJson,
            string? offlineNote = null,
            string? paymentMethodKey = null,
            string? cashReceived = null)
        {
            // ===== ДИАГНОСТИКА =====
            try
            {
                string diagPath = Path.Combine(Path.GetTempPath(), $"cart_dump_{Guid.NewGuid():N}.json");
                File.WriteAllText(diagPath, cartJson);
                PosLogger.Log($"📁 JSON сохранён в: {diagPath}", "DIAG");
            }
            catch { /* игнорируем ошибки записи */ }

            try
            {
                using var doc = JsonDocument.Parse(cartJson);
                var root = doc.RootElement;
                var items = CartDisplayHelper.EnumerateItems(root).ToList();

                if (items.Count == 0)
                {
                    PosLogger.Log("⚠️ В корзине НЕТ позиций! cartJson пуст.", "DIAG");
                    return;
                }

                var first = items[0];
                var name = CartDisplayHelper.ItemName(first);
                var qty = CartDisplayHelper.LineQuantity(first);
                var price = CartDisplayHelper.UnitPrice(first);
                var total = CartDisplayHelper.LineTotal(first);
                PosLogger.Log($"📦 Первый товар: '{name}', кол-во: {qty}, цена: {price}, сумма: {total}", "DIAG");
            }
            catch (Exception ex)
            {
                PosLogger.Log($"❌ Ошибка разбора cartJson: {ex.Message}", "DIAG");
            }
            // =========================================

            var prefs = UserPreferences.Instance;
            if (!prefs.ReceiptEnabled) return;

            string textReceipt = CartReceiptTextBuilder.BuildSimpleReceipt(
                cartJson,
                offlineNote,
                paymentMethodKey,
                cashReceived);

            if (prefs.SelectedPrintMode == PrintMode.Text)
            {
                var settings = prefs.ToReceiptPrinterSettings();
                EscPosTextReceiptPrinter.Print(settings, textReceipt);
            }
            else
            {
                var graphicSettings = new GraphicReceiptSettings
                {
                    PaperWidthPixels = prefs.GraphicPaperWidthPixels,
                    FontFamily = prefs.GraphicFontFamily,
                    FontSize = prefs.GraphicFontSize,
                    DevicePath = prefs.ReceiptDevicePath,
                    RetryCount = prefs.ReceiptRetryCount,
                    ShowStoreName = prefs.ShowStoreName,
                    ShowAddress = prefs.ShowAddress,
                    ShowReceiptNumber = prefs.ShowReceiptNumber,
                    ShowDate = prefs.ShowDate,
                    ShowItems = prefs.ShowItems,
                    ShowTotal = prefs.ShowTotal,
                    ShowQrCode = prefs.ShowQrCode,
                    QrCodePath = prefs.QrCodePath
                };

                try
                {
                    byte[] receiptBytes = GraphicReceiptGenerator.GenerateReceiptImage(textReceipt, graphicSettings);
                    PosLogger.Log($"✅ Изображение сгенерировано. Размер: {receiptBytes.Length} байт.", "DIAG");

                    // ===== НАДЁЖНАЯ ЗАПИСЬ ЧЕРЕЗ FILESTREAM =====
                    using var fs = new FileStream(
                        graphicSettings.DevicePath,
                        FileMode.Open,
                        FileAccess.Write,
                        FileShare.ReadWrite);

                    fs.Write(receiptBytes, 0, receiptBytes.Length);
                    fs.Flush();

                    PosLogger.Log($"✅ Чек успешно отправлен в порт: {graphicSettings.DevicePath}", "DIAG");
                }
                catch (UnauthorizedAccessException ex)
                {
                    PosLogger.Log($"❌ ОШИБКА ДОСТУПА: {ex.Message}. Запустите приложение от имени администратора.", "DIAG");
                }
                catch (Exception ex)
                {
                    PosLogger.Log($"❌ Ошибка записи в порт: {ex.Message}", "DIAG");
                }
            }
        }
    }
}