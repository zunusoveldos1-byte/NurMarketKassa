using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using NurMarketKassa.Configuration;

namespace NurMarketKassa.Services;

/// <summary>Фоновое чтение COM весов (аналог ScaleManager в Python).</summary>
public sealed class ScaleReaderService : IDisposable
{
    private readonly object _lock = new();
    private Thread? _thread;
    private volatile bool _stop;
    private double? _lastWeight;
    private string _lastRaw = "";
    private string _status = "—";
    private readonly ScaleSettings _cfg;
    private readonly byte[]? _requestBytes;
    private readonly int _pollMs;
    private long _nextPollAtMs;

    public ScaleReaderService(ScaleSettings cfg)
    {
        _cfg = cfg;
        _requestBytes = ParseRequestHex(cfg.RequestHex);
        _pollMs = Math.Max(0, cfg.PollMs);
    }

    public double? LastWeight
    {
        get
        {
            lock (_lock)
                return _lastWeight;
        }
    }

    public string LastRaw
    {
        get
        {
            lock (_lock)
                return _lastRaw;
        }
    }

    public string Status
    {
        get
        {
            lock (_lock)
                return _status;
        }
    }

    public void Start()
    {
        if (!_cfg.Enabled)
            return;
        if (_thread is { IsAlive: true })
            return;
        _stop = false;
        _thread = new Thread(RunLoop) { IsBackground = true, Name = "ScaleCOM" };
        _thread.Start();
    }

    public void Stop()
    {
        _stop = true;
        try
        {
            _thread?.Join(3000);
        }
        catch
        {
            /* ignore */
        }

        _thread = null;
    }

    private void RunLoop()
    {
        while (!_stop)
        {
            SerialPort? ser = null;
            try
            {
                var portName = HardwarePortHelper.NormalizeComPort(_cfg.ComPort);
                ser = new SerialPort(portName, _cfg.BaudRate, Parity.None, 8, StopBits.One)
                {
                    ReadTimeout = 50,
                    WriteTimeout = 1000,
                    NewLine = "\n",
                    DtrEnable = true,
                    RtsEnable = true,
                    Handshake = Handshake.None,
                };
                ser.Open();
                try
                {
                    ser.DiscardInBuffer();
                }
                catch
                {
                    /* ignore */
                }

                SetStatus($"OK {portName} {_cfg.BaudRate}");
                _nextPollAtMs = 0;

                if (_requestBytes is { Length: > 0 })
                {
                    try
                    {
                        ser.Write(_requestBytes, 0, _requestBytes.Length);
                        ser.BaseStream.Flush();
                    }
                    catch
                    {
                        /* ignore */
                    }
                }

                const int maxBuf = 512;
                var buf = new List<byte>(256);
                while (!_stop)
                {
                    MaybeSendRequest(ser);
                    try
                    {
                        if (ser.BytesToRead > 0)
                        {
                            var chunk = new byte[Math.Min(ser.BytesToRead, 256)];
                            var n = ser.Read(chunk, 0, chunk.Length);
                            for (var i = 0; i < n; i++)
                            {
                                var b = chunk[i];
                                if (b is 0x0D or 0x0A)
                                {
                                    if (buf.Count > 0)
                                        ProcessLine(buf.ToArray());
                                    buf.Clear();
                                }
                                else
                                {
                                    buf.Add(b);
                                    if (buf.Count >= maxBuf)
                                    {
                                        ProcessLine(buf.ToArray());
                                        buf.Clear();
                                    }
                                }
                            }

                            continue;
                        }

                        var b1 = ser.ReadByte();
                        if (b1 < 0)
                            continue;
                        if (b1 is 0x0D or 0x0A)
                        {
                            if (buf.Count > 0)
                                ProcessLine(buf.ToArray());
                            buf.Clear();
                        }
                        else
                        {
                            buf.Add((byte)b1);
                            if (buf.Count >= maxBuf)
                            {
                                ProcessLine(buf.ToArray());
                                buf.Clear();
                            }
                        }
                    }
                    catch (TimeoutException)
                    {
                        if (buf.Count > 0)
                        {
                            ProcessLine(buf.ToArray());
                            buf.Clear();
                        }
                    }
                    catch (IOException)
                    {
                        if (_stop)
                            break;
                        SetStatus($"COM потерян: {portName}. Переподключение…");
                        break;
                    }
                    catch (InvalidOperationException)
                    {
                        if (_stop)
                            break;
                        SetStatus($"COM закрыт: {portName}. Переподключение…");
                        break;
                    }
                }
            }
            catch (UnauthorizedAccessException)
            {
                SetStatus($"COM занят или доступ запрещён: {HardwarePortHelper.NormalizeComPort(_cfg.ComPort)}.");
            }
            catch (Exception ex)
            {
                SetStatus($"COM недоступен: {ex.Message}");
            }
            finally
            {
                try
                {
                    ser?.Close();
                    ser?.Dispose();
                }
                catch
                {
                    /* ignore */
                }
            }

            if (!_stop)
                Thread.Sleep(1200);
        }
    }

