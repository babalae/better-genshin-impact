using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.AutoTrackPath;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.Common.Job;
using Vanara.PInvoke;
using BetterGenshinImpact.GameTask.AutoFishing;
using BetterGenshinImpact.ViewModel.Pages;
using System;
using System.Linq;
using BetterGenshinImpact.GameTask.AutoPathing;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using OpenCvSharp;
using static BetterGenshinImpact.GameTask.Common.TaskControl;
using BetterGenshinImpact.GameTask.Common.Map;
using BetterGenshinImpact.GameTask.Common.Map.Maps.Base;

using BetterGenshinImpact.GameTask.Common.Exceptions;
using BetterGenshinImpact.GameTask.Common.Map.Maps;
using BetterGenshinImpact.Helpers.Extensions;
using System.Threading;
using BetterGenshinImpact.View;
using BetterGenshinImpact.Core.BgiVision;
using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask.Common;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.Core.Script.Dependence;

public class Genshin
{
    private RECT captureAreaRect = TaskContext.Instance().SystemInfo.CaptureAreaRect;

    /// <summary>
    /// 游戏宽度
    /// </summary>
    public int Width => captureAreaRect.Width;

    /// <summary>
    /// 游戏高度
    /// </summary>
    public int Height => captureAreaRect.Height;

    /// <summary>
    /// 游戏窗口大小相比1080P的缩放比例
    /// </summary>
    public double ScaleTo1080PRatio { get; } = TaskContext.Instance().SystemInfo.ScaleTo1080PRatio;

    /// <summary>
    /// 系统屏幕的DPI缩放比例
    /// </summary>
    public double ScreenDpiScale => TaskContext.Instance().DpiScale;
    
    public Lazy<NavigationInstance> LazyNavigationInstance { get; } = new(() =>
    {
        var matchingMethod = TaskContext.Instance().Config.PathingConditionConfig.MapMatchingMethod;
        Navigation.WarmUp(matchingMethod);
        return new NavigationInstance();
    });

    /// <summary>
    /// 传送到指定位置
    /// </summary>
    /// <param name="x"></param>
    /// <param name="y"></param>
    /// <returns></returns>
    public async Task Tp(double x, double y)
    {
        await new TpTask(CancellationContext.Instance.Cts.Token).Tp(x, y);
    }

    public async Task Tp(double x, double y, string mapName, bool force)
    {
        await new TpTask(CancellationContext.Instance.Cts.Token).Tp(x, y, mapName, force);
    }


    public async Task Tp(double x, double y, bool force)
    {
        await new TpTask(CancellationContext.Instance.Cts.Token).Tp(x, y, MapTypes.Teyvat.ToString(), force);
    }

    /// <summary>
    /// 传送到指定位置
    /// </summary>
    /// <param name="x"></param>
    /// <param name="y"></param>
    /// <returns></returns>
    public async Task Tp(string x, string y)
    {
        double.TryParse(x, out var dx);
        double.TryParse(y, out var dy);
        await Tp(dx, dy);
    }

    public async Task Tp(string x, string y, bool force)
    {
        double.TryParse(x, out var dx);
        double.TryParse(y, out var dy);
        await Tp(dx, dy, force);
    }


    #region 大地图操作

    /// <summary>
    /// 移动大地图到指定坐标
    /// </summary>
    /// <remarks>
    /// 与内置传送功能不同，此方法不会多次重试。
    /// 为避免初次中心点识别失败，建议先使用 SetBigMapZoomLevel 设置合适的大地图缩放等级。
    /// </remarks>
    /// <param name="x">目标X坐标</param>
    /// <param name="y">目标Y坐标</param>
    /// <param name="forceCountry">强制指定移动大地图时先切换的国家，默认为null</param>
    public async Task MoveMapTo(double x, double y, string? forceCountry = null)
    {
        TpTask tpTask = new TpTask(CancellationContext.Instance.Cts.Token);
        await tpTask.CheckInBigMapUi();
        await tpTask.SwitchRecentlyCountryMap(x, y, forceCountry);
        await tpTask.MoveMapTo(x, y, MapTypes.Teyvat.ToString());
    }

