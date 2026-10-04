using System;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Recognition.OCR;
using BetterGenshinImpact.Core.Recognition.OpenCv;
using BetterGenshinImpact.Core.Recorder;
using BetterGenshinImpact.Core.Script;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.AutoArtifactSalvage;
using BetterGenshinImpact.GameTask.AutoFight;
using BetterGenshinImpact.GameTask.AutoFight.Assets;
using BetterGenshinImpact.GameTask.AutoPathing;
using BetterGenshinImpact.GameTask.AutoPathing.Handler;
using BetterGenshinImpact.GameTask.AutoTrackPath;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using BetterGenshinImpact.GameTask.Common.Job;
using BetterGenshinImpact.GameTask.Common.Map.Maps.Base;
using BetterGenshinImpact.GameTask.Macro;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.GameTask.QuickBuy;
using BetterGenshinImpact.GameTask.QuickClaimReward;
using BetterGenshinImpact.GameTask.QuickSereniteaPot;
using BetterGenshinImpact.GameTask.QuickTeleport.Assets;
using BetterGenshinImpact.GameTask.UseRedeemCode;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Helpers.Extensions;
using BetterGenshinImpact.Model;
using BetterGenshinImpact.Service;
using BetterGenshinImpact.Service.Instance;
using BetterGenshinImpact.Service.Interface;
using BetterGenshinImpact.Service.I18n;
using BetterGenshinImpact.View;
using BetterGenshinImpact.View.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Vanara.PInvoke;
using HotKeySettingModel = BetterGenshinImpact.Model.HotKeySettingModel;

namespace BetterGenshinImpact.ViewModel.Pages;

public partial class HotKeyPageViewModel : ObservableObject, IViewModel
{
    private readonly ILogger<HotKeyPageViewModel> _logger;
    private readonly TaskSettingsPageViewModel _taskSettingsPageViewModel;
    private readonly RecognitionTemplateEditorService _recognitionTemplateEditorService;
    private readonly TaskTriggerDispatcher _taskTriggerDispatcher;
    private readonly Dictionary<string, HotKeyBinding> _acceptedBindings = [];
    private readonly HashSet<string> _rollingBackHotKeyProperties = [];

    /// <summary>
    /// 因「替换重复快捷键」而被清除的绑定。若新快捷键随后注册失败，需要把它一并恢复。
    /// key 为发起替换的功能的 ConfigPropertyName。
    /// </summary>
    private readonly Dictionary<string, (HotKeySettingModel Model, HotKeyBinding Binding)> _replacedBindings = [];

    /// <summary>
    /// 正在执行「切换快捷键类型」。该流程会临时清空 HotKey，
    /// 属于中间状态，不能覆盖 _acceptedBindings 中记录的可用绑定。
    /// </summary>
    private bool _isSwitchingHotKeyType;

    public AllConfig Config { get; set; }

    /// <summary>
    /// 一次真正生效的绑定。注册失败时按它整体回滚，因此除了快捷键还要记住类型：
    /// 类型切换后原快捷键会被清空，只回滚快捷键会得到「单键被当成全局热键」这类错误结果。
    /// </summary>
    private readonly record struct HotKeyBinding(HotKey HotKey, HotKeyTypeEnum Type);

    [ObservableProperty]
    private ObservableCollection<HotKeySettingModel> _hotKeySettingModels = [];

    /// <summary>
    /// 网页版实例不响应任何热键（全局热键与键鼠监听都不注册）：
    /// 同一 Windows Session 中全局热键只能被先启动的实例注册，键鼠监听也会与 Primary 重复响应。
    /// 网页版实例没有主界面，一般不会创建本 ViewModel，这里是兜底
    /// </summary>
    public bool IsHotKeyEnabled { get; } = !InstanceBootstrap.Current.Context.IsWebView;

    public HotKeyPageViewModel(
        IConfigService configService,
        ILogger<HotKeyPageViewModel> logger,
        TaskSettingsPageViewModel taskSettingsPageViewModel,
        RecognitionTemplateEditorService recognitionTemplateEditorService,
        TaskTriggerDispatcher taskTriggerDispatcher)
    {
        _logger = logger;
        _taskSettingsPageViewModel = taskSettingsPageViewModel;
        _recognitionTemplateEditorService = recognitionTemplateEditorService;
        _taskTriggerDispatcher = taskTriggerDispatcher;
        // 获取配置
        Config = configService.Get();

        // 构建快捷键配置列表
        BuildHotKeySettingModelList();

        var list = GetAllNonDirectoryHotkey(HotKeySettingModels);

        // 配置文件中可能存在重复的快捷键（手工编辑、旧版本残留、导入他人配置等），
        // 先做一次去重并写回配置，避免每次启动都变成「谁先注册谁生效」的不确定行为
        DeduplicateHotKeyConfig(list);

        foreach (var hotKeyConfig in list)
        {
            _acceptedBindings[hotKeyConfig.ConfigPropertyName] = new HotKeyBinding(hotKeyConfig.HotKey, hotKeyConfig.HotKeyType);
            if (IsHotKeyEnabled && !hotKeyConfig.RegisterHotKey())
            {
                _logger.LogWarning("快捷键 {Function}({HotKey}) 注册失败：{Reason}",
                    hotKeyConfig.FunctionName, hotKeyConfig.HotKey, hotKeyConfig.LastRegisterError);
            }
            hotKeyConfig.PropertyChanged += (sender, e) =>
            {
                if (sender is HotKeySettingModel model)
                {
                    OnHotKeySettingChanged(model, e.PropertyName);
                }
            };
        }
    }

    /// <summary>
    /// 分发快捷键配置项的属性变化
    /// </summary>
    /// <param name="model">发生变化的配置项</param>
    /// <param name="propertyName">发生变化的属性名</param>
    private void OnHotKeySettingChanged(HotKeySettingModel model, string? propertyName)
    {
        // 只有快捷键和快捷键类型的变化需要重新注册。
        // 其它属性（如多语言刷新产生的 LocalizedFunctionName）不应触发注销/注册。
        switch (propertyName)
        {
            case nameof(HotKeySettingModel.HotKey):
                OnHotKeyChanged(model);
                break;
            case nameof(HotKeySettingModel.HotKeyType):
                OnHotKeyTypeChanged(model);
                break;
        }
    }

