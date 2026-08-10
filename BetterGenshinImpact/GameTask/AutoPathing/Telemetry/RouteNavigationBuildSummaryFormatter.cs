using System;

namespace BetterGenshinImpact.GameTask.AutoPathing.Telemetry;

public static class RouteNavigationBuildSummaryFormatter
{
    public static string Format(RouteNavigationBuildResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var headline = result.Success
            ? $"路网生成完成：{result.Graph.Nodes.Count} 个节点 / {result.Graph.Edges.Count} 条边"
            : $"路网生成失败：{result.ErrorMessage}";
        if (result.ImportReport == null)
        {
            return headline;
        }

        var report = result.ImportReport;
        var skippedFiles = report.NonRouteFiles + report.InvalidJsonFiles + report.UnrecognizedMapFiles;
        var strippedActions = 0;
        foreach (var count in report.StrippedActions.Values)
        {
            strippedActions += count;
        }

        var output = result.Success && !string.IsNullOrWhiteSpace(result.OutputPath)
            ? $"；输出：{result.OutputPath}"
            : string.Empty;
        return $"{headline}；扫描 {report.TotalJsonFiles} 个 JSON，识别 {report.EligibleRouteFiles} 条路线，" +
               $"跳过 {skippedFiles} 个，去重 {report.DuplicateRouteFiles} 个，" +
               $"无效 JSON {report.InvalidJsonFiles} 个，未知地图 {report.UnrecognizedMapFiles} 个，" +
               $"坐标转换失败 {report.CoordinateConversionFailures} 个点，移除不安全动作 {strippedActions} 个，" +
               $"缺失目录 {report.MissingSourceDirectories} 个，不可读目录 {report.UnreadableSourceDirectories} 个{output}。";
    }
}
