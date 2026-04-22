using System.IO;
using System.Text;
using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;
using System.Diagnostics;
using NurMarketKassa.Configuration;

namespace NurMarketKassa.Services;

public static class EscPosSelfCheckPrinter
{
    /// <summary>Самопроверка принтера (как print_printer_self_check_page в receipt_printer.py).</summary>
    public static void PrintSelfCheck(ReceiptPrinterSettings cfg)
    {
        var enc = (cfg.TextEncoding ?? "cp866").Trim().ToLowerInvariant();
        var ts = DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss");
        var w = ReceiptLayout.CharWidth;
        var lpt = (cfg.DevicePath ?? "LPT1").Trim();
        var lines = new[]
        {
            new string('=', w),
            "  САМОПРОВЕРКА (ПРИЛОЖЕНИЕ)",
            "  NurMarketKassa (C#)",
            new string('=', w),
            $"Дата/время: {ts}",
            new string('-', w),
            "Модель (цель): Cashino EP-200",
            "Версия прошивки: см. отчёт",
            "  принтера (у держ. кнопки)",
            new string('-', w),
            "В отчёте принтера часто:",
            "Код.стр. по умол.: CP936 GBK",
            "Это нормально. Печать из",
            "кассы: ESC % 0 + ESC t 46 +",
            "WPC1251 (байты cp1251).",
            new string('-', w),
            $"Кодировка текста: {enc}",
            new string('-', w),
            $"LPT: {lpt}",
            new string('-', w),
            "Кириллица (тест):",
            "АБВГДЕЖЗИЙКЛМНОПРСТУФХЦЧШЩЪЫЬЭЮЯ",
            "абвгдежзийклмнопрстуфхцчшщъыьэюя",
            new string('-', w),
            "Латиница:",
            "ABCDEFGHIJKLMNOPQRSTUVWXYZ",
            "abcdefghijklmnopqrstuvwxyz",
            new string('-', w),
            "Цифры: 0123456789",
            "Символы: !\"#$%&'()*+,-./:;<=>?",
            new string('-', w),
            "Штрихкод CODE39 (текстом):",
            "*123456*",
            new string('=', w),
            "Самопроверка завершена.",
            "",
        };
        EscPosTextReceiptPrinter.Print(cfg, string.Join("\n", lines));
    }
}

/// <summary>
/// Печать готового текста на ESC/POS через LPT (упрощённый аналог print_receipt_text в receipt_printer.py).
/// </summary>
public static class EscPosTextReceiptPrinter
{
    /// <summary>DESKTOP_MARKET_RECEIPT_NO_ESC_PCT=1 — не слать ESC % 0.</summary>
    private static bool NoEscPct() =>
        Environment.GetEnvironmentVariable("DESKTOP_MARKET_RECEIPT_NO_ESC_PCT")?.Trim().ToLowerInvariant() is "1" or "true" or "yes" or "on";

    /// <summary>Для старых LPT-принтеров сначала пробуем copy /b, потом прямую запись.</summary>
    private static bool PreferCopyCommand() =>
        Environment.GetEnvironmentVariable("DESKTOP_MARKET_RECEIPT_PREFER_COPY")?.Trim().ToLowerInvariant() is not ("0" or "false" or "no" or "off");