    /// <summary>
    /// 移动大地图到指定坐标
    /// </summary>
    /// <remarks>
    /// 与内置传送功能不同，此方法不会多次重试。
    /// 为避免初次中心点识别失败，建议先使用 SetBigMapZoomLevel 设置合适的大地图缩放等级。
    /// </remarks>
    /// <param name="x">目标X坐标</param>
    /// <param name="y">目标Y坐标</param>
    /// <param name="mapName">指定要移动的大地图</param>
    public async Task MoveIndependentMapTo(int x, int y, string mapName, string? forceCountry = null)
    {
        TpTask tpTask = new TpTask(CancellationContext.Instance.Cts.Token);
        await tpTask.CheckInBigMapUi();
        // 切换地区
        if (mapName == MapTypes.Teyvat.ToString())
        {
            // 计算传送点位置离哪张地图切换后的中心点最近，切换到该地图
            await tpTask.SwitchRecentlyCountryMap(x, y, forceCountry);
        }
        else
        {
            // 直接切换地区
            await tpTask.SwitchArea(MapTypesExtensions.ParseFromName(mapName).GetDescription());
        }
        await tpTask.MoveMapTo(x, y, mapName);
    }

    /// <summary>
    /// 获取当前大地图缩放等级
    /// </summary>
    /// <returns>当前大地图缩放等级，范围1.0-6.0</returns>
    public double GetBigMapZoomLevel()
    {
        TpTask tpTask = new(CancellationContext.Instance.Cts.Token);
        return tpTask.GetBigMapZoomLevel(CaptureToRectArea());
    }

    /// <summary>
    /// 将大地图缩放等级设置为指定值
    /// </summary>
    /// <remarks>
    /// 缩放等级说明：
    /// - 数值范围：1.0(最大地图) 到 6.0(最小地图)
    /// - 缩放效果：数值越大，地图显示范围越广，细节越少
    /// - 缩放位置：1.0 对应缩放条最上方，6.0 对应缩放条最下方
    /// - 推荐范围：建议在 2.0 到 5.0 之间调整，过大或过小可能影响操作
    /// </remarks>
    /// <param name="zoomLevel">目标缩放等级，范围 1.0-6.0</param>
    public async Task SetBigMapZoomLevel(double zoomLevel)
    {
        TpTask tpTask = new(CancellationContext.Instance.Cts.Token);
        double currentZoomLevel = GetBigMapZoomLevel();
        await tpTask.AdjustMapZoomLevel(currentZoomLevel, zoomLevel);
    }

    /// <summary>
    /// 传送到用户指定的七天神像
    /// </summary>
    public async Task TpToStatueOfTheSeven()
    {
        TpTask tpTask = new TpTask(CancellationContext.Instance.Cts.Token);
        await tpTask.TpToStatueOfTheSeven();
    }

    /// <summary>
    /// 获取当前在大地图上的位置坐标
    /// </summary>
    /// <returns>包含X和Y坐标的Point2f结构体</returns>
    public Point2f? GetPositionFromBigMap()
    {
        TpTask tpTask = new TpTask(CancellationContext.Instance.Cts.Token);
        return tpTask.GetPositionFromBigMap(MapTypes.Teyvat.ToString());
    }

    /// <summary>
    /// 获取当前在大地图上的位置坐标
    /// </summary>
    /// <param name="mapName">大地图名称</param>
    /// <returns>包含X和Y坐标的Point2f结构体</returns>
    public Point2f? GetPositionFromBigMap(string mapName)
    {
        TpTask tpTask = new TpTask(CancellationContext.Instance.Cts.Token);
        return tpTask.GetPositionFromBigMap(mapName);
    }

    /// <summary>
    /// 获取当前在小地图上的位置坐标
    /// </summary>
    /// <returns>包含X和Y坐标的Point2f结构体</returns>
    public Point2f? GetPositionFromMap()
    {
        return GetPositionFromMap(MapTypes.Teyvat.ToString());
    }

