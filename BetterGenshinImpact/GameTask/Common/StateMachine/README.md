# StateMachineBase 状态机框架

## 概述

`StateMachineBase<TState, TContext>` 是一个通用的有限状态机基类，专为游戏自动化场景设计。

## 核心概念

| 概念 | 说明 |
|------|------|
| **State** | 游戏界面的抽象（如 MainWorld、EventMenu） |
| **Transition** | 状态之间的有向边，定义从一个状态可能到达的下一个状态 |
| **Handler** | 状态处理器，执行当前状态的操作（如点击按钮），返回 `StateHandlerResult` |
| **Detector** | 状态检测器，根据截图判断当前是否处于某个状态 |

## StateHandlerResult

Handler 必须返回 `StateHandlerResult`，状态机根据返回值决定下一步行为：

| 返回值 | 语义 | 状态机行为 | 使用场景 |
|--------|------|-----------|----------|
| `Success` | 操作成功 | 等待邻接状态转换 | 点击按钮后等待界面切换 |
| `Wait` | 可预期的等待 | 继续循环，重新检测 | 动画播放中、已到达目标状态 |
| `Retry` | 意外失败 | 重试计数+1，超限后异常 | 找不到按钮、OCR 识别失败 |
| `Fail` | 无法恢复 | 立即抛出异常 | 严重错误需要停止 |

## 使用步骤

### 1. 定义状态枚举

```csharp
public enum MyState
{
    Unknown,
    MainWorld,
    EventMenu,
    BattleArena
}
```

### 2. 继承 StateMachineBase

```csharp
public class MyTask : StateMachineBase<MyState, BvPage>
{
    protected override ILogger Logger => _logger;
    private readonly ILogger<MyTask> _logger;
}
```

### 3. 注册 Handlers

```csharp
RegisterStateHandlers(
    (MyState.MainWorld, HandleMainWorld),
    (MyState.EventMenu, HandleEventMenu)
);

RegisterUnknownStateHandler(HandleUnknown);
```

### 4. 注册 Detectors

```csharp
RegisterStateDetectors(
    (MyState.MainWorld, DetectMainWorld),
    (MyState.EventMenu, DetectEventMenu),
    (MyState.BattleArena, DetectBattleArena)
);
```

### 5. 注册状态转换

```csharp
RegisterStateTransitions(
    (MyState.MainWorld, [MyState.EventMenu]),
    (MyState.EventMenu, [MyState.BattleArena])
);
```

### 6. 实现 Handler

```csharp
private async Task<StateHandlerResult> HandleMainWorld(BvPage page)
{
    Simulation.SendInput.SimulateAction(GIActions.OpenTheEventsMenu);
    await Delay(500, _ct);
    return StateHandlerResult.Success; // 等待转换到 EventMenu
}

private async Task<StateHandlerResult> HandleEventMenu(BvPage page)
{
    var button = page.GetByText("开始").FindAll().FirstOrDefault();
    if (button == null)
    {
        return StateHandlerResult.Retry; // 找不到按钮，重试
    }
    
    button.Click();
    await Delay(300, _ct);
    return StateHandlerResult.Success;
}

private Task<StateHandlerResult> HandleBattleArena(BvPage page)
{
    // 已到达目标状态
    return Task.FromResult(StateHandlerResult.Wait);
}

private async Task<StateHandlerResult> HandleUnknown(BvPage page)
{
    await new ReturnMainUiTask().Start(_ct);
    return StateHandlerResult.Wait; // 尝试恢复后重新检测
}
```

### 7. 实现 Detector

```csharp
private bool DetectMainWorld(ImageRegion ra)
{
    return ra.Find(MainWorldAssets.Minimap).IsExist();
}

private bool DetectEventMenu(ImageRegion ra)
{
    return ra.FindMulti(RecognitionObject.Ocr(100, 50, 200, 50))
             .Any(t => t.Text.Contains("活动"));
}
```

### 8. 运行状态机

```csharp
public async Task Start(CancellationToken ct)
{
    Initialize(ct, MyState.MainWorld);
    
    using var page = Bv.Page();
    await RunStateMachineUntil(page, MyState.BattleArena);
    
    // 状态机退出后，已到达 BattleArena
    await ExecuteBattle();
}
```

## API 参考

### 核心方法

