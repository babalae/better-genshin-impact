using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Recognition.OCR;
using BetterGenshinImpact.GameTask.AutoFight.Assets;
using BetterGenshinImpact.GameTask.AutoFight.Config;
using BetterGenshinImpact.GameTask.AutoFight.Model;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using BetterGenshinImpact.View.Drawable;
using BetterGenshinImpact.GameTask.Model.Area;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Vanara.PInvoke;
using Point = System.Windows.Point;
using Rect = OpenCvSharp.Rect;

namespace BetterGenshinImpact.GameTask.SkillCd;

/// <summary>
/// 技能 CD 提示触发器
/// </summary>
public class SkillCdTrigger : ITaskTrigger
{
    public string Name => "SkillCd";
    public bool IsEnabled
    {
        get => TaskContext.Instance().Config.SkillCdConfig.Enabled;
        set => TaskContext.Instance().Config.SkillCdConfig.Enabled = value;
    }

    public int Priority => 10;
    public bool IsExclusive => false;
    /// <summary>
    /// 在所有UI场景下都运行（包括大地图），确保遮罩层能处理消失
    /// </summary>
    public GameUiCategory SupportedGameUiCategory => GameUiCategory.Unknown;

    private readonly double[] _cds = new double[4];
    private readonly bool[] _prevKeys = new bool[4];
    private bool _prevEKey = false;
    private DateTime _lastEKeyPress = DateTime.MinValue;
    private readonly DateTime[] _lastSetTime = new DateTime[4];
    private string[] _teamAvatarNames = new string[4];
    private Rect[] _teamIndexRects = new Rect[4];

    private DateTime _lastTickTime = DateTime.Now;
    private DateTime _contextEnterTime = DateTime.MinValue;
    /// <summary>
    /// 离开场景时间，用于0.8秒防抖避免识别失误导致UI闪烁（仅影响UI渲染，不影响CD计时）
    /// </summary>
    private DateTime _contextLeaveTime = DateTime.MinValue;
    private bool _wasInContext = false;
    
    /// <summary>
    /// 上一次激活的角色索引（1-4），用于检测当前激活角色切换
    /// </summary>
    private int _lastActiveIndex = -1;
    /// <summary>
    /// 上一次的队伍配置
    /// </summary>
    private string[] _lastTeamAvatarNames = new string[4];

    private int _lastSwitchFromSlot = -1;
    private DateTime _lastSwitchTime = DateTime.MinValue;
    private DateTime _lastPressIndexTime = DateTime.MinValue; // 换人按键时间


    private volatile bool _isSyncingTeam = false;

    private DateTime _lastSyncTime = DateTime.MinValue;

    private ImageRegion? _lastImage = null; // 上一帧
    private ImageRegion? _penultimateImage = null; // 上上帧（倒数第二帧）
    private readonly object _stateLock = new();
    private readonly ILogger _logger = TaskControl.Logger;
    private readonly AvatarActiveCheckContext _activeCheckContext = new();

    /// <summary>
    /// 初始化
    /// </summary>
    public void Init()
    {
        // 清空帧缓存
        _lastImage?.Dispose();
        _lastImage = null;
        _penultimateImage?.Dispose();
        _penultimateImage = null;
        for (int i = 0; i < 4; i++)
        {
            _cds[i] = 0;
            _prevKeys[i] = false;
            _teamAvatarNames[i] = string.Empty;
            _teamIndexRects[i] = default;
            _lastSetTime[i] = DateTime.MinValue;
            _lastTeamAvatarNames[i] = string.Empty;
        }

        _prevEKey = false;
        _lastEKeyPress = DateTime.MinValue;
        _wasInContext = false;
        _contextEnterTime = DateTime.MinValue;
        _contextLeaveTime = DateTime.MinValue;
        _lastTickTime = DateTime.Now;
        _lastActiveIndex = -1;
        _lastSwitchFromSlot = -1;
        _lastSwitchTime = DateTime.MinValue;
        _lastPressIndexTime = DateTime.MinValue;
        _lastSyncTime = DateTime.MinValue;

        if (!IsEnabled)
        {
            VisionContext.Instance().DrawContent.PutOrRemoveTextList("SkillCdText", null);
        }
    }

