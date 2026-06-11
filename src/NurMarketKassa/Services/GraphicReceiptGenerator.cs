using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Text;

namespace NurMarketKassa.Services
{
    [SupportedOSPlatform("windows")]
    public class GraphicReceiptSettings
    {
        public int PaperWidthPixels { get; set; } = 384;
        public bool PrintQrCode { get; set; } = false;
        public string QrCodePath { get; set; } = "";
        public string FontFamily { get; set; } = "Arial";
        public string DevicePath { get; set; } = "LPT1";
        public int RetryCount { get; set; } = 3;
        public float FontSize { get; set; } = 16f;

        public bool ShowStoreName { get; set; } = true;
        public bool ShowAddress { get; set; } = true;
        public bool ShowReceiptNumber { get; set; } = true;
        public bool ShowDate { get; set; } = true;
        public bool ShowItems { get; set; } = true;
        public bool ShowTotal { get; set; } = true;
        public bool ShowQrCode { get; set; } = false;
    }

    [SupportedOSPlatform("windows")]
    public static class GraphicReceiptGenerator
    {
        public static byte[] GenerateReceiptImage(string receiptText, GraphicReceiptSettings settings)
        {
            var rawLines = receiptText.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            var lines = new List<string>();

            foreach (var line in rawLines)
            {
                string trimmed = line.Trim();
                if (string.IsNullOrWhiteSpace(trimmed))
                    continue;

                if (!settings.ShowStoreName && trimmed.StartsWith("MARKET PLUS", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!settings.ShowAddress && trimmed.Contains("ул.", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!settings.ShowReceiptNumber && trimmed.StartsWith("Чек №", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!settings.ShowDate && trimmed.StartsWith("Дата", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!settings.ShowItems && (trimmed.Contains("x") || trimmed.Contains("Товар") || trimmed.Contains("Цена")))
                    continue;
                if (!settings.ShowTotal && (trimmed.StartsWith("ИТОГО", StringComparison.OrdinalIgnoreCase) || trimmed.StartsWith("К ОПЛАТЕ", StringComparison.OrdinalIgnoreCase)))
                    continue;
                if (!settings.ShowQrCode && (trimmed.Contains("QR") || trimmed.Contains("Код")))
                    continue;

                lines.Add(trimmed);
            }

            if (lines.Count == 0)
                lines.Add("Чек");

            using var font = new Font(settings.FontFamily, settings.FontSize, FontStyle.Bold);

            int lineHeight = (int)(settings.FontSize * 1.4);
            // ===== ИЗМЕНЕНИЕ: Увеличили высоту на 80 пикселей для отступа снизу =====
            int totalHeight = lines.Count * lineHeight + 50 + (settings.ShowQrCode ? 120 : 0) + 80;

            using var bitmap = new Bitmap(settings.PaperWidthPixels, totalHeight);
            using var g = Graphics.FromImage(bitmap);
            g.Clear(Color.White);

            float y = 10;
            foreach (var line in lines)
            {
                g.DrawString(line, font, Brushes.Black, new PointF(10, y));
                y += lineHeight;
            }

            if (settings.ShowQrCode && !string.IsNullOrEmpty(settings.QrCodePath) && System.IO.File.Exists(settings.QrCodePath))
            {
                using var qrImage = Image.FromFile(settings.QrCodePath);
                int qrSize = 100;
                using var scaledQr = new Bitmap(qrImage, qrSize, qrSize);
                g.DrawImage(scaledQr, (settings.PaperWidthPixels - qrSize) / 2, y + 10, qrSize, qrSize);
            }

            return ConvertToEscPosRaster(bitmap);
        }

        private static byte[] ConvertToEscPosRaster(Bitmap bitmap)
        {
            int width = bitmap.Width;
            int height = bitmap.Height;
            int widthBytes = (width + 7) / 8;

            var rasterBytes = new byte[widthBytes * height];

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    var pixel = bitmap.GetPixel(x, y);
                    if (pixel.R + pixel.G + pixel.B < 384)
                    {
                        int byteIndex = y * widthBytes + (x / 8);
                        int bitIndex = 7 - (x % 8);
                        rasterBytes[byteIndex] |= (byte)(1 << bitIndex);
                    }
                }
            }

            var command = new List<byte>
            {
                0x1D, 0x76, 0x30, 0x00,
                (byte)(widthBytes % 256), (byte)(widthBytes / 256),
                (byte)(height % 256), (byte)(height / 256)
            };
            command.AddRange(rasterBytes);

            using var ms = new MemoryStream();
            // ESC @ (инициализация принтера)
            ms.WriteByte(0x1B);
            ms.WriteByte(0x40);
            // Растровые данные
            ms.Write(command.ToArray(), 0, command.Count);

            // ===== ИЗМЕНЕНИЕ: ПОДАЧА БУМАГИ (4 строки) ПЕРЕД ОТРЕЗКОЙ =====
            ms.WriteByte(0x1B);
            ms.WriteByte(0x64);
            ms.WriteByte(0x04);
            // ============================================================

            // GS V m (отрезка)
            ms.WriteByte(0x1D);
            ms.WriteByte(0x56);
            ms.WriteByte(0x00);

            return ms.ToArray();
        }
    }
}