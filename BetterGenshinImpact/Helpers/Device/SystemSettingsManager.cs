using BetterGenshinImpact.GameTask.Common;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.Helpers.Device;

public class SystemSettingsManager
{
    //   1. 在启动后强制设置成鼠标灵敏度设置：10， 滚轮一次滚动行数：3；结束后还原
    private static uint _mouseSpeed = 10;
    private static uint _wheelScrollLines = 3;

    public static void GetSystemSettings()
    {
        _mouseSpeed = EnvironmentUtil.GetMouseSpeed(); // 获取鼠标灵敏度
        _wheelScrollLines = EnvironmentUtil.GetWheelScrollLines(); // 获取滚轮滚动行数
        TaskControl.Logger.LogDebug("当前鼠标灵敏度：{M1}, 滚轮滚动行数 {M2}", _mouseSpeed, _wheelScrollLines);
    }
    
    public static void SetSystemSettings()
    {
        MouseSpeedSettings.SetMouseSpeed(10); // 设置鼠标灵敏度
        MouseSettings.SetMouseWheelScrollLines(3); // 设置滚轮滚动行数
        
        TaskControl.Logger.LogDebug("强制设置后，当前鼠标灵敏度：{M1}, 滚轮滚动行数 {M2}", EnvironmentUtil.GetMouseSpeed(), EnvironmentUtil.GetWheelScrollLines());
    }

    public static void RestoreSystemSettings()
    {
        MouseSpeedSettings.SetMouseSpeed(_mouseSpeed); // 设置鼠标灵敏度
        MouseSettings.SetMouseWheelScrollLines(_wheelScrollLines); // 设置滚轮滚动行数
        
        TaskControl.Logger.LogDebug("还原设置后，当前鼠标灵敏度：{M1}, 滚轮滚动行数 {M2}", EnvironmentUtil.GetMouseSpeed(), EnvironmentUtil.GetWheelScrollLines());

    }
}