    /// <summary>
    /// 截图回调处理
    /// </summary>
    public void OnCapture(CaptureContent content)
    {
        if (!IsEnabled)
        {
            VisionContext.Instance().DrawContent.PutOrRemoveTextList("SkillCdText", null);
            return;
        }

        var now = DateTime.Now;

        var delta = (now - _lastTickTime).TotalSeconds;
        _lastTickTime = now;

        // CD计时器持续运行
        if (delta >= 0 && delta < 5)
        {
            for (int i = 0; i < 4; i++)
            {
                if (_cds[i] > 0)
                {
                    _cds[i] -= delta;
                    if (_cds[i] < 0) _cds[i] = 0;
                }
            }
        }

        // 场景检测（带0.5秒防抖，仅影响UI渲染）
        bool rawInContext = Bv.IsInMainUi(content.CaptureRectArea) || Bv.IsInDomain(content.CaptureRectArea);
        bool isInContext;
        
        if (rawInContext)
        {
            var multiGameStatus = PartyAvatarSideIndexHelper.DetectedMultiGameStatus(content.CaptureRectArea);
            if (multiGameStatus.IsInMultiGame)
            {
                // 检测到联机状态，自动关闭SkillCd
                IsEnabled = false;
                _logger.LogWarning("检测到联机状态，自动关闭冷却提示");
                return;
            }
            _contextLeaveTime = DateTime.MinValue;
            isInContext = true;
        }
        else
        {
            if (_wasInContext && _contextLeaveTime == DateTime.MinValue)
            {
                _contextLeaveTime = now;
            }

            // 离开后0.8秒内仍视为在场景中，防止识别失误
            isInContext = _contextLeaveTime != DateTime.MinValue &&
                          (now - _contextLeaveTime).TotalSeconds < 0.8;
        }

        // 离开场景时隐藏UI，但保留角色信息和CD数据
        if (!isInContext)
        {
            if (_wasInContext)
            {
                VisionContext.Instance().DrawContent.PutOrRemoveTextList("SkillCdText", null);
                _wasInContext = false;
                _contextEnterTime = DateTime.MinValue;
                _lastActiveIndex = -1;
            }

            _lastImage?.Dispose();
            _lastImage = null;
            _penultimateImage?.Dispose();
            _penultimateImage = null;
            return;
        }

        if (!_wasInContext)
        {
            // 进入场景时同步队伍信息并检测队伍变化
            _contextEnterTime = now;
            _lastSyncTime = DateTime.MinValue;
            _wasInContext = true;
            _isSyncingTeam = true;
            
            Task.Run(async () =>
            {
                // 确保画面加载完成，提高识别成功率
                await Task.Delay(500);
                var delaySinceLastPressIndex = (DateTime.Now - _lastPressIndexTime).TotalSeconds;
                if (delaySinceLastPressIndex < 1.1)
                {
                    // 刚按过换人键，人物头像还在读秒，此时yolo识别可能会失败
                    await Task.Delay(TimeSpan.FromSeconds(1.1 - delaySinceLastPressIndex));
                }
                    
                CombatScenes? scenes = null;
                try 
                {
                    scenes = RunnerContext.Instance.TrySyncCombatScenesSilent();
                    if (scenes != null && scenes.CheckTeamInitialized())
                    {
                        var avatars = scenes.GetAvatars();
                        
                        if (avatars.Count >= 1)
                        {
                            var newTeamNames = avatars.Select(a => a.Name).ToArray();
                            
                            // 检测队伍配置是否变化
                            bool teamChanged = false;
                            for (int i = 0; i < 4; i++)
                            {
                                string newName = i < newTeamNames.Length ? newTeamNames[i] : string.Empty;
                                if (_lastTeamAvatarNames[i] != newName)
                                {
                                    teamChanged = true;
                                    break;
                                }
                            }
                            
                            lock (_stateLock)
                            {
                                if (teamChanged)
                                {
                                    bool wasFullTeam = _lastTeamAvatarNames.All(n => !string.IsNullOrEmpty(n));
                                    bool isNowFullTeam = newTeamNames.Length == 4;
                                    bool isFullTeam = wasFullTeam && isNowFullTeam;
                                    if (isFullTeam)
                                    {
                                        _logger.LogInformation("[SkillCD] 队伍配置变化: {OldTeam} -> {NewTeam}",
                                            string.Join(",", _lastTeamAvatarNames),
                                            string.Join(",", newTeamNames));
                                    }
                                    
                                    for (int i = 0; i < 4; i++)
                                    {
                                        _cds[i] = 0;
                                        _lastSetTime[i] = DateTime.MinValue;
                                    }
                                    _lastActiveIndex = -1;
                                }
                                
                                SyncAvatarInfo(avatars.ToList());
                                
                                for (int i = 0; i < 4; i++)
                                {
                                    _lastTeamAvatarNames[i] = i < newTeamNames.Length ? newTeamNames[i] : string.Empty;
                                }
                            }
                        }
                        else
                        {
                            lock (_stateLock)
                            {
                                // 同步失败/无人时清空UI，但保留数据
                                for (int i = 0; i < 4; i++)
                                {
                                    _teamAvatarNames[i] = string.Empty;
                                    _teamIndexRects[i] = default;
                                }
                            }
                        }
                    }
                    else
                    {
                        lock (_stateLock)
                        {
                            for (int i = 0; i < 4; i++)
                            {
                                _teamAvatarNames[i] = string.Empty;
                                _teamIndexRects[i] = default;
                            }
                        }
                    }
                }
                finally
                {
                    scenes?.Dispose();
                    lock (_stateLock)
                    {
                        _isSyncingTeam = false; // 无论成功失败，同步结束，允许渲染
                    }
                }
            });
        }

        // 场景切入缓冲期，等待UI稳定
        if ((now - _contextEnterTime).TotalSeconds < 0.5)
        {
            return;
        }

        // 监听元素战技 (E) 键物理输入
        var elementalSkillKey = (int)TaskContext.Instance()
            .Config.KeyBindingsConfig.ElementalSkill.ToVK();

        short eKeyState = User32.GetAsyncKeyState(elementalSkillKey);
        bool isEDown = (eKeyState & 0x8000) != 0;
        if (isEDown && !_prevEKey) _lastEKeyPress = now;
        _prevEKey = isEDown;

        // 监听换人操作 (数字键 1-4)
        int pressedIndex = -1;
        for (int i = 0; i < 4; i++)
        {
            short keyState = User32.GetAsyncKeyState((int)(User32.VK.VK_1 + (byte)i));
            bool isDown = (keyState & 0x8000) != 0;
            if (isDown && !_prevKeys[i]) pressedIndex = i;
            _prevKeys[i] = isDown;
            _lastPressIndexTime = DateTime.Now;
        }

        if (_lastImage != null)
        {
            if (pressedIndex != -1)
            {
                ImageRegion frameToUse = _penultimateImage ?? _lastImage;
                if (frameToUse != null)
                {
                    HandleActionTrigger(frameToUse, pressedIndex);
                }
            }

            if (_prevEKey && TaskContext.Instance().Config.SkillCdConfig.TriggerOnSkillUse)
            {
                ImageRegion frameToUse = _penultimateImage ?? _lastImage;
                if (frameToUse != null)
                {
                    HandleActionTrigger(frameToUse, pressedIndex);
                }
            }
        }

        // 更新帧缓存队列
        _penultimateImage?.Dispose();
        _penultimateImage = _lastImage; // 把上一帧移到倒数第二帧
        
        // 记录当前帧为上一帧（深拷贝，避免current用完会被dispose）
        _lastImage = new ImageRegion(
            content.CaptureRectArea.SrcMat.Clone(),
            content.CaptureRectArea.X,
            content.CaptureRectArea.Y
        );

        UpdateOverlay();
    }

