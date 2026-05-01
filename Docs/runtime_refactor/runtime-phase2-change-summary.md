# 第二阶段改动说明

本文说明当前第二阶段代码修改的意义，重点回答三个问题：

1. 这批改动在解决什么问题
2. 每个修改文件分别承担了什么职责变化
3. 当前第二阶段已经做到哪里，后续还缺什么

## 背景

在重构前，截图器和运行器共享同一条 UI 启停链路。

这会导致两个直接问题：

1. 截图器重启时，会通过 UI Stop/Start 路径触发全局取消，打断正在运行的任务和脚本
2. 多个业务模块直接依赖全局截图入口，导致截图恢复逻辑无法集中治理

第二阶段的目标不是一次性完成全部解耦，而是先完成一条关键语义切换：

`截图器临时失效` 不再等价于 `整个运行时必须停止并重启`

换句话说，第二阶段要把截图故障从“系统级中断”降级成“截图服务内部恢复”。

## 本阶段完成的行为变化

第二阶段完成后，运行时语义变成：

1. 窗口尺寸变化时，优先由 `CaptureService` 内部重启截图器
2. 截图短时失败时，优先走取帧重试和恢复逻辑，而不是直接停止任务系统
3. 画中画、通知截图和部分任务代码不再直接依赖全局静态截图入口
4. 只有真正的会话失效，例如游戏进程退出，才继续触发整套运行时停止逻辑

这一步的意义是先稳住运行时生命周期边界，再继续推进更彻底的脚本级别解耦。

## 调用链变化

### 修改前

典型路径如下：

1. `TaskTriggerDispatcher` 检测到截图器失效或窗口尺寸变化
2. 触发 `UiTaskStopTickEvent` 和 `UiTaskStartTickEvent`
3. `HomePageViewModel` 进入 UI Stop/Start 路径
4. Stop 路径触发取消上下文和运行器停止
5. 任务、脚本、相关状态被整体打断

这条链路的问题在于：截图故障被升级成了全局运行时故障。

### 修改后

现在的典型路径如下：

1. `TaskTriggerDispatcher` 检测到截图器暂时不可用或窗口尺寸变化
2. 优先调用 `CaptureService.Restart()` 或 `CaptureService.CaptureWithRetry()`
3. 当前帧可以被跳过，但运行器本身继续存活
4. 旁路模块通过统一截图入口感知恢复窗口
5. 只有检测到游戏进程已经退出时，才触发 `UiTaskStopTickEvent`

这条新链路的意义是把“截图恢复”和“运行时停机”拆成两类不同级别的事件。

## 文件级说明

### 核心运行时文件

#### BetterGenshinImpact/GameTask/CaptureService.cs

这是第二阶段最关键的修改点。

它现在不再只是一个简单的截图器包装层，而是承担了以下职责：

1. 保存截图器启动上下文，例如窗口句柄和模式
2. 提供内部重启能力 `Restart()`
3. 提供统一取帧入口 `CaptureNoRetry()` 和 `CaptureWithRetry()`
4. 维护截图恢复状态，例如失败次数、恢复事件、永久失败事件
5. 暴露 `CaptureVersion`，为后续更严格的版本协调打基础

它的意义是把截图生命周期和截图恢复策略集中到同一个服务里，避免调用方各自实现恢复逻辑。

文件里额外加入的 `CaptureFactory`、`StartSettingsFactory` 和 `Clock` 不是新的业务能力，而是最小可测性入口。它们的存在是为了让第二阶段的核心契约可以被自动化测试验证，而不是为了改变线上行为。

#### BetterGenshinImpact/GameTask/TaskTriggerDispatcher.cs

这是第二阶段最重要的行为切换点。

它的修改意义主要有四个：

1. 删除 `UiTaskStartTickEvent`，不再让截图器恢复依赖 UI 层重新 Start 整个运行时
2. 截图取帧改走 `_captureService.CaptureWithRetry()`，把恢复逻辑收敛到 `CaptureService`
3. 窗口尺寸变化时改成内部重启截图器，而不是 UI Stop/Start
4. 仅在明确判定游戏进程退出时，才继续走 `UiTaskStopTickEvent`

这意味着 `TaskTriggerDispatcher` 从“截图器故障升级器”变成了“截图服务消费者”。

#### BetterGenshinImpact/ViewModel/Pages/HomePageViewModel.cs

这个文件的修改意义是把 UI 层从截图器恢复链路里移出来。

具体来说：

1. 不再订阅 `UiTaskStartTickEvent`
2. 不再负责在截图器异常时重新 Start 整个任务系统

