using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

#nullable enable
namespace NurMarketKassa.Services;

public static class AnalyticsService
{
    private static readonly HttpClient _client = new HttpClient();

    public static async Task SendAsync(AnalyticsRecord record)
    {
        try
        {
            StringContent content = new StringContent(JsonSerializer.Serialize<AnalyticsRecord>(record), Encoding.UTF8, "application/json");
            HttpResponseMessage httpResponseMessage = await AnalyticsService._client.PostAsync(App.Settings.ApiBaseUrl + "/analytics/shift-events", (HttpContent)content);
        }
        catch
        {
        }
    }
}
