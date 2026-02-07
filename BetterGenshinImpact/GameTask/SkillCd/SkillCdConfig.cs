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
    
    /// <summary>
    /// 横坐标
    /// </summary>
    [ObservableProperty]
    private double _pX = 1520.0;

    partial void OnPXChanged(double value)
    {
        if (value < 0.0) PX = 0.0;
        else if (value > 1920.0) PX = 1920.0;
    }
    
    /// <summary>
    /// 纵坐标
    /// </summary>
    [ObservableProperty]
    private double _pY = 245.0;

    partial void OnPYChanged(double value)
    {
        if (value < 0.0) PY = 0.0;
        else if (value > 1080.0) PY = 1080.0;
    }
    
    /// <summary>
    /// 计时器间隔
    /// </summary>
    [ObservableProperty]
    private double _gap = 91.2;

    partial void OnGapChanged(double value)
    {
        if (value < 0.0) Gap = 0.0;
        else if (value > 200.0) Gap = 200.0;
    }
    
    /// <summary>
    /// 计时器缩放
    /// </summary>
    [ObservableProperty]
    private double _scale = 1.0;

    partial void OnScaleChanged(double value)
    {
        if (value < 0.0) Scale = 0.0;
        else if (value > 10.0) Scale = 10.0;
    }
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