    public static IReadOnlyList<string> GetAvailablePorts()
    {
        try
        {
            return SerialPort.GetPortNames()
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    public static string BuildStatusSummary(ScaleSettings cfg, string status, double? weight)
    {
        var port = HardwarePortHelper.NormalizeComPort(cfg.ComPort);
        var weightText = weight is > 0
            ? $"{weight.Value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)} кг"
            : "нет стабильного веса";
        return $"Порт: {port} @ {cfg.BaudRate}\nСтатус: {status}\nВес: {weightText}";
    }

    public static void ValidateSettings(ScaleSettings cfg)
    {
        if (!HardwarePortHelper.LooksLikeComPort(cfg.ComPort))
            throw new InvalidOperationException("Для весов укажите корректный COM-порт, например COM3.");

        if (cfg.BaudRate <= 0)
            throw new InvalidOperationException("Скорость весов должна быть больше 0.");

        _ = ParseRequestHex(cfg.RequestHex);
    }

    private static byte[]? ParseRequestHex(string? raw)
    {
        var s = (raw ?? "").Trim();
        if (s.Length == 0)
            return null;
        var parts = s.Replace(',', ' ').Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var outBytes = new List<byte>();
        foreach (var p in parts)
        {
            var t = p.Trim();
            if (t.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                t = t[2..];
            if (t.Length == 0)
                continue;
            if (!byte.TryParse(t, System.Globalization.NumberStyles.HexNumber, null, out var b))
                throw new InvalidOperationException("Запрос весов HEX указан неверно. Пример: 05 или 57 0D.");
            outBytes.Add(b);
        }

        return outBytes.Count == 0 ? null : outBytes.ToArray();
    }

    private void ProcessLine(byte[] line)
    {
        string dec;
        try
        {
            dec = System.Text.Encoding.UTF8.GetString(line).Trim();
        }
        catch
        {
            dec = System.Text.Encoding.Latin1.GetString(line).Trim();
        }

        lock (_lock)
            _lastRaw = dec;

        var w = ScaleWeightParser.ParseWeightLine(line);
        if (w is not null)
        {
            lock (_lock)
                _lastWeight = w;
            SetStatus($"OK {HardwarePortHelper.NormalizeComPort(_cfg.ComPort)} {_cfg.BaudRate}");
        }
    }

    private void SetStatus(string msg)
    {
        lock (_lock)
            _status = msg;
    }

    private void MaybeSendRequest(SerialPort ser)
    {
        if (_requestBytes is null || _requestBytes.Length == 0 || _pollMs <= 0)
            return;
        var now = MonotonicMs();
        if (now < _nextPollAtMs)
            return;
        _nextPollAtMs = now + _pollMs;
        try
        {
            ser.Write(_requestBytes, 0, _requestBytes.Length);
            ser.BaseStream.Flush();
        }
        catch
        {
            /* ignore */
        }
    }

    private static long MonotonicMs() => Environment.TickCount64;

    public void Dispose()
    {
        Stop();
    }
}
