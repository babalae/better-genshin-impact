// 移植自 QuestVoice 1fc59d9c；保留原神业务算法，宿主适配见 QuestVoiceSessionFactory。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Ocr;

namespace BetterGenshinImpact.GameTask.QuestVoice.Features.Ocr;

/// <summary>SceneProfileLoader：原神自动配音的业务类型。</summary>
public sealed class SceneProfileLoader : ISceneProfileLoader
{
    /// <summary>异步读取并严格校验外置配置或布局。</summary>
    public async Task<SceneProfile> LoadAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var reader = XmlReader.Create(stream, new XmlReaderSettings
            { Async = true, DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 1_000_000 });
            var document = await XDocument.LoadAsync(reader, LoadOptions.None, cancellationToken).ConfigureAwait(false);
            var root = document.Root;
            if (root?.Name != "annotation") throw Invalid("缺少 annotation 根节点。");
            var size = One(root, "size");
            var width = Number(size, "width");
            var height = Number(size, "height");
            if (width <= 0 || height <= 0) throw Invalid("参考尺寸必须为正整数。");
            var regions = new List<LayoutRegion>();
            foreach (var element in root.Elements("object"))
            {
                var label = One(element, "name").Value.Trim();
                if (label.Length == 0) throw Invalid("区域标签不能为空。");
                var kind = OcrRegionKind.Speaker;
                var order = 0;
                if (label is not ("name" or "character"))
                {
                    if (!label.StartsWith("dialogue_line", StringComparison.Ordinal)) continue;
                    kind = OcrRegionKind.Dialogue;
                    if (!int.TryParse(label[13..], NumberStyles.None, CultureInfo.InvariantCulture, out order) || order <= 0)
                        throw Invalid($"无效对白编号：{label}");
                }
                var box = One(element, "bndbox");
                var bounds = new PixelRect(Number(box,"xmin"),Number(box,"ymin"),Number(box,"xmax"),Number(box,"ymax"));
                if (bounds.Left < 0 || bounds.Top < 0 || bounds.Right > width || bounds.Bottom > height || bounds.Left >= bounds.Right || bounds.Top >= bounds.Bottom)
                    throw Invalid($"区域越界或为空：{label}");
                if (regions.Any(r => r.Kind == kind && r.Order == order)) throw Invalid($"重复区域：{label}");
                regions.Add(new(label, kind, order, bounds));
            }
            if (regions.Count(r => r.Kind == OcrRegionKind.Speaker) != 1) throw Invalid("必须有一个 name 或 character 区域。");
            var lines = regions.Where(r => r.Kind == OcrRegionKind.Dialogue).OrderBy(r=>r.Order).ToArray();
            if (lines.Length == 0 || lines.Where((r,i)=>r.Order != i+1).Any()) throw Invalid("对白区域必须从 dialogue_line1 连续编号。");
            return new(width,height,regions.OrderBy(r=>r.Kind).ThenBy(r=>r.Order).ToArray());
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or XmlException or ArgumentException)
        {
            throw new OcrException(OcrErrorCode.Template, $"无法读取布局模板：{exception.Message}", exception);
        }
    }
    /// <summary>One：执行SceneProfileLoader中的对应校验或计算步骤。</summary>
    private static XElement One(XElement parent, string name)
    {
        var elements = parent.Elements(name).ToArray();
        if (elements.Length != 1 || elements[0].HasElements && name is "name" or "width" or "height" or "xmin" or "ymin" or "xmax" or "ymax")
            throw Invalid($"缺失或重复字段：{name}");
        return elements[0];
    }
    /// <summary>Number：执行SceneProfileLoader中的对应校验或计算步骤。</summary>
    private static int Number(XElement parent,string name) =>
        int.TryParse(One(parent,name).Value, NumberStyles.Integer, CultureInfo.InvariantCulture,out var value)
            ? value : throw Invalid($"无效整数：{name}");
    /// <summary>Invalid：执行SceneProfileLoader中的对应校验或计算步骤。</summary>
    private static OcrException Invalid(string reason) => new(OcrErrorCode.Template, reason);
}