    public float GetCameraOrientation()
    {
        var imageRegion = CaptureToRectArea();
        return CameraOrientation.Compute(imageRegion.SrcMat);
    }

    /// <summary>
    /// 获取当前在小地图上的位置坐标，如果缓存时间内有匹配成功的坐标优先返回缓存坐标，否则调用NavigationInstance的getPositionStable
    /// </summary>
    /// <param name="mapName">大地图名称</param>
    /// <param name="cacheTimeMs">缓存时间，单位毫秒，默认900ms</param>
    /// <returns>包含X和Y坐标的Point2f结构体</returns>
    public Point2f? GetPositionFromMap(string mapName, int cacheTimeMs = 900)
    {
        var imageRegion = CaptureToRectArea();
        if (!Bv.IsInMainUi(imageRegion))
        {
            throw new InvalidOperationException("不在主界面，无法识别小地图坐标");
        }

        var matchingMethod = TaskContext.Instance().Config.PathingConditionConfig.MapMatchingMethod;
        return MapManager.GetMap(mapName, matchingMethod)
            .ConvertImageCoordinatesToGenshinMapCoordinates(LazyNavigationInstance.Value
                .GetPositionStableByCache(imageRegion, mapName, matchingMethod, cacheTimeMs));
    }
    
    /// <summary>
    /// 获取当前在小地图上的位置坐标, 局部匹配, 需要世界坐标, 在坐标附近匹配, 失败不进行全局匹配
    /// </summary>
    /// <param name="mapName">大地图名称</param>
    /// <param name="x">世界坐标x</param>
    /// <param name="y">世界坐标y</param>
    /// <returns>包含X和Y坐标的Point2f结构体</returns>
    public Point2f? GetPositionFromMap(string mapName, float x, float y)
    {
        var imageRegion = CaptureToRectArea();
        if (!Bv.IsInMainUi(imageRegion))
        {
            throw new InvalidOperationException("不在主界面，无法识别小地图坐标");
        }
        var matchingMethod = TaskContext.Instance().Config.PathingConditionConfig.MapMatchingMethod;
        var sceneMap = MapManager.GetMap(mapName, matchingMethod);
        var navigationInstance = LazyNavigationInstance.Value;
        var pos = sceneMap.ConvertGenshinMapCoordinatesToImageCoordinates(new Point2f(x, y));
        navigationInstance.SetPrevPosition(pos.X, pos.Y);
        return sceneMap.ConvertImageCoordinatesToGenshinMapCoordinates(navigationInstance.GetPosition(imageRegion, mapName, matchingMethod));
    }

    #endregion 大地图操作

    /// <summary>
    /// 切换队伍
    /// </summary>
    /// <param name="partyName">队伍界面自定义的队伍名称</param>
    /// <returns></returns>
    public async Task<bool> SwitchParty(string partyName)
    {
        try
        {
            return await new SwitchPartyTask().Start(partyName, CancellationContext.Instance.Cts.Token);
        }
        catch (PartySetupFailedException ex)
        {
            return false;//释放失败状态到JS，否则失败后会退出任务。
        }
    }
    
    /// <summary>
    /// 清除当前调度器的队伍缓存
    /// </summary>
    public void ClearPartyCache()
    {
        RunnerContext.Instance.ClearCombatScenes();
    }


    /// <summary>
    /// 自动点击空月祝福
    /// </summary>
    /// <returns></returns>
    public async Task BlessingOfTheWelkinMoon()
    {
        await new BlessingOfTheWelkinMoonTask().Start(CancellationContext.Instance.Cts.Token);
    }

    /// <summary>
    /// 持续对话并选择目标选项
    /// </summary>
    /// <param name="option">选项文本</param>
    /// <param name="skipTimes">跳过次数</param>
    /// <param name="isOrange">是否为橙色选项</param>
    /// <returns></returns>
    public async Task ChooseTalkOption(string option, int skipTimes = 10, bool isOrange = false)
    {
        await new ChooseTalkOptionTask().SingleSelectText(option, CancellationContext.Instance.Cts.Token, skipTimes, isOrange);
    }