    public static void Print(ReceiptPrinterSettings cfg, string text)
    {
        ArgumentNullException.ThrowIfNull(cfg);
        var prepared = ReceiptSanitizer.StripShiftCashboxAndEnsureWelcome(text ?? string.Empty);
        var w = ReceiptLayout.CharWidth;
        var raw = ReceiptTextFormatter.FormatForPrinter(prepared, w).Trim().ToUpperInvariant();
        if (raw.Length == 0)
            throw new InvalidOperationException("Пустой текст чека.");

        ValidateSettings(cfg);

        PosLogger.Log($"EscPos Print: LPT={(cfg.DevicePath ?? "").Trim()}, encoding={cfg.TextEncoding}, len={raw.Length}",
            "PRINTER");

        var dev = HardwarePortHelper.NormalizeLptPort(cfg.DevicePath);
        if (dev.Length == 0)
            throw new InvalidOperationException("Не указан принтер (ReceiptPrinter.DevicePath).");

        var path = NormalizeDevicePath(dev);
        var encName = MapToDotNetEncoding(cfg.TextEncoding);
        Encoding encoding;
        try
        {
            encoding = Encoding.GetEncoding(encName);
        }
        catch (ArgumentException)
        {
            encoding = Encoding.GetEncoding(866);
        }

        var table = cfg.EscPosTableByte ?? DefaultEscPosTableByte(encName);
        var retries = Math.Clamp(cfg.RetryCount, 1, 8);
        Exception? last = null;

        for (var i = 0; i < retries; i++)
        {
            try
            {
                var payload = BuildReceiptBytes(encoding, raw, table, cfg.EscRByte, NoEscPct());
                TryWritePayload(path, payload);
                return;
            }
            catch (Exception ex)
            {
                last = ex;
                Thread.Sleep(70 + 60 * i);
            }
        }

        throw new InvalidOperationException($"Не удалось напечатать чек: {last?.Message}", last);
    }

    private static Stream OpenPort(string path)
    {
        try
        {
            return OpenDeviceStream(path);
        }
        catch (Exception ex)
        {
            /* Python receipt_printer: open("LPT1") — иногда без префикса \\.\ срабатывает иначе */
            if (path.StartsWith(@"\\.\", StringComparison.Ordinal) && path.Length > 4)
            {
                try
                {
                    return OpenDeviceStream(path[4..]);
                }
                catch
                {
                    /* fall through */
                }
            }

            throw new InvalidOperationException($"Не удалось открыть порт принтера «{path}».", ex);
        }
    }

    private static Stream OpenDeviceStream(string path)
    {
        try
        {
            return new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite, 4096, FileOptions.WriteThrough);
        }
        catch
        {
            var nativePath = path.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)
                ? $@"\\.\{path}"
                : path;
            var handle = CreateFile(
                nativePath,
                NativeGenericWrite,
                FileShare.ReadWrite,
                IntPtr.Zero,
                OpenExisting,
                0,
                IntPtr.Zero);

            if (handle.IsInvalid)
            {
                var err = Marshal.GetLastWin32Error();
                handle.Dispose();
                throw new IOException($"Windows не дал открыть устройство ({nativePath}), код {err}.");
            }

            return new FileStream(handle, FileAccess.Write);
        }
    }

    public static void ValidateSettings(ReceiptPrinterSettings cfg)
    {
        if (!HardwarePortHelper.LooksLikeLptPort(cfg.DevicePath))
            throw new InvalidOperationException("Для принтера укажите корректный LPT-порт, например LPT1.");

        if (cfg.RetryCount < 1)
            throw new InvalidOperationException("Количество повторов печати должно быть не меньше 1.");
    }

    private static byte[] BuildReceiptBytes(Encoding encoding, string text, int tableByte, int? escRByte, bool noEscPct)
    {
        using var ms = new MemoryStream(capacity: Math.Max(512, text.Length * 2));
        WriteReceipt(ms, encoding, text, tableByte, escRByte, noEscPct);
        return ms.ToArray();
    }

    private static void TryWritePayload(string path, byte[] payload)
    {
        if (PreferCopyCommand())
        {
            try
            {
                TryWritePayloadViaCopyCommand(path, payload);
                return;
            }
            catch (Exception ex)
            {
                PosLogger.Log($"LPT copy preferred failed, fallback to direct: {path} :: {ex.Message}", "PRINTER");
            }
        }

        try
        {
            using var stream = OpenPort(path);
            Thread.Sleep(20);
            stream.Write(payload, 0, payload.Length);
            stream.Flush();
            PosLogger.Log($"LPT direct write OK: {path}, bytes={payload.Length}", "PRINTER");
            return;
        }
        catch (Exception ex)
        {
            PosLogger.Log($"LPT direct write failed: {path} :: {ex.Message}", "PRINTER");
        }

        TryWritePayloadViaCopyCommand(path, payload);
    }

