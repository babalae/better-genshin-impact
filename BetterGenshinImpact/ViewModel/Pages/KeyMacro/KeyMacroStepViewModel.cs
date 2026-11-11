using BetterGenshinImpact.GameTask.KeyMacro.Script;
using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using Vanara.PInvoke;

namespace BetterGenshinImpact.ViewModel.Pages.KeyMacro;

/// <summary>
/// 步骤视图中的一行。修改任何内容都会触发 <see cref="Changed"/>，由所属宏写回脚本
/// </summary>
public partial class KeyMacroStepViewModel : ObservableObject
{
    /// <summary>
    /// 步骤类型选项
    /// </summary>
    public static IReadOnlyList<KeyMacroOption<KeyMacroCommandKind>> KindOptions { get; } =
    [
        new(KeyMacroCommandKind.KeyPress, "按键"),
        new(KeyMacroCommandKind.KeyDown, "按下键"),
        new(KeyMacroCommandKind.KeyUp, "松开键"),
        new(KeyMacroCommandKind.Click, "鼠标点击"),
        new(KeyMacroCommandKind.MouseDown, "鼠标按下"),
        new(KeyMacroCommandKind.MouseUp, "鼠标松开"),
        new(KeyMacroCommandKind.MoveBy, "相对移动"),
        new(KeyMacroCommandKind.MoveTo, "绝对移动"),
        new(KeyMacroCommandKind.Scroll, "滚轮"),
        new(KeyMacroCommandKind.Wait, "等待"),
        new(KeyMacroCommandKind.Comment, "注释")
    ];

    /// <summary>
    /// 鼠标键选项
    /// </summary>
    public static IReadOnlyList<KeyMacroOption<KeyMacroMouseButton>> ButtonOptions { get; } =
    [
        new(KeyMacroMouseButton.Left, KeyMacroKeys.GetButtonDisplayName(KeyMacroMouseButton.Left)),
        new(KeyMacroMouseButton.Right, KeyMacroKeys.GetButtonDisplayName(KeyMacroMouseButton.Right)),
        new(KeyMacroMouseButton.Middle, KeyMacroKeys.GetButtonDisplayName(KeyMacroMouseButton.Middle)),
        new(KeyMacroMouseButton.X1, KeyMacroKeys.GetButtonDisplayName(KeyMacroMouseButton.X1)),
        new(KeyMacroMouseButton.X2, KeyMacroKeys.GetButtonDisplayName(KeyMacroMouseButton.X2))
    ];

    /// <summary>
    /// 不影响脚本内容的属性，变化时不触发 <see cref="Changed"/>
    /// </summary>
    private static readonly HashSet<string> ViewOnlyProperties =
    [
        nameof(Index), nameof(IsKeyKind), nameof(IsButtonKind), nameof(IsClickKind), nameof(IsMoveKind),
        nameof(IsPositionVisible), nameof(IsScrollKind), nameof(IsWaitKind), nameof(IsComment), nameof(IsCommand)
    ];