    /// <summary>
    /// 快捷键变化：写回配置、处理重复、重新注册
    /// </summary>
    private void OnHotKeyChanged(HotKeySettingModel model)
    {
        Debug.WriteLine($"{model.FunctionName} 快捷键变更为 {model.HotKey}");

        // 回滚流程：只写回配置并重新注册，不再触发冲突/重复检查，避免递归
        if (_rollingBackHotKeyProperties.Remove(model.ConfigPropertyName))
        {
            UpdateHotKeyConfig(model);
            ReRegisterHotKey(model, CurrentBinding(model), true);
            return;
        }

        var newHotKey = model.HotKey;
        var previousBinding = CurrentBinding(model);

        // 自动切换类型（例如键鼠监听录入组合键）时，当前类型才是用户想要的目标类型；
        // 回滚会把类型还原，因此必须单独记下来，否则确认替换后会按旧类型注册而失败
        var targetType = model.HotKeyType;

        var duplicate = FindDuplicateHotKey(model, newHotKey);
        if (duplicate != null)
        {
            // 先回滚当前设置，避免两个功能同时占用同一个快捷键（那会导致注册失败），
            // 再询问用户是否替换已有绑定
            RestoreBinding(model, previousBinding);
            ConfirmReplaceDuplicateHotKey(model, duplicate, newHotKey, previousBinding, targetType);
            return;
        }

        UpdateHotKeyConfig(model);

        if (!ReRegisterHotKey(model, previousBinding, false))
        {
            // 失败提示与回滚由 ReRegisterHotKey 处理，被替换掉的绑定会在那里一并恢复
            return;
        }

        // 注册成功后才提醒原神键位冲突。需要弹窗时，替换事务要等用户答复后才结束，
        // 因此不能在这里提交，否则用户取消时已经无法恢复被替换掉的绑定。
        if (!ShowGameKeyBindingConflictWarning(model, newHotKey, previousBinding))
        {
            CommitReplacedBinding(model.ConfigPropertyName);
        }
    }

    /// <summary>
    /// 快捷键类型变化：原快捷键在新类型下不再适用，先清空再重新注册
    /// </summary>
    private void OnHotKeyTypeChanged(HotKeySettingModel model)
    {
        Debug.WriteLine($"{model.FunctionName} 快捷键类型变更为 {model.HotKeyType.ToChineseName()}");

        var previousBinding = CurrentBinding(model);

        _isSwitchingHotKeyType = true;
        try
        {
            // 清空快捷键会再次触发 HotKey 变化，从而完成配置写回与注销
            model.HotKey = HotKey.None;

            var pi = Config.HotKeyConfig.GetType().GetProperty(model.ConfigPropertyName + "Type", BindingFlags.Public | BindingFlags.Instance);
            if (null != pi && pi.CanWrite)
            {
                pi.SetValue(Config.HotKeyConfig, model.HotKeyType.ToString(), null);
            }

            ReRegisterHotKey(model, previousBinding, false);
        }
        finally
        {
            _isSwitchingHotKeyType = false;
        }

        // 类型切换只是中间步骤：紧接着控件会设置新的快捷键。
        // 这里保留切换前的可用绑定，新快捷键注册失败时才能整体回滚回去。
        _acceptedBindings[model.ConfigPropertyName] = previousBinding;
    }

    /// <summary>
    /// 注销并重新注册快捷键。注册失败时提示用户，并回滚到上一次可用的绑定。
    /// </summary>
    /// <param name="model">要注册的功能</param>
    /// <param name="previousBinding">注册前该功能可用的绑定，失败时回滚到它</param>
    /// <param name="isRollback">本次注册是否已经是回滚动作；回滚再失败时不再继续回滚</param>
    /// <returns>注册成功（或本实例不注册热键）返回 true</returns>
    private bool ReRegisterHotKey(HotKeySettingModel model, HotKeyBinding previousBinding, bool isRollback)
    {
        model.UnRegisterHotKey();

        if (!IsHotKeyEnabled || model.RegisterHotKey())
        {
            if (!_isSwitchingHotKeyType)
            {
                _acceptedBindings[model.ConfigPropertyName] = new HotKeyBinding(model.HotKey, model.HotKeyType);
            }

            return true;
        }

        _logger.LogWarning("快捷键 {Function}({HotKey}) 注册失败：{Reason}",
            model.FunctionName, model.HotKey, model.LastRegisterError);

        var message = string.Format(
            I18nService.Instance.Translate("快捷键 {0} 注册失败：{1}"),
            model.HotKey,
            model.LastRegisterError ?? I18nService.Instance.Translate("未知原因"));

        var failedBinding = new HotKeyBinding(model.HotKey, model.HotKeyType);

        if (isRollback)
        {
            // 回滚后的快捷键依然无法注册，只提示，不再继续回滚
            _acceptedBindings[model.ConfigPropertyName] = failedBinding;
            ShowHotKeyRegisterError(model, failedBinding, message, null);
            return false;
        }

        ShowHotKeyRegisterError(
            model,
            failedBinding,
            message + "\n\n" + I18nService.Instance.Translate("已恢复为上一次的快捷键设置。"),
            () =>
            {
                // 先恢复当前功能以释放新热键，再让被替换掉的功能拿回原来的快捷键
                RestoreBinding(model, previousBinding);
                RestoreReplacedBinding(model.ConfigPropertyName);
            });

        return false;
    }