    private static void TryWritePayloadViaCopyCommand(string path, byte[] payload)
    {
        var target = path.StartsWith(@"\\.\", StringComparison.Ordinal) ? path[4..] : path;
        var tempFile = Path.Combine(Path.GetTempPath(), $"NurMarketKassa-print-{Guid.NewGuid():N}.bin");
        try
        {
            File.WriteAllBytes(tempFile, payload);
            using var proc = Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c copy /b \"{tempFile}\" \"{target}\" >nul",
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden,
            });

            proc?.WaitForExit(8000);
            if (proc == null)
                throw new InvalidOperationException("Не удалось запустить copy /b.");

            if (proc.ExitCode != 0)
                throw new InvalidOperationException($"copy /b вернул код {proc.ExitCode}.");

            PosLogger.Log($"LPT copy /b OK: {target}, bytes={payload.Length}", "PRINTER");
        }
        catch (Exception ex)
        {
            PosLogger.Log($"LPT copy /b failed: {target} :: {ex.Message}", "PRINTER");
            throw new InvalidOperationException(
                $"Не удалось отправить чек на {target}. Попробовали прямую запись и copy /b.",
                ex);
        }
        finally
        {
            try
            {
                if (File.Exists(tempFile))
                    File.Delete(tempFile);
            }
            catch
            {
                /* ignore */
            }
        }
    }

    private static void WriteReceipt(Stream s, Encoding encoding, string text, int tableByte, int? escRByte, bool noEscPct)
    {
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');

        // ESC @ init
        s.WriteByte(0x1B);
        s.WriteByte(0x40);

        ApplyCodePage(s, tableByte, escRByte, noEscPct);
        var boldFirst = true;
        foreach (var line in lines)
        {
            if (boldFirst && !string.IsNullOrWhiteSpace(line))
            {
                // ESC/POS: жирный шрифт для первой непустой строки (приветствие)
                s.WriteByte(0x1B);
                s.WriteByte(0x45);
                s.WriteByte(0x01);
                s.Write(encoding.GetBytes(line + "\r\n"));
                s.WriteByte(0x1B);
                s.WriteByte(0x45);
                s.WriteByte(0x00);
                boldFirst = false;
                continue;
            }

            s.Write(encoding.GetBytes(line + "\r\n"));
        }

        s.WriteByte(0x0D);
        s.WriteByte(0x0A);
        s.WriteByte(0x0D);
        s.WriteByte(0x0A);
        s.WriteByte(0x0D);
        s.WriteByte(0x0A);
    }

    private static void ApplyCodePage(Stream s, int tableByte, int? escRByte, bool noEscPct)
    {
        if (!noEscPct)
        {
            s.WriteByte(0x1B);
            s.WriteByte(0x25);
            s.WriteByte(0x00);
        }

        if (escRByte is >= 0 and <= 255)
        {
            s.WriteByte(0x1B);
            s.WriteByte(0x52);
            s.WriteByte((byte)(escRByte.Value & 0xFF));
        }

        s.WriteByte(0x1B);
        s.WriteByte(0x74);
        s.WriteByte((byte)(tableByte & 0xFF));
    }

    private static string NormalizeDevicePath(string raw)
    {
        var d = HardwarePortHelper.NormalizeLptPort(raw);
        if (d.StartsWith(@"\\", StringComparison.Ordinal))
            return d;
        return d;
    }

    private static string MapToDotNetEncoding(string userEnc)
    {
        var u = (userEnc ?? "cp866").Trim().ToLowerInvariant().Replace(" ", "", StringComparison.Ordinal)
            .Replace("_", "", StringComparison.Ordinal);
        return u switch
        {
            "utf-8" or "utf8" => "windows-1251",
            "cp1251" or "windows-1251" or "wpc1251" => "windows-1251",
            "cp855" => "ibm855",
            "cp866" or "ibm866" => "cp866",
            _ => "cp866",
        };
    }

    private static int DefaultEscPosTableByte(string dotnetEncName)
    {
        if (dotnetEncName.Contains("1251", StringComparison.OrdinalIgnoreCase))
            return 46;
        if (string.Equals(dotnetEncName, "ibm855", StringComparison.OrdinalIgnoreCase))
            return 34;
        return 17;
    }

    private const uint NativeGenericWrite = 0x40000000;
    private const uint OpenExisting = 3;

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFile(
        string lpFileName,
        uint dwDesiredAccess,
        FileShare dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);
}
