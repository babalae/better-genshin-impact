# StateMachine 状态机基类使用说明

## 概述

`StateMachineBase<TState, TContext>` 是一个通用的状态机基类，采用 **注册式状态处理器** 模式，子类只需注册状态处理逻辑，基类负责状态循环和调度。

## 设计理念

1. **闭环检测**：使用 `DetectCurrentState()` 检测当前状态，而非固定等待
2. **状态驱动**：根据检测到的状态执行对应处理器
3. **注册式处理**：子类通过 `RegisterStateHandler()` 注册处理器，无需 switch-case
4. **可扩展性**：支持未知状态处理器、上下文传递

## 快速开始

### 1. 定义状态枚举

```csharp
public enum MyState
{
    Unknown,
    StateA,
    StateB,
    StateC,
    Done
}
```

### 2. 创建状态机子类

```csharp
using BetterGenshinImpact.GameTask.Common.StateMachine;
using Microsoft.Extensions.Logging;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

public class MyTask : StateMachineBase<MyState, MyContext>
{
    public override string Name => "我的任务";
    
    protected override ILogger Logger => TaskControl.Logger;

    public MyTask()
    {
        RegisterStateHandlers();
    }

    protected override void RegisterStateHandlers()
    {
        RegisterStateHandler(MyState.StateA, HandleStateA);
        RegisterStateHandler(MyState.StateB, HandleStateB);
        RegisterStateHandler(MyState.StateC, HandleStateC);
        RegisterUnknownStateHandler(HandleUnknownState);
    }

    protected override MyState DetectCurrentState()
    {
        // 使用 OCR/模板匹配检测当前界面状态
        using var ra = CaptureToRectArea();
        // ... 检测逻辑
        return MyState.StateA;
    }

    private async Task<MyState?> HandleStateA(MyContext ctx, CancellationToken ct)
    {
        Logger.LogInformation("处理状态A");
        // 执行操作...
        return MyState.StateB; // 返回期望的下一状态
    }

    private async Task<MyState?> HandleStateB(MyContext ctx, CancellationToken ct)
    {
        // ...
        return null; // 返回 null 表示重新检测状态
    }

    private async Task<MyState?> HandleStateC(MyContext ctx, CancellationToken ct)
    {
        // ...
        return MyState.Done;
    }

    private async Task HandleUnknownState(CancellationToken ct)
    {
        Logger.LogWarning("未知状态，等待重试");
        await Delay(500, ct);
    }
}
```

### 3. 运行状态机

```csharp
var myTask = new MyTask();
var result = await myTask.RunStateMachineUntil(
    new MyContext(),           // 上下文
    state => state == MyState.Done,  // 终止条件
    TimeSpan.FromMinutes(5),   // 超时时间
    cancellationToken
);
```

## 核心 API

### 状态注册

```csharp
// 注册单个状态处理器
RegisterStateHandler(TState state, StateHandlerDelegate<TContext> handler);

// 批量注册状态处理器
RegisterStateHandlers((state1, handler1), (state2, handler2), ...);

// 注册未知状态处理器
RegisterUnknownStateHandler(Func<CancellationToken, Task> handler);
```

### 状态处理器签名

```csharp
// 处理器返回值说明：
// - 返回具体状态：作为期望状态进行等待验证
// - 返回 null：重新检测当前状态
delegate Task<TState?> StateHandlerDelegate<TContext>(TContext context, CancellationToken ct);
```

### 状态机执行

```csharp
// 运行状态机直到满足终止条件
Task<TState> RunStateMachineUntil(
    TContext context,
    Func<TState, bool> exitCondition,
    TimeSpan? timeout = null,
    CancellationToken ct = default
);
```

### 辅助方法

```csharp
// 点击并确保状态转换（闭环点击）
Task ClickAndEnsure(
    int x, int y,
    Func<bool> successCondition,
    int maxRetries = 10,
    int retryDelay = 500,
    int clickInterval = 300
);

// 等待状态转换
Task<bool> EnsureStateTransition(
    TState expectedState,
    int timeoutMs = 10000,
    int checkInterval = 500
);
```

## 最佳实践

### 1. 使用 BvPage 进行 UI 检测

```csharp
protected override MyState DetectCurrentState()
{
    using var ra = CaptureToRectArea();
    var page = new BvPage(ra);
    
    if (page.GetByText("确认").IsExist())
        return MyState.ConfirmDialog;
    
    if (page.GetByText("开始").IsExist())
        return MyState.Ready;
    
    return MyState.Unknown;
}
```