    /// <summary>
    /// 一键领取纪行奖励
    /// </summary>
    /// <returns></returns>
    public async Task ClaimBattlePassRewards()
    {
        await new ClaimBattlePassRewardsTask().Start(CancellationContext.Instance.Cts.Token);
    }

    /// <summary>
    /// 领取长效历练点奖励
    /// </summary>
    /// <returns></returns>
    public async Task ClaimEncounterPointsRewards()
    {
        await new ClaimEncounterPointsRewardsTask().Start(CancellationContext.Instance.Cts.Token);
    }

    /// <summary>
    /// 前往冒险家协会领取奖励
    /// </summary>
    /// <param name="country">国家名称</param>
    /// <returns></returns>
    public async Task GoToAdventurersGuild(string country)
    {
        await new GoToAdventurersGuildTask().Start(country, CancellationContext.Instance.Cts.Token);
    }

    /// <summary>
    /// 前往合成台
    /// </summary>
    /// <param name="country">国家名称</param>
    /// <returns></returns>
    public async Task GoToCraftingBench(string country)
    {
        await new GoToCraftingBenchTask().Start(country, CancellationContext.Instance.Cts.Token);
    }

    /// <summary>
    /// 返回主界面
    /// </summary>
    /// <returns></returns>
    public async Task ReturnMainUi()
    {
        await new ReturnMainUiTask().Start(CancellationContext.Instance.Cts.Token);
    }

    /// <summary>
    /// 钓鱼
    /// </summary>
    /// <returns></returns>
    public async Task AutoFishing(int fishingTimePolicy = 0)
    {
        var taskSettingsPageViewModel = App.GetService<TaskSettingsPageViewModel>();
        if (taskSettingsPageViewModel == null)
        {
            throw new ArgumentNullException(nameof(taskSettingsPageViewModel), "内部视图模型对象为空");
        }

        var param = AutoFishingTaskParam.BuildFromConfig(TaskContext.Instance().Config.AutoFishingConfig, taskSettingsPageViewModel.SaveScreenshotOnKeyTick);
        param.FishingTimePolicy = (FishingTimePolicy)fishingTimePolicy;
        await new AutoFishingTask(param).Start(CancellationContext.Instance.Cts.Token);
    }

    /// <summary>
    /// 重新登录原神
    /// </summary>
    /// <returns></returns>
    public async Task Relogin()
    {
        await new ExitAndReloginJob().Start(CancellationContext.Instance.Cts.Token);
    }
    
    /// <summary>
    /// 进出千星奇域
    /// </summary>
    /// <returns></returns>
    public async Task WonderlandCycle()
    {
        await new EnterAndExitWonderlandJob().Start(CancellationContext.Instance.Cts.Token);
    }

    /// <summary>
    /// 调整时间
    /// </summary>
    /// <param name="hour">目标小时(0-24)</param>
    /// <param name="minute">目标分钟(0-59)</param>
    /// <param name="skip">是否跳过动画（默认为否）</param>
    /// <returns></returns>
    public async Task SetTime(int hour, int minute, bool skip = false)
    {
        if ( hour < 0 || hour > 24)
            throw new ArgumentException($"无效的小时值: {hour}，必须是 0-24 之间的整数字符", nameof(hour));
        if (minute < 0 || minute > 59)
            throw new ArgumentException($"无效的分钟值: {minute}，必须是 0-59 之间的整数字符", nameof(minute));
        await new SetTimeTask().Start(hour, minute, CancellationContext.Instance.Cts.Token, skip);
    }
    