| 方法 | 说明 |
|------|------|
| `Initialize(ct, initialState)` | 初始化状态机 |
| `RunStateMachineUntil(context, targetStates)` | 运行直到达到目标状态 |
| `RunStateMachineUntil(context, targetState)` | 运行直到达到单个目标状态 |
| `EnsureNextStateTransition(timeout)` | 等待邻接状态转换（供特殊场景使用） |

### 注册方法

| 方法 | 说明 |
|------|------|
| `RegisterStateHandler(state, handler)` | 注册单个状态处理器 |
| `RegisterStateHandlers(...)` | 批量注册状态处理器 |
| `RegisterUnknownStateHandler(handler)` | 注册未知状态处理器 |
| `RegisterStateDetector(state, detector)` | 注册单个状态检测器 |
| `RegisterStateDetectors(...)` | 批量注册状态检测器 |
| `RegisterStateTransitions(...)` | 注册状态转换关系 |

### 可重写属性

| 属性 | 默认值 | 说明 |
|------|--------|------|
| `DefaultDetectionInterval` | 300ms | 状态检测间隔 |
| `DefaultTransitionTimeout` | 10000ms | 状态转换超时 |
| `DefaultMaxRetries` | 3 | 最大重试次数 |
| `StateMachineLoopInterval` | 200ms | 状态机循环间隔 |

## 状态机循环流程

```
┌─────────────────────────────────────────────────────┐
│                  RunStateMachineUntil               │
└─────────────────────────────────────────────────────┘
                         │
                         ▼
              ┌──────────────────────┐
              │  RefreshCurrentState │  ◄─────────────┐
              │  (检测当前状态)       │                │
              └──────────────────────┘                │
                         │                            │
                         ▼                            │
              ┌──────────────────────┐                │
              │ 是否到达目标状态？    │                │
              └──────────────────────┘                │
                    │         │                       │
                   Yes        No                      │
                    │         │                       │
                    ▼         ▼                       │
              ┌────────┐  ┌──────────────────────┐    │
              │  退出  │  │  执行 Handler         │    │
              └────────┘  └──────────────────────┘    │
                                   │                  │
                                   ▼                  │
                    ┌─────────────────────────────┐   │
                    │      Handler 返回值          │   │
                    └─────────────────────────────┘   │
                         │    │    │    │            │
          ┌──────────────┤    │    │    │            │
          │              │    │    │    │            │
          ▼              ▼    ▼    ▼    │            │
    ┌─────────┐    ┌─────┐ ┌───┐ ┌────┐│            │
    │ Success │    │Wait │ │Rty│ │Fail││            │
    └─────────┘    └─────┘ └───┘ └────┘│            │
          │              │    │    │   │            │
          ▼              │    │    ▼   │            │
    ┌───────────────┐    │    │  抛出  │            │
    │EnsureNextState│    │    │  异常  │            │
    │   Transition  │    │    │        │            │
    └───────────────┘    │    │        │            │
          │              │    ▼        │            │
          │              │ ┌────────┐  │            │
          │              │ │重试计数│  │            │
          │              │ │  +1    │  │            │
          │              │ └────────┘  │            │
          │              │    │        │            │
          └──────────────┴────┴────────┴────────────┘
```

## 性能优化

### 检测器顺序

注册检测器时按速度排序（先快后慢）：

1. **模板匹配**（~10ms）- 最快
2. **小范围 OCR**（~50ms）
3. **大范围 OCR**（~100-200ms）- 最慢

### 邻接状态顺序

`RegisterStateTransitions` 中候选状态的顺序影响检测优先级，更具体的状态应放在前面：

```csharp
// 正确：更具体的状态放前面
(TeleportMap, [DomainEntrance, MainWorld])

// 错误：通用状态会被优先检测
(TeleportMap, [MainWorld, DomainEntrance])
```

## 示例

完整示例见 `GameTask/AutoStygianOnslaught/AutoStygianOnslaughtTask.cs`

状态图：
```
MainWorld ─► EventMenu ─► StygianOnslaughtPage ─► TeleportMap
                                                      │
                                                      ▼
DomainLobby ◄── DifficultySelect ◄── DomainEntrance ◄─┘
    │
    ▼
BossSelect ─► BattleArena ─► BattleResult ─► DomainLobby
```