这样做之后，UI 只保留真正的开始和停止入口，而不再参与截图器的内部恢复。边界更清晰，也避免了之前通过 UI Stop 路径触发取消令牌的问题。

### 统一截图入口的文件

#### BetterGenshinImpact/GameTask/Common/TaskControl.cs

这个文件的修改意义是统一公共截图入口。

以前很多逻辑直接通过传入的 `IGameCapture` 或全局截图器取帧。现在这些公共方法会优先走 `CaptureService`，让：

1. 重试策略统一
2. 恢复窗口的处理方式统一
3. 后续版本协调逻辑可以集中补到这里

它是把业务层从底层截图器直接耦合上逐步摘下来的关键过渡层。

#### BetterGenshinImpact/View/Windows/PictureInPictureWindow.xaml.cs

画中画窗口现在通过 `CaptureService.CaptureNoRetry()` 取帧。

修改意义是：

1. 画中画接受“恢复窗口期短暂无画面”
2. 但不再自己直接碰底层截图器
3. 也不再放大截图故障

这属于旁路能力容错，而不是主调度链路控制。

#### BetterGenshinImpact/Service/Notification/NotificationService.cs

通知截图改为使用统一截图入口。

它的意义是把“附带截图”从刚性依赖改成可退化能力。截图短暂失败时，通知链路可以少一张图，但不应该影响主流程。

#### BetterGenshinImpact/GameTask/Common/Job/LinneaMiningTask.cs

这是一个业务任务示例，意义在于把具体任务从 `GlobalGameCapture` 迁移到 `CaptureService` 提供的当前截图能力。

它说明第二阶段不只是改调度器本身，也开始处理业务代码里直接持有全局截图入口的问题。

### 可测试性与验证文件

#### BetterGenshinImpact/AssemblyInfo.cs

这里加入 `InternalsVisibleTo("BetterGenshinImpact.UnitTest")`，意义是让测试程序集可以访问第二阶段为测试开放的最小内部入口。

这样做避免了为了测试去扩大正式公开 API 面。

#### Test/BetterGenshinImpact.UnitTest/GameTaskTests/RuntimeTests/CaptureServiceTests.cs

这个文件把第二阶段最关键的运行时契约写成了自动化测试。

当前覆盖的点包括：

1. 启动与内部重启是否保留启动上下文
2. `CaptureVersion` 是否按预期递增
3. `CaptureUnavailable` 和 `CaptureRecovered` 是否按预期触发
4. 重试窗口内是否能自动恢复取帧
5. 连续失败是否会升级为 `PermanentFailure`
6. 恢复后失败窗口是否被正确重置
7. 停止后是否禁止继续内部重启
8. `Start`、`Capture`、`Stop`、`Dispose` 异常路径是否按预期降级处理
9. 零句柄启动保护是否生效

这个文件的意义不是增加功能，而是给第二阶段划出一条不能回退的行为底线。

#### Docs/runtime-phase2-validation.md

这个文档是第二阶段的验收清单。

它的意义有三个：

1. 把自动化测试和手动回归场景放到同一个验证口径里
2. 明确“第二阶段完成”的判断标准
3. 避免后续继续重构时只看是否能编译，而忽略运行时语义是否保持正确

## 当前第二阶段还没完成的部分

第二阶段虽然已经完成了关键语义切换，但还没有彻底结束。

当前仍有这些后续工作：

1. 继续收敛剩余的静态或旁路截图入口
2. 把 `CaptureVersion` 协调进一步推广到更多截图边界
3. 继续为脚本运行级别拆分做准备，让一部分 JS 在无截图条件下运行
4. 在有真实窗口和 UI 主线程环境的前提下，继续用实机回归验证关键生命周期场景

## 与总设计的关系

如果说 [Docs/runtime-refactor-design.md](Docs/runtime-refactor-design.md) 定义的是长期结构目标，那么本文描述的是第二阶段已经落地的中间状态。

它们之间的关系是：

1. 总设计定义最终边界
2. 第二阶段实现关键生命周期切换
3. 验证清单负责证明第二阶段没有只停留在代码结构层，而是真的改变了运行时行为

## 小结

第二阶段这批文件修改的本质，不是“把截图代码挪了几个地方”，而是完成了一个运行时语义上的降级处理：

1. 截图器异常从全局停机事件变成局部恢复事件
2. UI 层不再负责截图恢复
3. 公共截图入口开始统一到 `CaptureService`
4. 新行为已经有自动化测试和验证文档兜底

这一步做好之后，后续才有条件继续推进“脚本不依赖截图即可运行”的更深层解耦。