    /// <summary>
    /// 调整时间
    /// </summary>
    /// <param name="hour">目标小时(0-24的字符类型)</param>
    /// <param name="minute">目标分钟(0-59的字符类型)</param>
    /// <param name="skip">是否跳过动画（默认为否）</param>
    /// <returns></returns>
    public async Task SetTime(string hour, string minute, bool skip = false)
    {
        if (!int.TryParse(hour, out var h) || h < 0 || h > 24)
            throw new ArgumentException($"无效的小时值: {hour}，必须是 0-24 之间的整数字符", nameof(hour));
        if (!int.TryParse(minute, out var m) || m < 0 || m > 59)
            throw new ArgumentException($"无效的分钟值: {minute}，必须是 0-59 之间的整数字符", nameof(minute));
        await new SetTimeTask().Start(h, m, CancellationContext.Instance.Cts.Token, skip);
    }

    /// <summary>
    /// 修改游戏分辨率，在变更期间暂停截图器但不会中断当前 JS 脚本执行。
    /// 变更完成后截图器自动重启，所有分辨率相关资源（触发器、模板等）会被重置。
    /// </summary>
    /// <param name="width">目标宽度</param>
    /// <param name="height">目标高度</param>
    /// <remarks>
    /// 注意：分辨率变更后，此前通过 dispatcher.addTimer / dispatcher.addTrigger 注册的自定义触发器配置会丢失
    /// </remarks>
    public async Task ChangeResolution(int width, int height)
    {
        var dispatcher = TaskTriggerDispatcher.Instance();

        if (dispatcher.IsResolutionChanging)
        {
            throw new InvalidOperationException("已经在进行分辨率变更中，请勿重复调用");
        }

        dispatcher.IsResolutionChanging = true;
        try
        {
            var ct = CancellationContext.Instance.Cts.Token;
            var page = new BvPage(ct);
            var captureRect = TaskContext.Instance().SystemInfo.ScaleMax1080PCaptureRect;
            var targetW = width.ToString();
            var targetH = height.ToString();

            await new ReturnMainUiTask().Start(ct);

            var escSettingsRo = ElementAssets.Instance.EscSettingsRo.Clone();
            escSettingsRo.DrawOnWindow = false;
            var pageCloseRo = ElementAssets.Instance.PageCloseWhiteRo.Clone();
            pageCloseRo.DrawOnWindow = false;

            if (!await NewRetry.WaitForElementAppear(escSettingsRo,
                    () => page.Keyboard.KeyPress(User32.VK.VK_ESCAPE), ct, 5, 1500))
            {
                throw new TimeoutException("打开菜单超时");
            }

            if (!await NewRetry.WaitForElementAppear(pageCloseRo,
                    () => page.Click(45, 825), ct, 5, 1500))
            {
                throw new TimeoutException("打开设置页面超时");
            }

            var viewModeRo = new RecognitionObject
            {
                RecognitionType = RecognitionTypes.Ocr,
                RegionOfInterest = new Rect(
                    (int)(450 * captureRect.Width / 1920),
                    (int)(200 * captureRect.Height / 1080),
                    (int)(200 * captureRect.Width / 1920),
                    (int)(200 * captureRect.Height / 1080)),
                Text = "显示模式"
            };
            if (!await NewRetry.WaitForElementAppear(viewModeRo,
                    () => page.GetByText("图像").WithRoi(captureRect.CutLeft(0.20)).Click(), ct, 5, 1500))
            {
                throw new TimeoutException("切换到图像标签超时");
            }

            var viewModeRegion = page.GetByText("显示模式")
                .WithRoi(new Rect(
                    (int)(450 * captureRect.Width / 1920),
                    (int)(200 * captureRect.Height / 1080),
                    (int)(200 * captureRect.Width / 1920),
                    (int)(200 * captureRect.Height / 1080)))
                .FindAll().FirstOrDefault()
                ?? throw new TimeoutException("未找到「显示模式」选项");

            page.Click(
                (viewModeRegion.X + 1100 * captureRect.Width / 1920) * 1920.0 / captureRect.Width,
                (viewModeRegion.Y + 20 * captureRect.Height / 1080) * 1080.0 / captureRect.Height);
            await page.Wait(1500);

            Simulation.SendInput.Mouse.MoveMouseBy(0, 100);
            await page.Wait(200);
            for (var count = 0; count < 20; count++)
            {
                Simulation.SendInput.Mouse.VerticalScroll(100);
                await page.Wait(200);
            }

            await page.Wait(500);

            var listRegion = new Rect(
                (int)(1400 * captureRect.Width / 1920),
                (int)(300 * captureRect.Height / 1080),
                (int)(400 * captureRect.Width / 1920),
                (int)(600 * captureRect.Height / 1080));
            var resolutionFound = false;

            for (var scrollAttempt = 0; scrollAttempt < 30; scrollAttempt++)
            {
                Simulation.SendInput.Mouse.VerticalScroll(-100);
                await page.Wait(1000);

                var allResults = page.GetByText(string.Empty, listRegion).FindAll();
                // 按行聚合
                foreach (var line in allResults.GroupBy(r => r.Y / (int)(20 * captureRect.Height / 1080.0)))
                {
                    var texts = line.Select(r => r.Text).ToList();
                    if (texts.Any(t => t.Contains(targetW))
                        && texts.Any(t => t.Contains(targetH))
                        && !texts.Any(t => t.Contains("全屏") || t.Contains("独占")))
                    {
                        line.First(r => r.Text.Contains(targetW) || r.Text.Contains(targetH)).Click();
                        resolutionFound = true;
                        break;
                    }
                }

                if (resolutionFound) break;
            }

            if (!resolutionFound)
            {
                throw new TimeoutException($"未在下拉列表中找到分辨率 {width}x{height}");
            }

            await page.Wait(1000);

            await new ReturnMainUiTask().Start(ct);

            // 暂停截图定时器，等待分辨率生效
            dispatcher.StopTimer();

            // 更新缓存的分辨率指标
            GlobalMethod.SetGameMetrics(width, height);

            // 通过截图轮询检测分辨率是否已变更（与原分辨率比较，有差异即成功，部分电脑可设置的最大分辨率与实际情况不匹配，只能做此处理）
            Logger.LogInformation("等待游戏分辨率变更...");
            var gameCapture = dispatcher.GameCapture
                ?? throw new InvalidOperationException("截图器未初始化");
            using (var origBitmap = gameCapture.Capture())
            {
                var origW = origBitmap?.Width ?? 0;
                var origH = origBitmap?.Height ?? 0;
                var waitStart = DateTime.Now;
                var timeout = TimeSpan.FromSeconds(10);
                var changed = false;
                while ((DateTime.Now - waitStart) < timeout)
                {
                    await Task.Delay(500, ct);
                    try
                    {
                        using var bitmap = gameCapture.Capture();
                        if (bitmap != null && (bitmap.Width != origW || bitmap.Height != origH))
                        {
                            changed = true;
                            break;
                        }
                    }
                    catch
                    {
                        // 分辨率切换过程中截图可能暂时失败
                    }
                }

                if (!changed)
                {
                    throw new TimeoutException("等待分辨率变更超时（10秒）");
                }
            }

            // 重新初始化任务上下文
            var hWnd = TaskContext.Instance().GameHandle;
            TaskContext.Instance().Init(hWnd);
            // 重建截图器
            var captureMode = Enum.Parse<Fischless.GameCapture.CaptureModes>(TaskContext.Instance().Config.CaptureMode);
            dispatcher.RebuildCapture(hWnd, captureMode);
            // 重建触发器
            var triggers = GameTaskManager.LoadInitialTriggers();
            dispatcher.SetTriggers(triggers);
            // 更新内部游戏区域跟踪
            var newRect = SystemControl.GetCaptureRect(hWnd);
            TaskContext.Instance().SystemInfo.CaptureAreaRect = newRect;
            MaskWindow.Instance().RefreshPosition();
            // 恢复截图定时器，清标志（setter 会自动同步 _gameRect）
            dispatcher.StartTimer();
        }
        finally
        {
            // 确保定时器恢复运行
            try
            {
                dispatcher.StartTimer();
            }
            catch
            {
                // 忽略重复启动
            }

            dispatcher.IsResolutionChanging = false;
        }
    }
}