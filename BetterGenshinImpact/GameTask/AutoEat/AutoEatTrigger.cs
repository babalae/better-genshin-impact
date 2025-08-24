using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.Core.Simulator.Extensions;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.GameTask.AutoEat.Assets;
using BetterGenshinImpact.Service.Notification;
using BetterGenshinImpact.Service.Notification.Model.Enum;
using Microsoft.Extensions.Logging;
using System;
using static BetterGenshinImpact.GameTask.Common.TaskControl;
using static Vanara.PInvoke.User32;

namespace BetterGenshinImpact.GameTask.AutoEat;

/// <summary>
/// 自动吃药触发器
/// 检测红血状态时自动使用Recovery.png，检测到Resurrection.png时按z复活
/// </summary>
public class AutoEatTrigger : ITaskTrigger
{
    private readonly ILogger<AutoEatTrigger> _logger = App.GetLogger<AutoEatTrigger>();

    public string Name => "自动吃药";
    public bool IsEnabled { get; set; }
    public int Priority => 25; // 中等优先级
    public bool IsExclusive => false;

    private readonly AutoEatConfig _config;
    private DateTime _lastRecoveryCheckTime = DateTime.MinValue;
    private DateTime _lastResurrectionTime = DateTime.MinValue;
    private DateTime _lastEatTime = DateTime.MinValue;
    private bool _recoveryDetected = false;
    
    private DateTime _prevExecute = DateTime.MinValue;

    public AutoEatTrigger()
    {
        _config = TaskContext.Instance().Config.AutoEatConfig;
    }

    public void Init()
    {
        IsEnabled = _config.Enabled;
    }

    public void OnCapture(CaptureContent content)
    {
        if ((DateTime.Now - _prevExecute).TotalMilliseconds <= _config.CheckInterval)
        {
            return;
        }
        _prevExecute = DateTime.Now;

        try
        {
            var ra = content.CaptureRectArea;
            var now = DateTime.Now;

            // 检测角色是否红血
            if (Bv.CurrentAvatarIsLowHp(ra))
            {
                // 检查Recovery缓存是否过期（30秒）
                if ((now - _lastRecoveryCheckTime).TotalSeconds >= 30)
                {
                    _recoveryDetected = false;
                    _lastRecoveryCheckTime = now;
                }

                // 如果Recovery还在缓存期内，或者重新检测到Recovery
                if (_recoveryDetected || CheckRecovery(ra))
                {
                    if (!_recoveryDetected)
                    {
                        _recoveryDetected = true;
                        _lastRecoveryCheckTime = now;
                    }

                    // 检查吃药间隔，防止频繁吃药
                    if ((now - _lastEatTime).TotalMilliseconds >= _config.EatInterval)
                    {
                        // 使用便携营养袋
                        Simulation.SendInput.SimulateAction(GIActions.QuickUseGadget);
                        _lastEatTime = now;
                        
                        _logger.LogInformation("检测到红血且不在CD，自动吃药");
                    }
                }
            }
            
            // 检测复活图标，添加2秒CD
            if (CheckResurrection(ra))
            {
                // 检查复活CD（2秒）
                if ((now - _lastResurrectionTime).TotalSeconds >= 2)
                {
                    // 按z键复活
                    Simulation.SendInput.Keyboard.KeyPress(VK.VK_Z);
                    _lastResurrectionTime = now;
                    _logger.LogInformation("检测到复活图标，自动复活");
                }
            }
        }
        catch (Exception e)
        {
            _logger.LogDebug(e, "自动吃药检测时发生异常");
        }
    }

    /// <summary>
    /// 检测Recovery.png图标
    /// </summary>
    private bool CheckRecovery(ImageRegion imageRegion)
    {
        try
        {
            var result = imageRegion.Find(AutoEatAssets.Instance.RecoveryIconRa);
            return result.IsExist();
        }
        catch (Exception e)
        {
            _logger.LogDebug(e, "检测Recovery图标时发生异常");
            return false;
        }
    }

    /// <summary>
    /// 检测Resurrection.png图标
    /// </summary>
    private bool CheckResurrection(ImageRegion imageRegion)
    {
        try
        {
            var result = imageRegion.Find(AutoEatAssets.Instance.ResurrectionIconRa);
            return result.IsExist();
        }
        catch (Exception e)
        {
            _logger.LogDebug(e, "检测Resurrection图标时发生异常");
            return false;
        }
    }
}