    /// <summary>
    /// 同步角色基础数据
    /// </summary>
    private void SyncAvatarInfo(List<Avatar> avatars)
    {
        for (int i = 0; i < 4; i++)
        {
            if (i < avatars.Count)
            {
                _teamAvatarNames[i] = avatars[i].Name;
                _teamIndexRects[i] = avatars[i].IndexRect;
            }
            else
            {
                _teamAvatarNames[i] = string.Empty;
                _teamIndexRects[i] = default;
            }
        }
    }

    /// <summary>
    /// 处理按键切换角色时的CD记录
    /// </summary>
    private void HandleActionTrigger(ImageRegion frame, int pressedTarget)
    {
        int activeIdx = IdentifyActiveIndex(frame, new AvatarActiveCheckContext());
        if (activeIdx <= 0) return;

        int slot = activeIdx - 1;
        
        // 记录被切走角色的CD
        if (slot != pressedTarget)
        {
            double ocrVal = RecognizeSkillCd(frame);
            if (ocrVal > 0)
            {
                _cds[slot] = ocrVal;
                _lastSetTime[slot] = DateTime.Now;
                
                // 记录切人保护
                _lastSwitchFromSlot = slot;
                _lastSwitchTime = DateTime.Now;
            }
            else
            {
                // OCR识别失败，尝试兜底
                bool justUsedE = (DateTime.Now - _lastEKeyPress).TotalSeconds < 1.1;
                bool isVisualReady = Bv.IsSkillReady(frame, activeIdx, false);

                if (isVisualReady)
                {
                    if (justUsedE)
                    {
                        ApplyFallbackCd(slot);
                    }
                    else if (_cds[slot] > 0)
                    {
                         // 保留原CD
                    }
                    else
                    {
                        _cds[slot] = 0;
                    }
                }
                else
                {
                    if (justUsedE)
                    {
                        ApplyFallbackCd(slot);
                    }
                }
            }
        }
        
        // 更新当前激活角色索引（不清零CD，让计时器持续运行）
        _lastActiveIndex = pressedTarget + 1;
    }

