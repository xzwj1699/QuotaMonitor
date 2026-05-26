using System.Net;
using System.Net.Http.Headers;

namespace QuotaMonitor.Core.Infrastructure;

public static class HttpJsonClient
{
    public static Dictionary<string, object> GetJson(
        string url,
        string bearerToken,
        Dictionary<string, string> extraHeaders,
        int timeoutMs)
    {
        using var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        };
        using var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromMilliseconds(timeoutMs)
        };
        using var request = new HttpRequestMessage(HttpMethod.Get, url);

        request.Headers.Accept.ParseAdd("application/json, text/plain, */*");
        request.Headers.UserAgent.ParseAdd("QuotaMonitor/2.0");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true };

        if (extraHeaders != null)
        {
            foreach (var header in extraHeaders)
            {
                if (string.Equals(header.Key, "Accept", StringComparison.OrdinalIgnoreCase))
                {
                    request.Headers.Accept.Clear();
                    request.Headers.Accept.ParseAdd(header.Value);
                }
                else if (string.Equals(header.Key, "User-Agent", StringComparison.OrdinalIgnoreCase))
                {
                    request.Headers.UserAgent.Clear();
                    request.Headers.UserAgent.ParseAdd(header.Value);
                }
                else if (string.Equals(header.Key, "Referer", StringComparison.OrdinalIgnoreCase))
                {
                    request.Headers.Referrer = new Uri(header.Value);
                }
                else
                {
                    request.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
            }
        }

        using var response = client.Send(request);
        response.EnsureSuccessStatusCode();
        var content = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        return JsonUtil.ParseObject(content);
    }
}
