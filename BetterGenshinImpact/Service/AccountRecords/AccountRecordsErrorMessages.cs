using System;
using System.IO;
using Newtonsoft.Json;

namespace BetterGenshinImpact.Service.AccountRecords;

/// <summary>为界面提供事件处理文案和底层异常说明，同时保留原始错误供排查。</summary>
public static class AccountRecordsErrorMessages
{
    /// <summary>业务校验保留具体说明；底层异常仅补充错误类别，操作建议由事件处理文案提供。</summary>
    public static string Describe(Exception error)
    {
        var guidance = error switch
        {
            JsonException => "JSON 数据无法解析。",
            UnauthorizedAccessException => "没有访问文件或目录的权限。",
            FileNotFoundException or DirectoryNotFoundException => "所需文件或目录不存在。",
            InvalidDataException => null,
            IOException => "文件读写未完成。",
            OverflowException => "数值超出支持范围；库存数量须在 0～9223372036854775807 之间。",
            FormatException => "输入格式无法识别。",
            ArgumentException or InvalidOperationException => null,
            _ => "操作未完成。"
        };
        return guidance == null ? error.Message : $"{guidance}\n错误详情：{error.Message}";
    }
}