    /// <summary>
    /// 检测当前激活角色并同步技能状态
    /// </summary>
    private void CheckAndSyncActiveStatus(ImageRegion frame)
    {
        int activeIdx = IdentifyActiveIndex(frame, _activeCheckContext);
        if (activeIdx > 0)
        {
            // int slot = activeIdx - 1;
            //
            // // 更新当前激活角色索引（切换角色不清零CD）
            // if (_lastActiveIndex != activeIdx)
            // {
            //     _lastActiveIndex = activeIdx;
            // }
            //
            // // 检测技能是否就绪，就绪则归零
            // // 额外保护：处于切人冷却期时不检测
            // bool isInSwitchProtect = (slot == _lastSwitchFromSlot) && (DateTime.Now - _lastSwitchTime).TotalSeconds < 1.0;
            //
            // if (activeIdx == slot + 1 && !isInSwitchProtect)
            // {
            //     bool isReady = Bv.IsSkillReady(frame, activeIdx, false);
            //     if (isReady)
            //     {
            //         // 默认逻辑：识别到技能就绪时，不清零当前计时
            //         // 防止因开大招全屏遮挡导致误判为Ready从而错误清零计数器
            //         // 让倒计时自然跑完
            //     }
            // }
            _lastActiveIndex = activeIdx;
        }
    }

    /// <summary>
    /// 获取自定义规则中的CD值
    /// 返回值：
    /// - double值：命中规则，应强制设定为该值
    /// - null：未命中规则，走默认逻辑
    /// </summary>
    private double? GetCustomCdRule(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;

        var config = ParseCustomCdConfig();
        if (config.TryGetValue(name, out var val))
        {
            // 如果用户只写了名字没写数值，尝试读默认配置
            if (!val.HasValue)
            {
                if (DefaultAutoFightConfig.CombatAvatarMap.TryGetValue(name, out var info))
                {
                    return info.SkillCd;
                }
                return 0; // 名字匹配但无默认配置，视为0
            }
            return val.Value;
        }
        return null;
    }

    /// <summary>
    /// 应用角色的冷却时间
    /// </summary>
    private void ApplyFallbackCd(int slot)
    {
        var name = _teamAvatarNames[slot];
        
        // 1. 优先自定义规则
        double? customRule = GetCustomCdRule(name);
        if (customRule.HasValue)
        {
            _cds[slot] = customRule.Value;
            _lastSetTime[slot] = DateTime.Now;
            return;
        }

        // 2. 默认兜底
        if (!string.IsNullOrEmpty(name) && DefaultAutoFightConfig.CombatAvatarMap.TryGetValue(name, out var info))
        {
            _cds[slot] = info.SkillCd;
            _lastSetTime[slot] = DateTime.Now;
        }
        else
        {
            _cds[slot] = 0;
        }
    }

