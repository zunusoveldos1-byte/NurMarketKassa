using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using NurMarketKassa.Models;

#nullable disable

namespace NurMarketKassa.Services
{
    public class UpdateService
    {
        private readonly HttpClient _http = new HttpClient();
        private readonly string _manifestUrl;

        public UpdateService(string manifestUrl) => _manifestUrl = manifestUrl;

        public async Task<UpdateManifest> CheckAsync()
        {
            if (string.IsNullOrWhiteSpace(_manifestUrl))
                return null;

            try
            {
                var manifest = await _http.GetFromJsonAsync<UpdateManifest>(
                    _manifestUrl, CancellationToken.None);

                if (manifest == null || string.IsNullOrWhiteSpace(manifest.LatestVersion))
                    return null;

                Version currentVersion = Assembly.GetExecutingAssembly().GetName().Version;
                if (currentVersion == null)
                    return null;

                return new Version(manifest.LatestVersion) > currentVersion ? manifest : null;
            }
            catch
            {
                return null;
            }
        }

        public async Task DownloadAndRunAsync(string downloadUrl, IProgress<double> progress = null)
        {
            string tempPath = Path.Combine(Path.GetTempPath(), "NurMarketSetup.exe");

            using (var response = await _http.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                long totalBytes = response.Content.Headers.ContentLength.GetValueOrDefault(-1);

                using (var stream = await response.Content.ReadAsStreamAsync())
                using (var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    byte[] buffer = new byte[8192];
                    long totalRead = 0;
                    int bytesRead;
                    while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    {
                        await fileStream.WriteAsync(buffer, 0, bytesRead);
                        totalRead += bytesRead;
                        if (totalBytes > 0 && progress != null)
                            progress.Report((double)totalRead / totalBytes * 100.0);
                    }
                }
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = tempPath,
                UseShellExecute = true
            });

            Application.Current.Dispatcher.Invoke(() => Application.Current.Shutdown());
        }
    }
}