### 2. 闭环点击模式

```csharp
private async Task<MyState?> HandleReadyState(MyContext ctx, CancellationToken ct)
{
    var page = new BvPage(CaptureToRectArea());
    
    // 使用 ClickAndEnsure 确保点击成功
    await ClickAndEnsure(
        x: 500, y: 300,
        successCondition: () => {
            using var ra = CaptureToRectArea();
            return new BvPage(ra).GetByText("加载中").IsExist();
        },
        maxRetries: 5
    );
    
    return MyState.Loading;
}
```

### 3. 状态转换等待

```csharp
private async Task<MyState?> HandleLoadingState(MyContext ctx, CancellationToken ct)
{
    // 等待状态转换到 Ready，最多等待 30 秒
    var success = await EnsureStateTransition(MyState.Ready, timeoutMs: 30000);
    
    if (!success)
    {
        Logger.LogWarning("等待超时");
        return null; // 重新检测
    }
    
    return MyState.Ready;
}
```

### 4. 使用 NewRetry 进行重试

```csharp
private async Task<MyState?> HandleInputState(MyContext ctx, CancellationToken ct)
{
    var found = await NewRetry.WaitForAction(() =>
    {
        Simulation.SendInput.SimulateAction(GIActions.PickUpOrInteract);
        Sleep(300, ct);
        
        using var ra = CaptureToRectArea();
        return new BvPage(ra).GetByText("成功").IsExist();
    }, ct, maxRetries: 10, retryDelay: 500);
    
    return found ? MyState.Success : null;
}
```

## 项目约定

### Logger 使用

```csharp
// 正确：使用属性覆盖
protected override ILogger Logger => TaskControl.Logger;

// 错误：构造函数注入
// private readonly ILogger _logger;
```

### 按键模拟

```csharp
// 正确：使用 GIActions
Simulation.SendInput.SimulateAction(GIActions.PickUpOrInteract);
Simulation.SendInput.SimulateAction(GIActions.OpenPaimonMenu);

// 错误：直接使用 KeyPress
// Simulation.SendInput.Keyboard.KeyPress(VK.VK_F);
```

### 延时调用

```csharp
// 使用 TaskControl 的静态方法
using static BetterGenshinImpact.GameTask.Common.TaskControl;

await Delay(500, ct);  // 异步延时
Sleep(200, ct);        // 同步延时（在闭环中使用）
```

## 示例：AutoStygianOnslaughtTask

参考 [AutoStygianOnslaughtTask.cs](../../AutoStygianOnslaught/AutoStygianOnslaughtTask.cs) 了解完整的状态机实现示例。

该任务实现了：
- 18 种状态的检测和处理
- 使用 BvPage 进行 OCR 检测
- 闭环点击和状态等待
- 战斗状态机嵌套
- 奖励领取流程

## 架构图

```
┌─────────────────────────────────────────────────────────────┐
│                    StateMachineBase                          │
├─────────────────────────────────────────────────────────────┤
│  ┌─────────────────────────────────────────────────────┐    │
│  │ RunStateMachineUntil()                              │    │
│  │   ↓                                                 │    │
│  │   while (!exitCondition)                            │    │
│  │     ↓                                               │    │
│  │     DetectCurrentState() → 子类实现                  │    │
│  │     ↓                                               │    │
│  │     _stateHandlers[state]() → 子类注册的处理器        │    │
│  │     ↓                                               │    │
│  │     返回期望状态 → EnsureStateTransition()           │    │
│  └─────────────────────────────────────────────────────┘    │
├─────────────────────────────────────────────────────────────┤
│  辅助方法：                                                  │
│  - ClickAndEnsure()       闭环点击                          │
│  - EnsureStateTransition() 状态转换等待                      │
│  - CaptureToRectArea()    截图                              │
└─────────────────────────────────────────────────────────────┘
                              ↑
                              │ 继承
┌─────────────────────────────────────────────────────────────┐
│                    子类 (如 AutoStygianOnslaughtTask)         │
├─────────────────────────────────────────────────────────────┤
│  - 定义状态枚举                                              │
│  - 实现 DetectCurrentState()                                │
│  - 注册状态处理器                                            │
│  - 实现各状态的处理逻辑                                       │
└─────────────────────────────────────────────────────────────┘
```