    private Dictionary<string, double?> ParseCustomCdConfig()
    {
        var result = new Dictionary<string, double?>();
        var list = TaskContext.Instance().Config.SkillCdConfig.CustomCdList;
        
        if (list == null) return result;

        foreach (var item in list)
        {
            if (!string.IsNullOrWhiteSpace(item.RoleName))
            {
                if (!result.ContainsKey(item.RoleName))
                {
                    result[item.RoleName] = item.CdValue;
                }
            }
        }
        return result;
    }
    private int IdentifyActiveIndex(ImageRegion region, AvatarActiveCheckContext context)
    {
        var validRects = _teamIndexRects.Any(r => r != default)
            ? _teamIndexRects.Where(r => r != default).ToArray()
            : AutoFightAssets.Instance.AvatarIndexRectList.ToArray();

        return PartyAvatarSideIndexHelper.GetAvatarIndexIsActiveWithContext(region, validRects, context);
    }

    private double RecognizeSkillCd(ImageRegion image)
    {
        try
        {
            var eCdRect = AutoFightAssets.Instance.ECooldownRect;
            using var crop = image.DeriveCrop(eCdRect);
            var roi = crop.SrcMat;
            using var whiteMask = new Mat();
            Cv2.InRange(roi, new Scalar(230, 230, 230), new Scalar(255, 255, 255), whiteMask);
            var text = OcrFactory.Paddle.OcrWithoutDetector(whiteMask);
            if (string.IsNullOrWhiteSpace(text)) return 0;
            var match = Regex.Match(text, @"\d+(\.\d+)?");
            if (match.Success && double.TryParse(match.Value, out var val))
            {
                // 减去两帧的时间作为补偿
                int intervalMs = TaskContext.Instance().Config.TriggerInterval;
                double compensation = (intervalMs * 2) / 1000.0;
                val -= compensation;

                return (val > 0 && val < 60) ? val : 0;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[SkillCD] OCR识别CD失败");
        }
        return 0;
    }

    /// <summary>
    /// 更新 UI 层渲染
    /// </summary>
    private void UpdateOverlay()
    {
        var drawContent = VisionContext.Instance().DrawContent;
        var sideRects = AutoFightAssets.Instance.AvatarSideIconRectList;
        var config = TaskContext.Instance().Config.SkillCdConfig;
        
        if (sideRects == null || sideRects.Count < 4)
        {
            drawContent.PutOrRemoveTextList("SkillCdText", null);
            return;
        }

        var systemInfo = TaskContext.Instance().SystemInfo;
        double factor = (double)systemInfo.GameScreenSize.Width / systemInfo.ScaleMax1080PCaptureRect.Width;
        
        // 使用配置中的坐标（保留一位小数）
        double userPX = Math.Round(config.PX, 1);
        double userPY = Math.Round(config.PY, 1);
        double userGap = Math.Round(config.Gap, 1);

        double basePx = userPX * factor;
        double basePy = userPY * factor;
        double intervalY = userGap * factor;

        var textList = new List<TextDrawable>();
        
        if (_isSyncingTeam)
        {
            drawContent.PutOrRemoveTextList("SkillCdText", null);
            return;
        }

        // 检查是否有足够的角色信息（必须恰好4人）
        int validAvatarCount = _teamAvatarNames.Count(n => !string.IsNullOrEmpty(n));
        // _logger.LogDebug("[SkillCD] UpdateOverlay: 有效角色数量={Count}, Names={Names}", validAvatarCount, string.Join(",", _teamAvatarNames));
        
        if (validAvatarCount != 4)
        {
            // 不是4人，确保清空
            if (drawContent.TextList.ContainsKey("SkillCdText"))
            {
               drawContent.PutOrRemoveTextList("SkillCdText", null);
            }
            return;
        }
        
        for (int i = 0; i < 4; i++)
        {
            if (!string.IsNullOrEmpty(_teamAvatarNames[i]))
            {
                // 如果启用了"冷却为0时隐藏"，且CD为0，则跳过
                if (config.HideWhenZero && _cds[i] <= 0)
                {
                    continue;
                }

                var px = basePx;
                var py = basePy + intervalY * i;

                textList.Add(new TextDrawable(_cds[i].ToString("F1"), new Point(px, py)));
            }
        }

        if (textList.Count == 0) drawContent.PutOrRemoveTextList("SkillCdText", null);
        else drawContent.PutOrRemoveTextList("SkillCdText", textList);
    }
}