    /// <summary>
    /// 提示注册失败。对话框是延后弹出的，因此回滚前会先确认状态没有被再次修改。
    /// </summary>
    /// <param name="model">注册失败的功能</param>
    /// <param name="failedBinding">注册失败时的绑定，用于确认期间没有被改过</param>
    /// <param name="message">提示内容</param>
    /// <param name="rollback">用户确认后执行的回滚动作；状态已变化时不会执行</param>
    private void ShowHotKeyRegisterError(HotKeySettingModel model, HotKeyBinding failedBinding, string message, Action? rollback)
    {
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            ThemedMessageBox.Warning(
                message,
                I18nService.Instance.Translate("快捷键注册失败"),
                MessageBoxButton.OK,
                MessageBoxResult.OK);

            if (rollback == null)
            {
                return;
            }

            // 弹窗期间用户可能已经重新设置过，此时按旧状态回滚会覆盖更新的设置
            if (model.HotKey != failedBinding.HotKey || model.HotKeyType != failedBinding.Type)
            {
                return;
            }

            rollback();
        });
    }

    /// <summary>
    /// 该功能上一次真正生效的绑定
    /// </summary>
    private HotKeyBinding CurrentBinding(HotKeySettingModel model)
    {
        return _acceptedBindings.TryGetValue(model.ConfigPropertyName, out var binding)
            ? binding
            : new HotKeyBinding(model.HotKey, model.HotKeyType);
    }

    /// <summary>
    /// 把功能恢复为指定的绑定（快捷键 + 类型）
    /// </summary>
    private void RestoreBinding(HotKeySettingModel model, HotKeyBinding binding)
    {
        _acceptedBindings[model.ConfigPropertyName] = binding;

        // 恢复类型会清空 HotKey，因此必须在恢复 HotKey 之前执行
        if (model.HotKeyType != binding.Type)
        {
            model.HotKeyType = binding.Type;
        }

        if (model.HotKey == binding.HotKey)
        {
            return;
        }

        _rollingBackHotKeyProperties.Add(model.ConfigPropertyName);
        model.HotKey = binding.HotKey;
    }

    /// <summary>
    /// 恢复因替换重复快捷键而被清除的那个绑定
    /// </summary>
    /// <param name="configPropertyName">发起替换的功能</param>
    private void RestoreReplacedBinding(string configPropertyName)
    {
        if (!_replacedBindings.Remove(configPropertyName, out var replaced))
        {
            return;
        }

        RestoreBinding(replaced.Model, replaced.Binding);
    }

    /// <summary>
    /// 结束替换事务：确认替换生效，丢弃被替换绑定的备份。
    /// 只有到这里，被替换掉的那个功能才算真正失去它的快捷键。
    /// </summary>
    /// <param name="configPropertyName">发起替换的功能</param>
    private void CommitReplacedBinding(string configPropertyName)
    {
        _replacedBindings.Remove(configPropertyName);
    }

    /// <summary>
    /// 查找与该快捷键重复的其它功能
    /// </summary>
    private HotKeySettingModel? FindDuplicateHotKey(HotKeySettingModel current, HotKey hotKey)
    {
        if (hotKey.IsEmpty)
        {
            return null;
        }

        foreach (var hotKeySettingModel in GetAllNonDirectoryHotkey(HotKeySettingModels))
        {
            if (hotKeySettingModel.ConfigPropertyName != current.ConfigPropertyName && hotKeySettingModel.HotKey == hotKey)
            {
                return hotKeySettingModel;
            }
        }

        return null;
    }

    /// <summary>
    /// 询问用户是否用新快捷键替换已有的绑定
    /// </summary>
    /// <param name="model">发起替换的功能</param>
    /// <param name="duplicate">当前占用了该快捷键的功能</param>
    /// <param name="newHotKey">用户想要使用的快捷键</param>
    /// <param name="previousBinding">发起替换前该功能的绑定，用于确认期间没有被改过</param>
    /// <param name="targetType">用户录入新快捷键时的目标类型，确认后要按它注册</param>
    private void ConfirmReplaceDuplicateHotKey(HotKeySettingModel model, HotKeySettingModel duplicate, HotKey newHotKey, HotKeyBinding previousBinding, HotKeyTypeEnum targetType)
    {
        var message = string.Format(
                          I18nService.Instance.Translate("快捷键 {0} 已被「{1}」使用。"),
                          newHotKey,
                          duplicate.LocalizedFunctionName)
                      + "\n\n"
                      + I18nService.Instance.Translate("是否替换？替换后原功能将不再绑定该快捷键。");

        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            // 弹窗期间用户可能已经改了别的设置，此时按旧状态处理会清掉更新的绑定
            if (model.HotKey != previousBinding.HotKey
                || model.HotKeyType != previousBinding.Type
                || duplicate.HotKey != newHotKey)
            {
                return;
            }

            var result = ThemedMessageBox.Warning(
                message,
                I18nService.Instance.Translate("快捷键重复提醒"),
                MessageBoxButton.OKCancel,
                MessageBoxResult.OK);

            if (result != MessageBoxResult.OK)
            {
                // 当前设置已经回滚，无需额外处理
                return;
            }

            // 记录被替换掉的绑定：若新快捷键随后注册失败，需要把它一并恢复
            _replacedBindings[model.ConfigPropertyName] =
                (duplicate, new HotKeyBinding(duplicate.HotKey, duplicate.HotKeyType));

            // 清除旧绑定（会同步写回配置并注销）
            duplicate.HotKey = HotKey.None;

            // 恢复用户录入时的目标类型：上面的回滚把类型还原成了旧值，
            // 若直接设置快捷键，组合键会按「键鼠监听」注册而失败
            if (model.HotKeyType != targetType)
            {
                model.HotKeyType = targetType;
            }

            if (model.HotKey != newHotKey)
            {
                model.HotKey = newHotKey;
            }
        });
    }

    /// <summary>
    /// 清除配置中重复的快捷键（保留列表中靠前的那个）并写回配置
    /// </summary>
    private void DeduplicateHotKeyConfig(List<HotKeySettingModel> list)
    {
        var used = new Dictionary<HotKey, HotKeySettingModel>();
        foreach (var model in list)
        {
            if (model.HotKey.IsEmpty)
            {
                continue;
            }

            if (used.TryGetValue(model.HotKey, out var owner))
            {
                _logger.LogWarning("检测到重复的快捷键 {HotKey}：「{Owner}」与「{Duplicate}」，已清除后者",
                    model.HotKey, owner.FunctionName, model.FunctionName);
                model.HotKey = HotKey.None;
                UpdateHotKeyConfig(model);
                continue;
            }

            used[model.HotKey] = model;
        }
    }

    /// <summary>
    /// 快捷键与原神键位冲突时提示用户；用户取消则连同被替换掉的绑定一起回滚
    /// </summary>
    /// <param name="model">发生冲突的功能</param>
    /// <param name="newHotKey">新设置的快捷键</param>
    /// <param name="previousBinding">设置前的绑定，用户取消时回滚到它</param>
    /// <returns>需要弹窗询问时返回 true，此时由弹窗回调决定事务是提交还是回滚</returns>
    private bool ShowGameKeyBindingConflictWarning(HotKeySettingModel model, HotKey newHotKey, HotKeyBinding previousBinding)
    {
        if (!TryConvertToGameKeyId(model.HotKey, out var hotKeyId))
        {
            return false;
        }

        var conflictLines = GetGameKeyBindingConflictLines(hotKeyId);
        if (conflictLines.Count == 0)
        {
            return false;
        }

        var message = string.Format(
                          I18nService.Instance.Translate("{0}使用{1}键会与原神键位冲突："),
                          model.LocalizedFunctionName,
                          hotKeyId.ToName())
                      + "\n\n"
                      + string.Join("\n", conflictLines)
                      + "\n\n"
                      + I18nService.Instance.Translate("会导致游戏内动作和 BetterGI 功能同时触发，建议更换为其他按键。是否继续使用？");
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            if (model.HotKey != newHotKey)
            {
                // 状态已经变化，本次替换事务不再成立，直接提交（丢弃被替换绑定的备份）
                CommitReplacedBinding(model.ConfigPropertyName);
                return;
            }

            var result = ThemedMessageBox.Warning(
                message,
                I18nService.Instance.Translate("快捷键冲突提醒"),
                MessageBoxButton.OKCancel,
                MessageBoxResult.Cancel);
            if (result == MessageBoxResult.Cancel && model.HotKey == newHotKey)
            {
                // 整个编辑被撤销，因此被替换掉的功能也要拿回它原来的快捷键。
                // 必须先恢复当前功能以释放新热键，否则被替换的功能会因该键仍被占用而注册失败。
                RestoreBinding(model, previousBinding);
                RestoreReplacedBinding(model.ConfigPropertyName);
                return;
            }

            CommitReplacedBinding(model.ConfigPropertyName);
        });

        return true;
    }

    /// <summary>
    /// 把快捷键转换为原神键位枚举，仅支持无修饰键的单键与鼠标侧键
    /// </summary>
    /// <param name="hotKey">待转换的快捷键</param>
    /// <param name="keyId">转换结果</param>
    /// <returns>能够对应到原神键位时返回 true</returns>
    private static bool TryConvertToGameKeyId(HotKey hotKey, out KeyId keyId)
    {
        keyId = KeyId.Unknown;

        if (hotKey.IsEmpty)
        {
            return false;
        }

        if (hotKey.MouseButton is MouseButton.XButton1 or MouseButton.XButton2)
        {
            keyId = KeyIdConverter.FromMouseButton(hotKey.MouseButton);
            return keyId is not KeyId.Unknown and not KeyId.None;
        }

        if (hotKey.Modifiers != ModifierKeys.None || hotKey.Key == Key.None)
        {
            return false;
        }

        keyId = KeyIdConverter.FromInputKey(hotKey.Key);
        return keyId is not KeyId.Unknown and not KeyId.None;
    }

    /// <summary>
    /// 列出与原神键位冲突的功能
    /// </summary>
    /// <param name="hotKeyId">与 BetterGI 快捷键相同的原神键位</param>
    /// <returns>冲突项的描述文本</returns>
    private List<string> GetGameKeyBindingConflictLines(KeyId hotKeyId)
    {
        var lines = new List<string>();
        foreach (var pi in typeof(KeyBindingsConfig).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (pi.PropertyType != typeof(KeyId))
            {
                continue;
            }

            if (pi.GetValue(Config.KeyBindingsConfig) is KeyId gameKeyId && gameKeyId == hotKeyId)
            {
                lines.Add($"- {GetGameKeyBindingActionName(pi.Name)}（{gameKeyId.ToName()}）");
            }
        }

        return lines;
    }

    private static string GetGameKeyBindingActionName(string propertyName)
    {
        return propertyName switch
        {
            nameof(KeyBindingsConfig.MoveForward) => I18nService.Instance.Translate("向前移动"),
            nameof(KeyBindingsConfig.MoveBackward) => I18nService.Instance.Translate("向后移动"),
            nameof(KeyBindingsConfig.MoveLeft) => I18nService.Instance.Translate("向左移动"),
            nameof(KeyBindingsConfig.MoveRight) => I18nService.Instance.Translate("向右移动"),
            nameof(KeyBindingsConfig.SwitchToWalkOrRun) => I18nService.Instance.Translate("切换走/跑；特定操作模式下向下移动"),
            nameof(KeyBindingsConfig.NormalAttack) => I18nService.Instance.Translate("普通攻击"),
            nameof(KeyBindingsConfig.ElementalSkill) => I18nService.Instance.Translate("元素战技"),
            nameof(KeyBindingsConfig.ElementalBurst) => I18nService.Instance.Translate("元素爆发"),
            nameof(KeyBindingsConfig.SprintKeyboard) => I18nService.Instance.Translate("冲刺（键盘）"),
            nameof(KeyBindingsConfig.SprintMouse) => I18nService.Instance.Translate("冲刺（鼠标）"),
            nameof(KeyBindingsConfig.SwitchAimingMode) => I18nService.Instance.Translate("切换瞄准模式"),
            nameof(KeyBindingsConfig.Jump) => I18nService.Instance.Translate("跳跃；特定操作模式下向上移动"),
            nameof(KeyBindingsConfig.Drop) => I18nService.Instance.Translate("落下"),
            nameof(KeyBindingsConfig.PickUpOrInteract) => I18nService.Instance.Translate("拾取/交互（自动拾取由AutoPick模块管理）"),
            nameof(KeyBindingsConfig.QuickUseGadget) => I18nService.Instance.Translate("快捷使用小道具"),
            nameof(KeyBindingsConfig.InteractionInSomeMode) => I18nService.Instance.Translate("特定玩法内交互操作"),
            nameof(KeyBindingsConfig.QuestNavigation) => I18nService.Instance.Translate("开启任务追踪"),
            nameof(KeyBindingsConfig.AbandonChallenge) => I18nService.Instance.Translate("中断挑战"),
            nameof(KeyBindingsConfig.SwitchMember1) => I18nService.Instance.Translate("切换小队角色1"),
            nameof(KeyBindingsConfig.SwitchMember2) => I18nService.Instance.Translate("切换小队角色2"),
            nameof(KeyBindingsConfig.SwitchMember3) => I18nService.Instance.Translate("切换小队角色3"),
            nameof(KeyBindingsConfig.SwitchMember4) => I18nService.Instance.Translate("切换小队角色4"),
            nameof(KeyBindingsConfig.SwitchMember5) => I18nService.Instance.Translate("切换小队角色5"),
            nameof(KeyBindingsConfig.ShortcutWheel) => I18nService.Instance.Translate("呼出快捷轮盘"),
            nameof(KeyBindingsConfig.OpenInventory) => I18nService.Instance.Translate("打开背包"),
            nameof(KeyBindingsConfig.OpenCharacterScreen) => I18nService.Instance.Translate("打开角色界面"),
            nameof(KeyBindingsConfig.OpenMap) => I18nService.Instance.Translate("打开地图"),
            nameof(KeyBindingsConfig.OpenPaimonMenu) => I18nService.Instance.Translate("打开派蒙界面"),
            nameof(KeyBindingsConfig.OpenAdventurerHandbook) => I18nService.Instance.Translate("打开冒险之证界面"),
            nameof(KeyBindingsConfig.OpenCoOpScreen) => I18nService.Instance.Translate("打开多人游戏界面"),
            nameof(KeyBindingsConfig.OpenWishScreen) => I18nService.Instance.Translate("打开祈愿界面"),
            nameof(KeyBindingsConfig.OpenBattlePassScreen) => I18nService.Instance.Translate("打开纪行界面"),
            nameof(KeyBindingsConfig.OpenTheEventsMenu) => I18nService.Instance.Translate("打开活动面板"),
            nameof(KeyBindingsConfig.OpenTheSettingsMenu) => I18nService.Instance.Translate("打开玩法系统界面（尘歌壶内猫尾酒馆内）"),
            nameof(KeyBindingsConfig.OpenTheFurnishingScreen) => I18nService.Instance.Translate("打开摆设界面（尘歌壶内）"),
            nameof(KeyBindingsConfig.OpenStellarReunion) => I18nService.Instance.Translate("打开星之归还（条件符合期间生效）"),
            nameof(KeyBindingsConfig.OpenQuestMenu) => I18nService.Instance.Translate("开关任务菜单"),
            nameof(KeyBindingsConfig.OpenNotificationDetails) => I18nService.Instance.Translate("打开通知详情"),
            nameof(KeyBindingsConfig.OpenChatScreen) => I18nService.Instance.Translate("打开聊天界面"),
            nameof(KeyBindingsConfig.OpenSpecialEnvironmentInformation) => I18nService.Instance.Translate("打开特殊环境说明"),
            nameof(KeyBindingsConfig.CheckTutorialDetails) => I18nService.Instance.Translate("查看教程详情"),
            nameof(KeyBindingsConfig.ElementalSight) => I18nService.Instance.Translate("长按打开元素视野"),
            nameof(KeyBindingsConfig.ShowCursor) => I18nService.Instance.Translate("呼出鼠标"),
            nameof(KeyBindingsConfig.OpenPartySetupScreen) => I18nService.Instance.Translate("打开队伍配置界面"),
            nameof(KeyBindingsConfig.OpenFriendsScreen) => I18nService.Instance.Translate("打开好友界面"),
            nameof(KeyBindingsConfig.HideUI) => I18nService.Instance.Translate("隐藏主界面"),
            _ => propertyName
        };
    }

    /// <summary>
    /// 把界面上的快捷键写回配置对象
    /// </summary>
    /// <param name="model">发生变化的配置项</param>
    private void UpdateHotKeyConfig(HotKeySettingModel model)
    {
        var pi = Config.HotKeyConfig.GetType().GetProperty(model.ConfigPropertyName, BindingFlags.Public | BindingFlags.Instance);
        if (null != pi && pi.CanWrite)
        {
            pi.SetValue(Config.HotKeyConfig, model.HotKey.IsEmpty ? "" : model.HotKey.ToString(), null);
        }
    }

    public static List<HotKeySettingModel> GetAllNonDirectoryHotkey(IEnumerable<HotKeySettingModel> modelList)
    {
        var list = new List<HotKeySettingModel>();
        foreach (var hotKeySettingModel in modelList)
        {
            if (!hotKeySettingModel.IsDirectory)
            {
                list.Add(hotKeySettingModel);
            }

            list.AddRange(GetAllNonDirectoryChildren(hotKeySettingModel));
        }

        return list;
    }

    public static List<HotKeySettingModel> GetAllNonDirectoryChildren(HotKeySettingModel model)
    {
        var result = new List<HotKeySettingModel>();

        if (model.Children.Count == 0)
        {
            return result;
        }

        foreach (var child in model.Children)
        {
            if (!child.IsDirectory)
            {
                result.Add(child);
            }

            // 递归调用以获取子节点中的非目录对象
            result.AddRange(GetAllNonDirectoryChildren(child));
        }

        return result;
    }

    /// <summary>
    /// 按功能分组构建快捷键配置列表
    /// </summary>
    private void BuildHotKeySettingModelList()
    {
        // 一级目录/快捷键
        var bgiEnabledHotKeySettingModel = new HotKeySettingModel(
            "启动停止 BetterGI",
            nameof(Config.HotKeyConfig.BgiEnabledHotkey),
            Config.HotKeyConfig.BgiEnabledHotkey,
            Config.HotKeyConfig.BgiEnabledHotkeyType,
            (_, _) => { WeakReferenceMessenger.Default.Send(new PropertyChangedMessage<object>(this, "SwitchTriggerStatus", "", "")); }
        );
        HotKeySettingModels.Add(bgiEnabledHotKeySettingModel);

        var systemDirectory = new HotKeySettingModel(
            "系统控制"
        );
        HotKeySettingModels.Add(systemDirectory);

        var timerDirectory = new HotKeySettingModel(
            "实时任务"
        );
        HotKeySettingModels.Add(timerDirectory);

        var soloTaskDirectory = new HotKeySettingModel(
            "独立任务"
        );
        HotKeySettingModels.Add(soloTaskDirectory);

        var macroDirectory = new HotKeySettingModel(
            "操控辅助"
        );
        HotKeySettingModels.Add(macroDirectory);

        var devDirectory = new HotKeySettingModel(
            "开发者"
        );
        HotKeySettingModels.Add(devDirectory);

        // 二级快捷键
        systemDirectory.Children.Add(new HotKeySettingModel(
            "停止当前脚本/独立任务",
            nameof(Config.HotKeyConfig.CancelTaskHotkey),
            Config.HotKeyConfig.CancelTaskHotkey,
            Config.HotKeyConfig.CancelTaskHotkeyType,
            (_, _) =>
            {
                _logger.LogInformation("检测到您配置的停止快捷键{Key}按下，停止当前执行任务", Config.HotKeyConfig.CancelTaskHotkey);
                CancellationContext.Instance.ManualCancel();
            }
        ));
        systemDirectory.Children.Add(new HotKeySettingModel(
            "暂停当前脚本/独立任务",
            nameof(Config.HotKeyConfig.SuspendHotkey),
            Config.HotKeyConfig.SuspendHotkey,
            Config.HotKeyConfig.SuspendHotkeyType,
            (_, _) => { RunnerContext.Instance.IsSuspend = !RunnerContext.Instance.IsSuspend; }
        ));
        var takeScreenshotHotKeySettingModel = new HotKeySettingModel(
            "游戏截图",
            nameof(Config.HotKeyConfig.TakeScreenshotHotkey),
            Config.HotKeyConfig.TakeScreenshotHotkey,
            Config.HotKeyConfig.TakeScreenshotHotkeyType,
            (_, _) => { _taskTriggerDispatcher.TakeScreenshot(); }
        );
        systemDirectory.Children.Add(takeScreenshotHotKeySettingModel);

        systemDirectory.Children.Add(new HotKeySettingModel(
            "日志与状态窗口展示开关",
            nameof(Config.HotKeyConfig.LogBoxDisplayHotkey),
            Config.HotKeyConfig.LogBoxDisplayHotkey,
            Config.HotKeyConfig.LogBoxDisplayHotkeyType,
            (_, _) =>
            {
                TaskContext.Instance().Config.MaskWindowConfig.ShowLogBox = !TaskContext.Instance().Config.MaskWindowConfig.ShowLogBox;
                // 与状态窗口同步
                TaskContext.Instance().Config.MaskWindowConfig.ShowStatus = TaskContext.Instance().Config.MaskWindowConfig.ShowLogBox;
            }
        ));

        systemDirectory.Children.Add(new HotKeySettingModel(
            "遮罩指标栏展示开关",
            nameof(Config.HotKeyConfig.OverlayMetricsDisplayHotkey),
            Config.HotKeyConfig.OverlayMetricsDisplayHotkey,
            Config.HotKeyConfig.OverlayMetricsDisplayHotkeyType,
            (_, _) =>
            {
                TaskContext.Instance().Config.MaskWindowConfig.ShowOverlayMetrics = !TaskContext.Instance().Config.MaskWindowConfig.ShowOverlayMetrics;
            }
        ));

        var autoPickEnabledHotKeySettingModel = new HotKeySettingModel(
            "自动拾取开关",
            nameof(Config.HotKeyConfig.AutoPickEnabledHotkey),
            Config.HotKeyConfig.AutoPickEnabledHotkey,
            Config.HotKeyConfig.AutoPickEnabledHotkeyType,
            (_, _) =>
            {
                TaskContext.Instance().Config.AutoPickConfig.Enabled = !TaskContext.Instance().Config.AutoPickConfig.Enabled;
                _logger.LogInformation("切换{Name}状态为[{Enabled}]", "自动拾取", ToChinese(TaskContext.Instance().Config.AutoPickConfig.Enabled));
            }
        );
        timerDirectory.Children.Add(autoPickEnabledHotKeySettingModel);

        var autoSkipEnabledHotKeySettingModel = new HotKeySettingModel(
            "自动剧情开关",
            nameof(Config.HotKeyConfig.AutoSkipEnabledHotkey),
            Config.HotKeyConfig.AutoSkipEnabledHotkey,
            Config.HotKeyConfig.AutoSkipEnabledHotkeyType,
            (_, _) =>
            {
                TaskContext.Instance().Config.AutoSkipConfig.Enabled = !TaskContext.Instance().Config.AutoSkipConfig.Enabled;
                _logger.LogInformation("切换{Name}状态为[{Enabled}]", "自动剧情", ToChinese(TaskContext.Instance().Config.AutoSkipConfig.Enabled));
            }
        );
        timerDirectory.Children.Add(autoSkipEnabledHotKeySettingModel);

        timerDirectory.Children.Add(new HotKeySettingModel(
            "自动邀约开关",
            nameof(Config.HotKeyConfig.AutoSkipHangoutEnabledHotkey),
            Config.HotKeyConfig.AutoSkipHangoutEnabledHotkey,
            Config.HotKeyConfig.AutoSkipHangoutEnabledHotkeyType,
            (_, _) =>
            {
                TaskContext.Instance().Config.AutoSkipConfig.AutoHangoutEventEnabled = !TaskContext.Instance().Config.AutoSkipConfig.AutoHangoutEventEnabled;
                _logger.LogInformation("切换{Name}状态为[{Enabled}]", "自动邀约", ToChinese(TaskContext.Instance().Config.AutoSkipConfig.AutoHangoutEventEnabled));
            }
        ));

        var autoFishingEnabledHotKeySettingModel = new HotKeySettingModel(
            "自动钓鱼开关",
            nameof(Config.HotKeyConfig.AutoFishingEnabledHotkey),
            Config.HotKeyConfig.AutoFishingEnabledHotkey,
            Config.HotKeyConfig.AutoFishingEnabledHotkeyType,
            (_, _) =>
            {
                TaskContext.Instance().Config.AutoFishingConfig.Enabled = !TaskContext.Instance().Config.AutoFishingConfig.Enabled;
                _logger.LogInformation("切换{Name}状态为[{Enabled}]", "自动钓鱼", ToChinese(TaskContext.Instance().Config.AutoFishingConfig.Enabled));
            }
        );
        timerDirectory.Children.Add(autoFishingEnabledHotKeySettingModel);

        var quickTeleportEnabledHotKeySettingModel = new HotKeySettingModel(
            "快速传送开关",
            nameof(Config.HotKeyConfig.QuickTeleportEnabledHotkey),
            Config.HotKeyConfig.QuickTeleportEnabledHotkey,
            Config.HotKeyConfig.QuickTeleportEnabledHotkeyType,
            (_, _) =>
            {
                TaskContext.Instance().Config.QuickTeleportConfig.Enabled = !TaskContext.Instance().Config.QuickTeleportConfig.Enabled;
                _logger.LogInformation("切换{Name}状态为[{Enabled}]", "快速传送", ToChinese(TaskContext.Instance().Config.QuickTeleportConfig.Enabled));
            }
        );
        timerDirectory.Children.Add(quickTeleportEnabledHotKeySettingModel);
        
        var skillCdEnabledHotKeySettingModel = new HotKeySettingModel(
            "冷却提示开关",
            nameof(Config.HotKeyConfig.SkillCdEnabledHotkey),
            Config.HotKeyConfig.SkillCdEnabledHotkey,
            Config.HotKeyConfig.SkillCdEnabledHotkeyType,
            (_, _) =>
            {
                TaskContext.Instance().Config.SkillCdConfig.Enabled = !TaskContext.Instance().Config.SkillCdConfig.Enabled;
                _logger.LogInformation("切换{Name}状态为[{Enabled}]", "冷却提示", ToChinese(TaskContext.Instance().Config.SkillCdConfig.Enabled));
            }
        );
        timerDirectory.Children.Add(skillCdEnabledHotKeySettingModel);

        var quickTeleportTickHotKeySettingModel = new HotKeySettingModel(
            "手动触发快速传送触发快捷键（按住起效）",
            nameof(Config.HotKeyConfig.QuickTeleportTickHotkey),
            Config.HotKeyConfig.QuickTeleportTickHotkey,
            Config.HotKeyConfig.QuickTeleportTickHotkeyType,
            (_, _) => { Thread.Sleep(100); },
            true
        );
        timerDirectory.Children.Add(quickTeleportTickHotKeySettingModel);

        var mapMaskEnabledHotKeySettingModel = new HotKeySettingModel(
            "地图遮罩开关",
            nameof(Config.HotKeyConfig.MapMaskEnabledHotkey),
            Config.HotKeyConfig.MapMaskEnabledHotkey,
            Config.HotKeyConfig.MapMaskEnabledHotkeyType,
            (_, _) =>
            {
                TaskContext.Instance().Config.MapMaskConfig.Enabled = !TaskContext.Instance().Config.MapMaskConfig.Enabled;
                _logger.LogInformation("切换{Name}状态为[{Enabled}]", "地图遮罩", ToChinese(TaskContext.Instance().Config.MapMaskConfig.Enabled));
            }
        );
        timerDirectory.Children.Add(mapMaskEnabledHotKeySettingModel);

        var turnAroundHotKeySettingModel = new HotKeySettingModel(
            "长按旋转视角 - 那维莱特转圈",
            nameof(Config.HotKeyConfig.TurnAroundHotkey),
            Config.HotKeyConfig.TurnAroundHotkey,
            Config.HotKeyConfig.TurnAroundHotkeyType,
            (_, _) => { TurnAroundMacro.Done(); },
            true
        );
        macroDirectory.Children.Add(turnAroundHotKeySettingModel);

        var enhanceArtifactHotKeySettingModel = new HotKeySettingModel(
            "按下快速强化圣遗物",
            nameof(Config.HotKeyConfig.EnhanceArtifactHotkey),
            Config.HotKeyConfig.EnhanceArtifactHotkey,
            Config.HotKeyConfig.EnhanceArtifactHotkeyType,
            (_, _) => { QuickEnhanceArtifactMacro.Done(); },
            true
        );
        macroDirectory.Children.Add(enhanceArtifactHotKeySettingModel);

        macroDirectory.Children.Add(new HotKeySettingModel(
            "按下快速购买商店物品",
            nameof(Config.HotKeyConfig.QuickBuyHotkey),
            Config.HotKeyConfig.QuickBuyHotkey,
            Config.HotKeyConfig.QuickBuyHotkeyType,
            (_, _) => { QuickBuyTask.Done(); },
            true
        ));

        macroDirectory.Children.Add(new HotKeySettingModel(
            "一键领取奖励",
            nameof(Config.HotKeyConfig.OneKeyClaimRewardHotkey),
            Config.HotKeyConfig.OneKeyClaimRewardHotkey,
            Config.HotKeyConfig.OneKeyClaimRewardHotkeyType,
            null,
            true)
        {
            OnKeyDownAction = (_, _) => { OneKeyClaimRewardTask.Instance.KeyDown(); },
            OnKeyUpAction = (_, _) => { OneKeyClaimRewardTask.Instance.KeyUp(); }
        });

        macroDirectory.Children.Add(new HotKeySettingModel(
            "按下快速进出尘歌壶",
            nameof(Config.HotKeyConfig.QuickSereniteaPotHotkey),
            Config.HotKeyConfig.QuickSereniteaPotHotkey,
            Config.HotKeyConfig.QuickSereniteaPotHotkeyType,
            (_, _) => { QuickSereniteaPotTask.Done(); }
        ));

        soloTaskDirectory.Children.Add(new HotKeySettingModel(
            "启动/停止一条龙",
            nameof(Config.HotKeyConfig.OnedragonHotkey),
            Config.HotKeyConfig.OnedragonHotkey,
            Config.HotKeyConfig.OnedragonHotkeyType,
            (_, _) => { SwitchSoloTask(_taskSettingsPageViewModel.SOneDragonFlowCommand); }
        ));

        soloTaskDirectory.Children.Add(new HotKeySettingModel(
            "启动/停止自动七圣召唤",
            nameof(Config.HotKeyConfig.AutoGeniusInvokationHotkey),
            Config.HotKeyConfig.AutoGeniusInvokationHotkey,
            Config.HotKeyConfig.AutoGeniusInvokationHotkeyType,
            (_, _) => { SwitchSoloTask(_taskSettingsPageViewModel.SwitchAutoGeniusInvokationCommand); }
        ));

        soloTaskDirectory.Children.Add(new HotKeySettingModel(
            "启动/停止自动伐木",
            nameof(Config.HotKeyConfig.AutoWoodHotkey),
            Config.HotKeyConfig.AutoWoodHotkey,
            Config.HotKeyConfig.AutoWoodHotkeyType,
            (_, _) => { SwitchSoloTask(_taskSettingsPageViewModel.SwitchAutoWoodCommand); }
        ));

        soloTaskDirectory.Children.Add(new HotKeySettingModel(
            "启动/停止自动战斗",
            nameof(Config.HotKeyConfig.AutoFightHotkey),
            Config.HotKeyConfig.AutoFightHotkey,
            Config.HotKeyConfig.AutoFightHotkeyType,
            (_, _) => { SwitchSoloTask(_taskSettingsPageViewModel.SwitchAutoFightCommand); }
        ));

        soloTaskDirectory.Children.Add(new HotKeySettingModel(
            "启动/停止自动秘境",
            nameof(Config.HotKeyConfig.AutoDomainHotkey),
            Config.HotKeyConfig.AutoDomainHotkey,
            Config.HotKeyConfig.AutoDomainHotkeyType,
            (_, _) => { SwitchSoloTask(_taskSettingsPageViewModel.SwitchAutoDomainCommand); }
        ));
        soloTaskDirectory.Children.Add(new HotKeySettingModel(
            "启动/停止自动音游",
            nameof(Config.HotKeyConfig.AutoMusicGameHotkey),
            Config.HotKeyConfig.AutoMusicGameHotkey,
            Config.HotKeyConfig.AutoMusicGameHotkeyType,
            (_, _) => { SwitchSoloTask(_taskSettingsPageViewModel.SwitchAutoMusicGameCommand); }
        ));
        soloTaskDirectory.Children.Add(new HotKeySettingModel(
            "启动/停止自动钓鱼",
            nameof(Config.HotKeyConfig.AutoFishingGameHotkey),
            Config.HotKeyConfig.AutoFishingGameHotkey,
            Config.HotKeyConfig.AutoFishingGameHotkeyType,
            (_, _) => { SwitchSoloTask(_taskSettingsPageViewModel.SwitchAutoFishingCommand); }
        ));
        soloTaskDirectory.Children.Add(new HotKeySettingModel(
            "启动/停止自动烹饪",
            nameof(Config.HotKeyConfig.AutoCookGameHotkey),
            Config.HotKeyConfig.AutoCookGameHotkey,
            Config.HotKeyConfig.AutoCookGameHotkeyType,
            (_, _) => { SwitchSoloTask(_taskSettingsPageViewModel.SwitchAutoCookCommand); }
        ));

        macroDirectory.Children.Add(new HotKeySettingModel(
            "快捷点击原神内确认按钮",
            nameof(Config.HotKeyConfig.ClickGenshinConfirmButtonHotkey),
            Config.HotKeyConfig.ClickGenshinConfirmButtonHotkey,
            Config.HotKeyConfig.ClickGenshinConfirmButtonHotkeyType,
            (_, _) =>
            {
                using var capture = TaskControl.CaptureToRectArea();
                if (Bv.ClickConfirmButton(capture))
                {
                    TaskControl.Logger.LogInformation("触发快捷点击原神内{Btn}按钮：成功", "确认");
                }
                else
                {
                    TaskControl.Logger.LogInformation("触发快捷点击原神内{Btn}按钮：未找到按钮图片", "确认");
                }
            },
            true
        ));

        macroDirectory.Children.Add(new HotKeySettingModel(
            "快捷点击原神内取消按钮",
            nameof(Config.HotKeyConfig.ClickGenshinCancelButtonHotkey),
            Config.HotKeyConfig.ClickGenshinCancelButtonHotkey,
            Config.HotKeyConfig.ClickGenshinCancelButtonHotkeyType,
            (_, _) =>
            {
                using var capture = TaskControl.CaptureToRectArea();
                if (Bv.ClickCancelButton(capture))
                {
                    TaskControl.Logger.LogInformation("触发快捷点击原神内{Btn}按钮：成功", "取消");
                }
                else
                {
                    TaskControl.Logger.LogInformation("触发快捷点击原神内{Btn}按钮：未找到按钮图片", "取消");
                }
            },
            true
        ));

        macroDirectory.Children.Add(new HotKeySettingModel(
            "一键战斗宏快捷键",
            nameof(Config.HotKeyConfig.OneKeyFightHotkey),
            Config.HotKeyConfig.OneKeyFightHotkey,
            Config.HotKeyConfig.OneKeyFightHotkeyType,
            null,
            true)
        {
            OnKeyDownAction = (_, _) => { OneKeyFightTask.Instance.KeyDown(); },
            OnKeyUpAction = (_, _) => { OneKeyFightTask.Instance.KeyUp(); }
        });

        devDirectory.Children.Add(new HotKeySettingModel(
            "启动/停止键鼠录制",
            nameof(Config.HotKeyConfig.KeyMouseMacroRecordHotkey),
            Config.HotKeyConfig.KeyMouseMacroRecordHotkey,
            Config.HotKeyConfig.KeyMouseMacroRecordHotkeyType, async (_, _) =>
            {
                var vm = App.GetService<KeyMouseRecordPageViewModel>();
                if (vm == null)
                {
                    _logger.LogError("无法找到 KeyMouseRecordPageViewModel 单例对象！");
                    return;
                }

                if (GlobalKeyMouseRecord.Instance.Status == KeyMouseRecorderStatus.Stop)
                {
                    Thread.Sleep(300); // 防止录进快捷键进去
                    await vm.OnStartRecord();
                }
                else
                {
                    vm.OnStopRecord();
                }
            }
        ));

        devDirectory.Children.Add(new HotKeySettingModel(
            "（开发）模板素材制作",
            nameof(Config.HotKeyConfig.RecognitionTemplateEditorHotkey),
            Config.HotKeyConfig.RecognitionTemplateEditorHotkey,
            Config.HotKeyConfig.RecognitionTemplateEditorHotkeyType,
            (_, _) => { _recognitionTemplateEditorService.OpenAsync().SafeForget(); }
        ));

        devDirectory.Children.Add(new HotKeySettingModel(
            "（开发）获取当前大地图中心点位置",
            nameof(Config.HotKeyConfig.RecBigMapPosHotkey),
            Config.HotKeyConfig.RecBigMapPosHotkey,
            Config.HotKeyConfig.RecBigMapPosHotkeyType,
            (_, _) =>
            {
                try
                {
                    var p = new TpTask(CancellationToken.None).GetPositionFromBigMap(MapTypes.Teyvat.ToString());
                    _logger.LogInformation("大地图位置：{Position}", p);
                }
                catch (Exception e)
                {
                    _logger.LogError(e.Message);
                }
            }
        ));

        var pathRecorder = PathRecorder.Instance;
        var pathRecording = false;

        devDirectory.Children.Add(new HotKeySettingModel(
            "启动/停止路径记录器",
            nameof(Config.HotKeyConfig.PathRecorderHotkey),
            Config.HotKeyConfig.PathRecorderHotkey,
            Config.HotKeyConfig.PathRecorderHotkeyType,
            (_, _) =>
            {
                if (pathRecording)
                {
                    pathRecorder.Save();
                }
                else
                {
                    Task.Run(() => { pathRecorder.Start(); });
                }

                pathRecording = !pathRecording;
            }
        ));

        devDirectory.Children.Add(new HotKeySettingModel(
            "添加路径点",
            nameof(Config.HotKeyConfig.AddWaypointHotkey),
            Config.HotKeyConfig.AddWaypointHotkey,
            Config.HotKeyConfig.AddWaypointHotkeyType,
            (_, _) =>
            {
                if (pathRecording)
                {
                    Task.Run(() => { pathRecorder.AddWaypoint(); });

                }
            }
        ));

        // DEBUG
        if (RuntimeHelper.IsDebug)
        {
            var debugDirectory = new HotKeySettingModel(
                "内部测试"
            );
            HotKeySettingModels.Add(debugDirectory);


            // HotKeySettingModels.Add(new HotKeySettingModel(
            //     "（测试）启动/停止自动追踪",
            //     nameof(Config.HotKeyConfig.AutoTrackHotkey),
            //     Config.HotKeyConfig.AutoTrackHotkey,
            //     Config.HotKeyConfig.AutoTrackHotkeyType,
            //     (_, _) =>
            //     {
            //         // _taskSettingsPageViewModel.OnSwitchAutoTrack();
            //     }
            // ));
            // HotKeySettingModels.Add(new HotKeySettingModel(
            //     "（测试）地图路线录制",
            //     nameof(Config.HotKeyConfig.MapPosRecordHotkey),
            //     Config.HotKeyConfig.MapPosRecordHotkey,
            //     Config.HotKeyConfig.MapPosRecordHotkeyType,
            //     (_, _) =>
            //     {
            //         PathPointRecorder.Instance.Switch();
            //     }));
            // HotKeySettingModels.Add(new HotKeySettingModel(
            //     "（测试）自动寻路",
            //     nameof(Config.HotKeyConfig.AutoTrackPathHotkey),
            //     Config.HotKeyConfig.AutoTrackPathHotkey,
            //     Config.HotKeyConfig.AutoTrackPathHotkeyType,
            //     (_, _) =>
            //     {
            //         // _taskSettingsPageViewModel.OnSwitchAutoTrackPath();
            //     }
            // ));
            debugDirectory.Children.Add(new HotKeySettingModel(
                "（测试）测试",
                nameof(Config.HotKeyConfig.Test1Hotkey),
                Config.HotKeyConfig.Test1Hotkey,
                Config.HotKeyConfig.Test1HotkeyType,
                (_, _) =>
                {
                    Task.Run(async () =>
                    {
                        try
                        {
                            await new TpTask(CancellationToken.None).Tp(7001.6416, -569.6846, nameof(MapTypes.Teyvat));
                        }
                        catch (Exception e)
                        {
                            _logger.LogError(e.Message);
                        }
                    });

                }
            ));
            debugDirectory.Children.Add(new HotKeySettingModel(
                "（测试）测试2",
                nameof(Config.HotKeyConfig.Test2Hotkey),
                Config.HotKeyConfig.Test2Hotkey,
                Config.HotKeyConfig.Test2HotkeyType,
                (_, _) =>
                {
                    var myTask = new ChooseFOptionTask();
                    Task.Run(async () => { await myTask.SingleSelectText("荒坠的圣迹", CancellationToken.None); });

                    // var pName = SystemControl.GetActiveProcessName();
                    // Debug.WriteLine($"当前处于前台的程序：{pName}，原神是否位于前台：{SystemControl.IsGenshinImpactActive()}");
                    // TaskControl.Logger.LogInformation($"当前处于前台的程序：{pName}");
                }
            ));

            debugDirectory.Children.Add(new HotKeySettingModel(
                "（测试）播放内存中的路径",
                nameof(Config.HotKeyConfig.ExecutePathHotkey),
                Config.HotKeyConfig.ExecutePathHotkey,
                Config.HotKeyConfig.ExecutePathHotkeyType,
                (_, _) =>
                {
                    // if (pathRecording)
                    // {
                    //     new TaskRunner(DispatcherTimerOperationEnum.UseCacheImageWithTrigger)
                    //        .FireAndForget(async () => await new PathExecutor(CancellationContext.Instance.Cts).Pathing(pathRecorder._pathingTask));
                    // }
                }
            ));
        }
    }

    private void SwitchSoloTask(IAsyncRelayCommand asyncRelayCommand)
    {
        if (asyncRelayCommand.IsRunning)
        {
            CancellationContext.Instance.Cancel();
        }
        else
        {
            asyncRelayCommand.Execute(null);
        }
    }

    private string ToChinese(bool enabled)
    {
        return enabled.ToChinese();
    }
}
