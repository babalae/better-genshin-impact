using CommunityToolkit.Mvvm.ComponentModel;
using System;

namespace BetterGenshinImpact.GameTask.AutoFight;





/// <summary>
/// 自动战斗配置
/// </summary>
[Serializable]
public partial class AutoFightConfig : ObservableObject
{
    [ObservableProperty] private string _strategyName = "";

    /// <summary>
    /// 英文逗号分割 强制指定队伍角色
    /// </summary>
    [ObservableProperty] private string _teamNames = "";

    /// <summary>
    /// 检测战斗结束
    /// </summary>
    [ObservableProperty]
    private bool _fightFinishDetectEnabled = true;
    /// <summary>
    /// 根据技能CD优化出招人员
    /// 根据填入人或人和cd，来决定当此人元素战技cd未结束时，跳过此人出招，来优化战斗流程，可填入人名或人名数字（用逗号分隔），
    /// 多种用分号分隔，例如:白术;钟离,12;，如果人名，则用内置cd检查，如果是人名和数字，则把数字当做出招cd(秒)。
    /// </summary>
    [ObservableProperty] private string _actionSchedulerByCd = "";
    
    [Serializable]
    public partial class FightFinishDetectConfig : ObservableObject
    {
        /// <summary>
        /// 判断战斗结束读条颜色，不同帧率可能下会有些不同，默认为95,235,255
        /// </summary>
        [ObservableProperty]
        private string _battleEndProgressBarColor = "";

        /// <summary>
        /// 对于上方颜色地偏差值，即±某个值，例如 6或6,6,6，前者表示所有偏差值都一样，后者则可以分别设置
        /// </summary>
        [ObservableProperty]
        private string _battleEndProgressBarColorTolerance = "";
        
        
        /// <summary>
        /// 快速检查战斗结束，在一轮脚本中，可以每隔一定秒数（默认为5）或指定角色操作后，去检查（在每个角色完成该轮脚本时）。
        /// </summary>
        [ObservableProperty]
        private bool _fastCheckEnabled = false;
        
        /// <summary>
        /// 快速检查战斗结束的参数，可填入数字和人名，多种用分号分隔，例如:15,白术;钟离;，如果是数字（小于等于0则不会根据时间去检查），则指定检查间隔，如果是人名，则该角色执行一轮操作后进行检查。同时每轮结束后检查不变。
        /// </summary>
        [ObservableProperty]
        private string _fastCheckParams = "";
        
        /// <summary>
        /// 检查战斗结束的延时，即角色，默认为1.5秒。也可以指定特定角色之后延时多少时间检查。格式如：2.5;白术,1.5;钟离,1.0;
        /// </summary>
        [ObservableProperty]
        private string _checkEndDelay = "";

    }
    /// <summary>
    /// 战斗结束相关配置
    /// </summary>   
    [ObservableProperty]
    private FightFinishDetectConfig _finishDetectConfig = new();

    /// <summary>
    /// 检测战斗结束，默认为每轮脚本后检查
    /// </summary>
    [ObservableProperty]
    private bool _pickDropsAfterFightEnabled = true;

    [Serializable]
    public partial class PickDropsAfterFightConfig : ObservableObject
    {
        /// <summary>
        /// 前进次数
        /// </summary>
        [ObservableProperty]
        private int _forwardTimes = 6;

        /// <summary>
        /// 校准次数
        /// </summary>
        [ObservableProperty]
        private int _calibrationTimes = 15;

        /// <summary>
        /// 衰减因子
        /// </summary>
        [ObservableProperty]
        private double _decayFactor = 0.7;

        /// <summary>
        /// 前进量（秒），设置为0时在[1,3]中随机
        /// </summary>
        [ObservableProperty]
        private int _forwardSeconds = 2;

    }
    /// <summary>
    /// 掉落寻物相关配置
    /// </summary>   
    [ObservableProperty]
    private PickDropsAfterFightConfig _pickDropsConfig = new();

    /// <summary>
    /// 战斗结束后，如果存在枫原万叶，则使用该角色捡材料
    /// </summary>
    [ObservableProperty]
    private bool _kazuhaPickupEnabled = true;

    /// <summary>
    /// 战斗超时，单位秒
    /// </summary>
    [ObservableProperty]
    private int _timeout = 120;


}
