using System;
using System.Threading;
using OpenCvSharp;

namespace BetterGenshinImpact.GameTask.AutoFight.Model;

public class MultiGameStatus
{
    /// <summary>
    /// 是否在联机状态
    /// </summary>
    public bool IsInMultiGame { get; set; } = false;
    
    /// <summary>
    /// 是不是房主
    /// 我是房主的情况下
    /// 1人联机：最多控制4名角色
    /// 2人联机：最多控制2名角色
    /// 3人联机：最多控制2名角色
    /// 4人联机：最多控制1名角色
    /// 我不是房主的情况下
    /// 2人联机：最多控制2名角色
    /// 3人联机：最多控制1名角色
    /// 4人联机：最多控制1名角色
    /// </summary>
    public bool IsHost { get; set; } = false;
    
    /// <summary>
    /// 玩家数量，最少1人（我自己）
    /// </summary>
    public int PlayerCount { get; set; } = 1;
    
    /// <summary>
    /// 我能控制的最大角色数量
    /// </summary>
    public int MaxControlAvatarCount 
    {
        get
        {
            if (!IsInMultiGame)
            {
                return 4;
            }

            if (IsHost)
            {
                return PlayerCount switch
                {
                    1 => 4,
                    2 => 2,
                    3 => 2,
                    4 => 1,
                    _ => throw new ArgumentOutOfRangeException(nameof(PlayerCount), "自己为主机时，联机总人数异常")
                };
            }
            else
            {
                return PlayerCount switch
                {
                    2 => 2,
                    3 => 1,
                    4 => 1,
                    _ => throw new ArgumentOutOfRangeException(nameof(PlayerCount), "进入别人世界时，联机总人数异常")
                };
            }
        }
    }
}