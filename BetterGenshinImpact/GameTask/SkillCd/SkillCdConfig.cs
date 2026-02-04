using CommunityToolkit.Mvvm.ComponentModel;
using System;

namespace BetterGenshinImpact.GameTask.SkillCd;

/// <summary>
/// 技能 CD 提示配置
/// </summary>
[Serializable]
public partial class SkillCdConfig : ObservableObject
{
    /// <summary>
    /// 是否启用
    /// </summary>
    [ObservableProperty]
    private bool _enabled = false;

    /// <summary>
    /// 特定角色CD修正配置列表
    /// </summary>
    [ObservableProperty]
    private System.Collections.Generic.List<SkillCdRule> _customCdList = new();
}

/// <summary>
/// 角色CD修正规则
/// </summary>
public partial class SkillCdRule : ObservableObject
{
    [ObservableProperty]
    private string _roleName;

    [ObservableProperty]
    private string _cdValueText;

    [System.Text.Json.Serialization.JsonIgnore]
    [Newtonsoft.Json.JsonIgnore]
    public double? CdValue
    {
        get
        {
            if (string.IsNullOrWhiteSpace(CdValueText))
                return null;

            if (double.TryParse(
                    CdValueText,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var v))
            {
                return v;
            }

            return null;
        }
    }
}

