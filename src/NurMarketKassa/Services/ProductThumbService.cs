using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace NurMarketKassa.Services;

/// <summary>Дисковый кэш превью + загрузка с Bearer для URL API.</summary>
internal sealed class ProductThumbService
{
    private const int DecodePixelSize = 96;
    private static readonly HttpClient PublicHttp = new() { Timeout = TimeSpan.FromSeconds(25) };
    private static readonly ImageSource PlaceholderThumb = BuildPlaceholderThumb();
    private readonly string _cacheDir;
    private readonly ConcurrentDictionary<string, ImageSource> _decodedCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Task<string?>> _downloadTasks = new(StringComparer.OrdinalIgnoreCase);

    public ProductThumbService()
    {
        _cacheDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "NurMarketKassa",
            "product_thumbs");
        try
        {
            Directory.CreateDirectory(_cacheDir);
        }
        catch
        {
            /* ignore */
        }
    }

    public async Task SetThumbAsync(
        Dispatcher uiDispatcher,
        NurMarketApiClient api,
        string apiBaseUrl,
        string imageUrl,
        Models.Pos.CatalogProductTileVm vm,
        CancellationToken ct)
    {
        if (vm.Thumb != null)
            return;

        var path = await GetOrDownloadPathAsync(api, apiBaseUrl, imageUrl, ct).ConfigureAwait(false);
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            return;

        if (_decodedCache.TryGetValue(path, out var cached))
        {
            await uiDispatcher.InvokeAsync(() => vm.Thumb = cached);
            return;
        }

        var thumb = await TryLoadThumbAsync(path, ct).ConfigureAwait(false);
        if (thumb == null)
        {
            TryDelete(path);
            path = await GetOrDownloadPathAsync(api, apiBaseUrl, imageUrl, ct, forceDownload: true).ConfigureAwait(false);
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                await uiDispatcher.InvokeAsync(() => vm.Thumb ??= PlaceholderThumb);
                return;
            }

            thumb = await TryLoadThumbAsync(path, ct).ConfigureAwait(false);
            if (thumb == null)
            {
                await uiDispatcher.InvokeAsync(() => vm.Thumb ??= PlaceholderThumb);
                return;
            }
        }

        _decodedCache[path] = thumb;
        await uiDispatcher.InvokeAsync(() => vm.Thumb = thumb);
    }

    private async Task<string?> GetOrDownloadPathAsync(
        NurMarketApiClient api,
        string apiBaseUrl,
        string imageUrl,
        CancellationToken ct,
        bool forceDownload = false)
    {
        var key = Sha256Hex(imageUrl);
        var ext = GuessExt(imageUrl);
        var local = Path.Combine(_cacheDir, key + ext);
        if (!forceDownload && File.Exists(local))
            return local;

        if (!forceDownload)
        {
            var task = _downloadTasks.GetOrAdd(local, _ => DownloadToCacheAsync(local, api, apiBaseUrl, imageUrl, ct));
            try
            {
                return await task.ConfigureAwait(false);
            }
            finally
            {
                _downloadTasks.TryRemove(local, out _);
            }
        }

        return await DownloadToCacheAsync(local, api, apiBaseUrl, imageUrl, ct).ConfigureAwait(false);
    }

    private async Task<string?> DownloadToCacheAsync(
        string local,
        NurMarketApiClient api,
        string apiBaseUrl,
        string imageUrl,
        CancellationToken ct)
    {
        byte[]? bytes;
        if (IsSameHost(imageUrl, apiBaseUrl))
        {
            bytes = await api.DownloadAuthorizedAsync(imageUrl, ct).ConfigureAwait(false);
            bytes ??= await DownloadPublicAsync(imageUrl, ct).ConfigureAwait(false);
        }
        else
            bytes = await DownloadPublicAsync(imageUrl, ct).ConfigureAwait(false);

        if (bytes is null || bytes.Length == 0)
            return null;

        try
        {
            await File.WriteAllBytesAsync(local, bytes, ct).ConfigureAwait(false);
            return local;
        }
        catch
        {
            return null;
        }
    }

    private static async Task<ImageSource?> TryLoadThumbAsync(string path, CancellationToken ct)
    {
        try
        {
            await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var ms = new MemoryStream();
            await fs.CopyToAsync(ms, ct).ConfigureAwait(false);
            ms.Position = 0;

            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.DecodePixelWidth = DecodePixelSize;
            bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bmp.StreamSource = ms;
            bmp.EndInit();
            bmp.Freeze();
            ms.Dispose();
            return bmp;
        }
        catch
        {
            return await TryLoadWithSystemDrawingAsync(path, ct).ConfigureAwait(false);
        }
    }

    private static async Task<ImageSource?> TryLoadWithSystemDrawingAsync(string path, CancellationToken ct)
    {
        try
        {
            using var bitmap = new Bitmap(path);

            // Масштабирование, если необходимо
            int width = bitmap.Width;
            int height = bitmap.Height;
            if (width > DecodePixelSize || height > DecodePixelSize)
            {
                double scale = Math.Min(1.0, DecodePixelSize / (double)Math.Max(width, height));
                int newWidth = (int)(width * scale);
                int newHeight = (int)(height * scale);
                using var resized = new Bitmap(bitmap, newWidth, newHeight);
                bitmap.Dispose();
                return await Task.Run(() => ConvertToBitmapSource(resized), ct);
            }

            return await Task.Run(() => ConvertToBitmapSource(bitmap), ct);
        }
        catch
        {
            return null;
        }
    }

    private static ImageSource ConvertToBitmapSource(Bitmap bitmap)
    {
        IntPtr hbitmap = bitmap.GetHbitmap();
        try
        {
            var source = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                hbitmap,
                IntPtr.Zero,
                System.Windows.Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        finally
        {
            DeleteObject(hbitmap);
        }
    }

    [System.Runtime.InteropServices.DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            /* ignore */
        }
    }

    private static bool IsSameHost(string imageUrl, string apiBaseUrl)
    {
        if (!Uri.TryCreate(imageUrl, UriKind.Absolute, out var iu))
            return false;
        var b = (apiBaseUrl ?? "").Trim().TrimEnd('/');
        if (b.Length == 0)
            return false;
        if (!Uri.TryCreate(b + "/", UriKind.Absolute, out var bu))
            return false;
        return string.Equals(iu.Host, bu.Host, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<byte[]?> DownloadPublicAsync(string url, CancellationToken ct)
    {
        try
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
                return null;

            return await PublicHttp.GetByteArrayAsync(uri, ct).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }

    private static string Sha256Hex(string s)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(s));
        var sb = new StringBuilder(bytes.Length * 2);
        foreach (var b in bytes)
            sb.Append(b.ToString("x2"));
        return sb.ToString();
    }

    private static string GuessExt(string url)
    {
        var u = url.ToLowerInvariant();
        if (u.Contains(".png", StringComparison.Ordinal))
            return ".png";
        if (u.Contains(".webp", StringComparison.Ordinal))
            return ".webp";
        if (u.Contains(".gif", StringComparison.Ordinal))
            return ".gif";
        return ".jpg";
    }

    private static ImageSource BuildPlaceholderThumb()
    {
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(
            new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x24, 0x32, 0x46)),
            null,
            new RectangleGeometry(new System.Windows.Rect(0, 0, 60, 60), 6, 6)));
        group.Children.Add(new GeometryDrawing(
            new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x94, 0xA3, 0xB8)),
            null,
            new EllipseGeometry(new System.Windows.Point(22, 22), 8, 8)));
        group.Children.Add(new GeometryDrawing(
            new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x64, 0x74, 0x8B)),
            null,
            Geometry.Parse("M8,48 L23,31 L34,40 L42,34 L52,48 Z")));
        group.Freeze();
        return new DrawingImage(group);
    }
}