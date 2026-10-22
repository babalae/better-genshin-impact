using BetterGenshinImpact.GameTask.Model;
using Microsoft.Extensions.Localization;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace BetterGenshinImpact.GameTask.AutoArtifactSalvage
{
    /// <summary>
    /// 圣遗物分解任务参数。可 JSON 往返；语言和本地化器由基类提供，不参与 JSON。
    /// </summary>
    public class AutoArtifactSalvageTaskParam : BaseTaskParam<AutoArtifactSalvageTask>
    {
        /// <summary>
        /// 使用默认值创建参数：分解 4 星及以下，只做快速分解，不逐个筛选。用于 JSON 反序列化和任务计划。
        /// </summary>
        public AutoArtifactSalvageTaskParam() : this(4, null, null, null, null)
        {
        }

        public AutoArtifactSalvageTaskParam(int star, string? javaScript, string? artifactSetFilter, int? maxNumToCheck, RecognitionFailurePolicy? recognitionFailurePolicy, CultureInfo? cultureInfo = null, IStringLocalizer<AutoArtifactSalvageTask>? stringLocalizer = null) : base(cultureInfo, stringLocalizer)
        {
            Star = star;
            JavaScript = javaScript;
            ArtifactSetFilter = artifactSetFilter;
            MaxNumToCheck = maxNumToCheck;
            RecognitionFailurePolicy = recognitionFailurePolicy;
        }

        /// <summary>
        /// 快速分解的最大星级：快速选择后取消高于该星级的选项（快速选择本身最多包含 4 星）。
        /// </summary>
        public int Star { get; set; }

        /// <summary>
        /// 逐个筛选 5 星圣遗物使用的 JavaScript；为空时只做快速分解。
        /// </summary>
        public string? JavaScript { get; set; }

        /// <summary>
        /// 套装筛选条件，逐个筛选前先按包含的套装名勾选。
        /// </summary>
        public string? ArtifactSetFilter { get; set; }

        /// <summary>
        /// 逐个筛选时最多检查的圣遗物数量；设置了 JavaScript 时必填。
        /// </summary>
        public int? MaxNumToCheck { get; set; }

        /// <summary>
        /// 逐个筛选时识别失败的处理策略；设置了 JavaScript 时必填。
        /// </summary>
        public RecognitionFailurePolicy? RecognitionFailurePolicy { get; set; }
    }
}
