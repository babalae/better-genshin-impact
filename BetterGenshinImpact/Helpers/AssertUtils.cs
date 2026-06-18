using System;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.Common;
using Microsoft.Extensions.Logging;
using Vanara.PInvoke;

namespace BetterGenshinImpact.Helpers;

public class AssertUtils
{
    public static void IsTrue(bool b, string msg)
    {
        if (!b)
        {
            throw new System.Exception(msg);
        }
    }
    
    
    /// <summary>
    /// 强制要求游戏分辨率检查
    /// 要求功能必须处于启用状态
    /// </summary>
    public static void CheckGameResolution(string msg = "")
    {
        var taskContext = TaskContext.Instance();
        var gameScreenSize = taskContext is { IsInitialized: true, SystemInfo: not null }
            ? taskContext.SystemInfo.GameScreenSize
            : SystemControl.GetGameScreenRect(taskContext.GameHandle);
        if (!IsGameResolution16X9(gameScreenSize))
        {
            TaskControl.Logger.LogError("游戏窗口分辨率不是 16:9 ！当前分辨率为 {Width}x{Height} , 非 16:9 分辨率的游戏无法正常使用{Msg}功能 !", gameScreenSize.Width, gameScreenSize.Height, msg);
            throw new Exception("游戏窗口分辨率不是 16:9");
        }
    }

    public static bool IsGameResolution16X9(RECT gameScreenSize)
    {
        return gameScreenSize.Width * 9 == gameScreenSize.Height * 16;
    }
}
