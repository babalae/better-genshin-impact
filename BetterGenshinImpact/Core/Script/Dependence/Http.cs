using BetterGenshinImpact.Core.Script.Utils;
using System;
using System.IO;
using System.Threading.Tasks;
using OpenCvSharp;
using System.Linq;
using System.Text.Json;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using BetterGenshinImpact.GameTask;

namespace BetterGenshinImpact.Core.Script.Dependence;

public class Http
{
    private bool CheckHttpPermission()
    {
        try
        {
            var currentProject = TaskContext.Instance().CurrentScriptProject;
            return currentProject?.AllowJsHTTP ?? false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 执行HTTP请求
    /// </summary>
    /// <param name="method">HTTP方法</param>
    /// <param name="url">请求URL</param>
    /// <param name="body">请求体</param
    /// <returns></returns>
    public async Task<string> Request(string method, string url, string? body, string? headersJson)
    {
        if (!CheckHttpPermission())
        {
            throw new UnauthorizedAccessException("当前JS脚本不允许使用HTTP请求，请在调度器通用设置中启用“JS HTTP权限”");
        }
        var dictHeaders = new Dictionary<string, string>();
        if (!string.IsNullOrWhiteSpace(headersJson))
        {
            try
            {
                var headers = JsonSerializer.Deserialize<Dictionary<string, string>>(headersJson);
                if (headers != null)
                {
                    dictHeaders = headers;
                }
            }
            catch (JsonException)
            {
                throw new ArgumentException("Headers JSON格式错误");
            }
        }

        // header全部小写
        dictHeaders = dictHeaders.ToDictionary(kvp => kvp.Key.ToLowerInvariant(), kvp => kvp.Value);
        
        // 提前取出来Content-Type，防止被覆盖
        string contentType = "application/json";
        if (dictHeaders.TryGetValue("content-type", out var ct))
        {
            contentType = ct;
            dictHeaders.Remove("content-type");
        }
        
        // 使用HttpClient发送请求
        using var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Clear();
        foreach (var header in dictHeaders)
        {
            httpClient.DefaultRequestHeaders.Add(header.Key, header.Value);
        }

        var content = body == null ? null : new StringContent(body, Encoding.UTF8, contentType);
        var response = await httpClient.SendAsync(new HttpRequestMessage(new HttpMethod(method), url) { Content = content });

        return await response.Content.ReadAsStringAsync();
    }
}