    /// <summary>
    /// 步骤类型
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsKeyKind), nameof(IsButtonKind), nameof(IsClickKind), nameof(IsMoveKind),
        nameof(IsPositionVisible), nameof(IsScrollKind), nameof(IsWaitKind), nameof(IsComment), nameof(IsCommand))]
    private KeyMacroCommandKind _kind = KeyMacroCommandKind.KeyPress;

    /// <summary>
    /// 键盘指令的按键，规范名称（例如 e、lshift）
    /// </summary>
    [ObservableProperty]
    private string _keyName = "e";

    /// <summary>
    /// 鼠标指令的鼠标键
    /// </summary>
    [ObservableProperty]
    private KeyMacroMouseButton _button = KeyMacroMouseButton.Left;

    /// <summary>
    /// moveby 的横向位移
    /// </summary>
    [ObservableProperty]
    private int _x = 100;

    /// <summary>
    /// moveby 的纵向位移
    /// </summary>
    [ObservableProperty]
    private int _y;

    /// <summary>
    /// 鼠标点击是否指定坐标
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPositionVisible))]
    private bool _hasPosition;

    /// <summary>
    /// moveto 和带坐标点击的横坐标（1920×1080 游戏画面坐标），默认画面中心
    /// </summary>
    [ObservableProperty]
    private int _positionX = KeyMacroParser.GameWidth / 2;

    /// <summary>
    /// moveto 和带坐标点击的纵坐标（1920×1080 游戏画面坐标），默认画面中心
    /// </summary>
    [ObservableProperty]
    private int _positionY = KeyMacroParser.GameHeight / 2;

    /// <summary>
    /// scroll 的格数，正数向上
    /// </summary>
    [ObservableProperty]
    private int _amount = 1;

    /// <summary>
    /// wait 的时长（毫秒）
    /// </summary>
    [ObservableProperty]
    private int _waitMs = 100;

    /// <summary>
    /// 注释文字
    /// </summary>
    [ObservableProperty]
    private string _text = string.Empty;

    /// <summary>
    /// 指令的备注，写成行尾注释
    /// </summary>
    [ObservableProperty]
    private string _note = string.Empty;

    /// <summary>
    /// 显示序号，从 1 开始
    /// </summary>
    [ObservableProperty]
    private int _index;

    /// <summary>
    /// 脚本内容变化
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>
    /// 是否为键盘指令
    /// </summary>
    public bool IsKeyKind => Kind is KeyMacroCommandKind.KeyDown or KeyMacroCommandKind.KeyUp or KeyMacroCommandKind.KeyPress;

    /// <summary>
    /// 是否为鼠标键指令
    /// </summary>
    public bool IsButtonKind => Kind is KeyMacroCommandKind.MouseDown or KeyMacroCommandKind.MouseUp or KeyMacroCommandKind.Click;

    /// <summary>
    /// 是否为鼠标点击（可以指定坐标）
    /// </summary>
    public bool IsClickKind => Kind == KeyMacroCommandKind.Click;

    /// <summary>
    /// 是否为相对移动鼠标
    /// </summary>
    public bool IsMoveKind => Kind == KeyMacroCommandKind.MoveBy;

    /// <summary>
    /// 是否显示游戏画面坐标输入框：绝对移动，或指定了坐标的鼠标点击
    /// </summary>
    public bool IsPositionVisible => Kind == KeyMacroCommandKind.MoveTo || (IsClickKind && HasPosition);

    /// <summary>
    /// 是否为滚轮
    /// </summary>
    public bool IsScrollKind => Kind == KeyMacroCommandKind.Scroll;

    /// <summary>
    /// 是否为等待
    /// </summary>
    public bool IsWaitKind => Kind == KeyMacroCommandKind.Wait;

    /// <summary>
    /// 是否为注释
    /// </summary>
    public bool IsComment => Kind == KeyMacroCommandKind.Comment;

    /// <summary>
    /// 是否为指令（可以加备注）
    /// </summary>
    public bool IsCommand => !IsComment;

    /// <summary>
    /// 从解析结果创建
    /// </summary>
    public static KeyMacroStepViewModel FromStep(KeyMacroStep step)
    {
        // 新建的对象还没有订阅者，用属性赋值不会触发写回
        var vm = new KeyMacroStepViewModel
        {
            Kind = step.Kind,
            Text = step.Text,
            Note = step.Note ?? string.Empty
        };
        switch (step.Kind)
        {
            case KeyMacroCommandKind.KeyDown:
            case KeyMacroCommandKind.KeyUp:
            case KeyMacroCommandKind.KeyPress:
                vm.KeyName = KeyMacroKeys.GetName(step.Key);
                break;
            case KeyMacroCommandKind.MouseDown:
            case KeyMacroCommandKind.MouseUp:
                vm.Button = step.Button;
                break;
            case KeyMacroCommandKind.Click:
                vm.Button = step.Button;
                vm.HasPosition = step.HasPosition;
                if (step.HasPosition)
                {
                    vm.PositionX = step.X;
                    vm.PositionY = step.Y;
                }

                break;
            case KeyMacroCommandKind.MoveBy:
                vm.X = step.X;
                vm.Y = step.Y;
                break;
            case KeyMacroCommandKind.MoveTo:
                vm.PositionX = step.X;
                vm.PositionY = step.Y;
                break;
            case KeyMacroCommandKind.Scroll:
                vm.Amount = step.Amount;
                break;
            case KeyMacroCommandKind.Wait:
                vm.WaitMs = step.WaitMs;
                break;
        }

        return vm;
    }

    /// <summary>
    /// 新建指定类型的步骤，参数取默认值
    /// </summary>
    public static KeyMacroStepViewModel Create(KeyMacroCommandKind kind)
    {
        return new KeyMacroStepViewModel { Kind = kind };
    }

    /// <summary>
    /// 复制一份
    /// </summary>
    public KeyMacroStepViewModel Clone()
    {
        return FromStep(ToStep());
    }

    /// <summary>
    /// 转为解析模型，供写回脚本。参数超出范围时按可写回的值处理，避免写出无法解析的脚本
    /// </summary>
    public KeyMacroStep ToStep()
    {
        var key = KeyMacroKeys.TryParseKey(KeyName, out var vk) ? vk : User32.VK.VK_E;
        // 相对移动用位移；绝对移动和带坐标点击用游戏画面坐标，限制在 1920×1080 内
        var usePosition = Kind == KeyMacroCommandKind.MoveTo || (Kind == KeyMacroCommandKind.Click && HasPosition);
        return new KeyMacroStep
        {
            Kind = Kind,
            Key = key,
            Button = Button,
            X = usePosition ? Math.Clamp(PositionX, 0, KeyMacroParser.GameWidth) : X,
            Y = usePosition ? Math.Clamp(PositionY, 0, KeyMacroParser.GameHeight) : Y,
            HasPosition = Kind == KeyMacroCommandKind.Click && HasPosition,
            // 滚轮格数不能为 0
            Amount = Amount == 0 ? 1 : Amount,
            WaitMs = Math.Clamp(WaitMs, 0, (int)(KeyMacroParser.MaxWaitSeconds * 1000)),
            // 注释和备注不能换行，否则写回后会变成多行
            Text = Text.ReplaceLineEndings(" "),
            Note = IsCommand && !string.IsNullOrWhiteSpace(Note) ? Note.ReplaceLineEndings(" ") : null
        };
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName != null && !ViewOnlyProperties.Contains(e.PropertyName))
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
