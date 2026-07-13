using System;
using System.Collections.Concurrent;
using System.Net.Http;

namespace BetterGenshinImpact.Helpers.Http;

public class HttpClientFactory
{
    private static readonly ConcurrentDictionary<string, HttpClient> Clients = new();

    public static HttpClient GetClient(string key, Func<HttpClient> factory)
    {
        return Clients.GetOrAdd(key, _ => factory());
    }

    public static HttpClient GetCommonSendClient()
    {
        return Clients.GetOrAdd("common", _ => new HttpClient());
    }
}