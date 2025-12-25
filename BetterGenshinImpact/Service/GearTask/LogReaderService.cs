using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using System.Collections.Generic;

namespace BetterGenshinImpact.Service.GearTask;

public class LogReaderService
{
    private readonly string _logDirectory;

    public LogReaderService()
    {
        _logDirectory = Path.Combine(AppContext.BaseDirectory, "log");
    }

    public async Task<string> GetLogsForCorrelationIdAsync(string correlationId, DateTime date)
    {
        if (string.IsNullOrWhiteSpace(correlationId))
        {
            return "No correlation ID provided.";
        }

        var logFileName = $"better-genshin-impact{date:yyyyMMdd}.log";
        var logFilePath = Path.Combine(_logDirectory, logFileName);

        if (!File.Exists(logFilePath))
        {
            return $"Log file not found: {logFilePath}";
        }

        try
        {
            // 读取文件内容
            // 由于日志文件可能很大，这里简单起见读取全部，生产环境可能需要更高效的方式（如流式读取）
            // 考虑到文件可能被占用，使用 FileShare.ReadWrite
            using var fileStream = new FileStream(logFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var streamReader = new StreamReader(fileStream);
            var content = await streamReader.ReadToEndAsync();

            var lines = content.Split(new[] { Environment.NewLine }, StringSplitOptions.None);
            
            // 筛选包含 CorrelationId 的行
            // 日志格式: [Timestamp] [Level] [CorrelationId] SourceContext ...
            var relatedLines = lines.Where(line => line.Contains(correlationId)).ToList();

            if (relatedLines.Count == 0)
            {
                return "No logs found for this execution.";
            }

            return string.Join(Environment.NewLine, relatedLines);
        }
        catch (Exception ex)
        {
            return $"Error reading log file: {ex.Message}";
        }
    }
}