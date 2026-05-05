using System.Text.Json.Serialization;

#nullable enable
namespace NurMarketKassa.Models;

public class UpdateManifest
{
    [JsonPropertyName("latest_version")]
    public string LatestVersion { get; set; } = "";

    [JsonPropertyName("download_url")]
    public string DownloadUrl { get; set; } = "";
}
