using BetterGenshinImpact.GameTask.Model;

namespace BetterGenshinImpact.GameTask.AutoLeyLineOutcrop;

public class AutoLeyLineOutcropParam:BaseTaskParam<AutoLeyLineOutcropTask>
{

    // 刷取次数
    public int Count { get; set; }
    // 地区
    public string Country { get; set; }
    //地脉花类型
    public string LeyLineOutcropType { get; set; }
    // 开启取小值模式
    public bool OpenModeCountMin { get; set; }
    // 是否开启树脂耗尽模式
    public bool IsResinExhaustionMode { get; set; }
    //是否使用冒险之证寻找地脉花
    public bool UseAdventurerHandbook { get; set; }
    //好感队名称
    public string FriendshipTeam { get; set; }
    //战斗的队伍名称
    public string Team { get; set; }
    //战斗超时时间
    public int Timeout { get; set; }
    //是否前往合成台合成浓缩树脂
    public bool IsGoToSynthesizer { get; set; }
    //是否使用脆弱树脂
    public bool UseFragileResin { get; set; }
    //是否使用须臾树脂
    public bool UseTransientResin { get; set; }
    //通过BGI通知系统发送详细通知
    public bool IsNotification { get; set; }
    
    public void SetDefault()
    {
        var config = TaskContext.Instance().Config.AutoLeyLineOutcropConfig;
        SetAutoLeyLineOutcropConfig(config);
    }

    public void SetAutoLeyLineOutcropConfig(AutoLeyLineOutcropConfig config)
    {
        OpenModeCountMin= config.OpenModeCountMin;
        IsResinExhaustionMode= config.IsResinExhaustionMode;
        UseAdventurerHandbook= config.UseAdventurerHandbook;
        FriendshipTeam= config.FriendshipTeam;
        Team= config.Team;
        Timeout= config.Timeout;
        IsGoToSynthesizer=config.IsGoToSynthesizer;
        UseFragileResin= config.UseFragileResin;
        UseTransientResin= config.UseTransientResin;
        IsNotification= config.IsNotification;
        Count = config.Count;
        Country = config.Country;
        LeyLineOutcropType = config.LeyLineOutcropType;
    }

    public AutoLeyLineOutcropParam() : base(null, null)
    {
        SetDefault();
    }
    
    public AutoLeyLineOutcropParam(int count, string country, string leyLineOutcropType) : base(null, null)
    {
        SetDefault();
        this.Count = count;
        this.Country = country;
        this.LeyLineOutcropType = leyLineOutcropType;
    }
}