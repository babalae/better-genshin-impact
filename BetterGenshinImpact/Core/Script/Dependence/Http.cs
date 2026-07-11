using BetterGenshinImpact.Core.Script.Utils;
using Microsoft.ClearScript;
using System;
using System.Collections;
using System.IO;
using System.Threading.Tasks;
using OpenCvSharp;
using System.Linq;
using System.Text.Json;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text;
using BetterGenshinImpact.GameTask;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.Core.Script.Dependence;

public class Http
{
    private static readonly HashSet<string> ContentHeaderNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Content-Type",
        "Content-Length",
        "Content-Encoding",
        "Content-Language",
        "Content-Disposition",
        "Content-Location",
        "Content-Range"
    };

    private readonly ILogger<Http> _logger;
    private readonly HttpClient? _httpClient;

    public Http()
    {
        _logger = App.GetLogger<Http>();
    }

    public Http(HttpClient httpClient, ILogger<Http>? logger = null)
    {
        _httpClient = httpClient;
        _logger = logger ?? App.GetLogger<Http>();
    }

    private void CheckHttpPermission(string url)
    {
        var currentProject = TaskContext.Instance().CurrentScriptProject;
        if (!currentProject?.AllowJsHTTP ?? false)
        {
            throw new UnauthorizedAccessException("当前JS脚本不允许使用HTTP请求，请在调度器通用设置中启用“JS HTTP权限”");
        }
        var allowedUrls = currentProject?.Project?.Manifest.HttpAllowedUrls ?? [];
        if (allowedUrls.Length == 0)
        {
            throw new UnauthorizedAccessException("当前JS脚本没有配置允许请求的URL，请在脚本的manifest.json中配置http_allowed_urls");
        }
        if (allowedUrls.Any(allowedUrl =>
        {
            // fuzzy match
            var pattern = "^" + System.Text.RegularExpressions.Regex.Escape(allowedUrl).Replace("\\*", ".*") + "$";
            _logger.LogDebug($"[HTTP] 检查URL {url} 是否符合: {pattern}");
            var regex = new System.Text.RegularExpressions.Regex(pattern);
            return regex.IsMatch(url);
        }))
        {
            return;
        }
        throw new UnauthorizedAccessException($"当前JS脚本不允许请求此URL: {url}，请在脚本的manifest.json中配置http_allowed_urls，当前允许的URL列表: [{string.Join(", ", allowedUrls)}]");
    }

    public class HttpReponse
    {
        public int status_code { get; set; }
        public Dictionary<string, string> headers { get; set; } = new();
        public string body { get; set; } = "";
    }


    /// <summary>
    /// 执行HTTP请求
    /// </summary>
    /// <param name="method">HTTP方法</param>
    /// <param name="url">请求URL</param>
    /// <param name="body">请求体</param>
    /// <param name="headersJson">请求头，JSON格式</param>
    /// <returns></returns>
    public async Task<HttpReponse> Request(string method, string url, string? body = null, string? headersJson = null)
    {
        return await RequestCore(method, url, body, headersJson);
    }

    /// <summary>
    /// 执行HTTP请求，支持从JS传入普通对象作为请求头。
    /// </summary>
    /// <param name="method">HTTP方法</param>
    /// <param name="url">请求URL</param>
    /// <param name="body">请求体</param>
    /// <param name="headers">请求头，支持JS对象、字典、键值对集合或JSON字符串</param>
    /// <returns></returns>
    public async Task<HttpReponse> Request(string method, string url, string? body, object? headers)
    {
        return await RequestCore(method, url, body, headers);
    }

    private async Task<HttpReponse> RequestCore(string method, string url, string? body, object? headers)
    {
        _logger.LogDebug("[HTTP] 发送HTTP请求: {Method} {Url} BodyLength: {BodyLength}", method, url, body?.Length ?? 0);
        CheckHttpPermission(url);

        var dictHeaders = ConvertToHeaderDictionary(headers);
        if (dictHeaders.Count > 0)
        {
            _logger.LogDebug("[HTTP] 请求头名称: {HeaderNames}", string.Join(", ", dictHeaders.Keys));
        }

        var request = new HttpRequestMessage(new HttpMethod(method), url);
        if (body != null)
        {
            request.Content = new StringContent(body, Encoding.UTF8);
            request.Content.Headers.Clear();
            if (!dictHeaders.ContainsKey("Content-Type"))
            {
                request.Content.Headers.TryAddWithoutValidation("Content-Type", "application/json");
            }
        }

        ApplyHeaders(request, dictHeaders);

        using var localHttpClient = _httpClient == null ? new HttpClient() : null;
        var httpClient = _httpClient ?? localHttpClient!;
        var response = await httpClient.SendAsync(request);

        var responseCode = (int)response.StatusCode;
        var responseHeaders = response.Headers
            .Concat(response.Content.Headers)
            .GroupBy(h => h.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(h => h.Key, h => h.First().Value.FirstOrDefault() ?? "", StringComparer.OrdinalIgnoreCase);
        var responseBody = await response.Content.ReadAsStringAsync();
        return new HttpReponse
        {
            status_code = responseCode,
            headers = responseHeaders,
            body = responseBody,
        };
    }

    private void ApplyHeaders(HttpRequestMessage request, Dictionary<string, string> headers)
    {
        foreach (var header in headers)
        {
            if (ContentHeaderNames.Contains(header.Key))
            {
                if (request.Content == null)
                {
                    _logger.LogDebug("[HTTP] 请求无Body，忽略内容请求头: {HeaderName}", header.Key);
                    continue;
                }

                if (header.Key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
                {
                    if (!long.TryParse(header.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var contentLength))
                    {
                        throw new ArgumentException("Content-Length请求头必须是有效的非负整数");
                    }

                    request.Content.Headers.ContentLength = contentLength;
                    continue;
                }

                if (!request.Content.Headers.TryAddWithoutValidation(header.Key, header.Value))
                {
                    throw new ArgumentException($"无法添加内容请求头: {header.Key}");
                }

                continue;
            }

            if (!request.Headers.TryAddWithoutValidation(header.Key, header.Value))
            {
                throw new ArgumentException($"无法添加请求头: {header.Key}");
            }
        }
    }

    private static Dictionary<string, string> ConvertToHeaderDictionary(object? headers)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (headers == null || headers is Undefined)
        {
            return result;
        }

        if (headers is string headersJson)
        {
            AddJsonHeaders(result, headersJson);
            return result;
        }

        if (headers is ScriptObject scriptObject)
        {
            AddScriptObjectHeaders(result, scriptObject);
            return result;
        }

        if (headers is IDictionary dictionary)
        {
            AddDictionaryHeaders(result, dictionary);
            return result;
        }

        if (TryAddReflectedScriptObjectHeaders(result, headers))
        {
            return result;
        }

        if (headers is IEnumerable enumerable)
        {
            AddEnumerableHeaders(result, enumerable);
            return result;
        }

        throw new ArgumentException($"无法将请求头转换为Header字典，实际类型: {headers.GetType().FullName}");
    }

    private static void AddJsonHeaders(Dictionary<string, string> result, string headersJson)
    {
        if (string.IsNullOrWhiteSpace(headersJson))
        {
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(headersJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new ArgumentException("Headers JSON必须是对象");
            }

            foreach (var property in document.RootElement.EnumerateObject())
            {
                AddHeader(result, property.Name, JsonElementToHeaderValue(property.Value));
            }
        }
        catch (JsonException ex)
        {
            throw new ArgumentException("Headers JSON格式错误", ex);
        }
    }

    private static string JsonElementToHeaderValue(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? "",
            JsonValueKind.Null => "",
            JsonValueKind.Undefined => "",
            _ => value.GetRawText()
        };
    }

    private static void AddScriptObjectHeaders(Dictionary<string, string> result, ScriptObject scriptObject)
    {
        foreach (var propertyName in scriptObject.PropertyNames)
        {
            var value = scriptObject.GetProperty(propertyName);
            if (value is Undefined)
            {
                continue;
            }

            AddHeader(result, propertyName, ConvertHeaderValueToString(propertyName, value));
        }
    }

    private static bool TryAddReflectedScriptObjectHeaders(Dictionary<string, string> result, object headers)
    {
        var type = headers.GetType();
        var propertyNamesProperty = type.GetProperty("PropertyNames");
        if (propertyNamesProperty?.GetValue(headers) is not IEnumerable propertyNames)
        {
            return false;
        }

        var getPropertyMethod = type.GetMethod("GetProperty", [typeof(string)]);
        var indexerProperty = type.GetProperty("Item", [typeof(string)]);
        foreach (var propertyNameObject in propertyNames)
        {
            var propertyName = ConvertHeaderNameToString(propertyNameObject);
            object? value;
            if (getPropertyMethod != null)
            {
                value = getPropertyMethod.Invoke(headers, [propertyName]);
            }
            else if (indexerProperty != null)
            {
                value = indexerProperty.GetValue(headers, [propertyName]);
            }
            else
            {
                return false;
            }

            if (value is Undefined)
            {
                continue;
            }

            AddHeader(result, propertyName, ConvertHeaderValueToString(propertyName, value));
        }

        return true;
    }

    private static void AddDictionaryHeaders(Dictionary<string, string> result, IDictionary dictionary)
    {
        foreach (DictionaryEntry entry in dictionary)
        {
            AddHeader(result, ConvertHeaderNameToString(entry.Key), ConvertHeaderValueToString(entry.Key, entry.Value));
        }
    }

    private static void AddEnumerableHeaders(Dictionary<string, string> result, IEnumerable enumerable)
    {
        foreach (var item in enumerable)
        {
            if (item == null)
            {
                continue;
            }

            if (item is DictionaryEntry dictionaryEntry)
            {
                AddHeader(result, ConvertHeaderNameToString(dictionaryEntry.Key), ConvertHeaderValueToString(dictionaryEntry.Key, dictionaryEntry.Value));
                continue;
            }

            var itemType = item.GetType();
            var keyProperty = itemType.GetProperty("Key");
            var valueProperty = itemType.GetProperty("Value");
            if (keyProperty == null || valueProperty == null)
            {
                throw new ArgumentException($"无法将请求头集合项转换为键值对，集合项类型: {itemType.FullName}");
            }

            var key = keyProperty.GetValue(item);
            var value = valueProperty.GetValue(item);
            AddHeader(result, ConvertHeaderNameToString(key), ConvertHeaderValueToString(key, value));
        }
    }

    private static void AddHeader(Dictionary<string, string> result, string name, string value)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Header名称不能为空");
        }

        result[name] = value;
    }

    private static string ConvertHeaderNameToString(object? name)
    {
        var headerName = ConvertHeaderValueToString(null, name);
        if (string.IsNullOrWhiteSpace(headerName))
        {
            throw new ArgumentException("Header名称不能为空");
        }

        return headerName;
    }

    private static string ConvertHeaderValueToString(object? name, object? value)
    {
        if (value == null || value is Undefined)
        {
            return "";
        }

        if (value is string stringValue)
        {
            return stringValue;
        }

        if (value is IFormattable formattable)
        {
            return formattable.ToString(null, CultureInfo.InvariantCulture);
        }

        return value.ToString() ?? "";
    }
}
