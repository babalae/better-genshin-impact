# BetterGI 多实例命名管道协议

## 设计目标

协议不再使用 InstanceId，也不要求根实例保存“由谁启动了谁”的进程树。实例关系只由
当前 Windows 用户、Windows Session、进程用途和进程 ID 决定。

当前协议版本为 `v2`，规则如下：

- 每个 Windows 用户最多有一个根 BetterGI；
- 根 BetterGI 是该用户固定根管道的持有者，通常运行在主桌面 Session；
- 与根不在同一个 Session 的 BetterGI 是桌面分身 BetterGI；
- WebView 无论在哪个 Session 启动，都是根 BetterGI 的直接客户端；
- 桌面分身 BetterGI 只能查询和访问相同 Session 中的 WebView；
- 同一个 Session 只允许一个 BetterGI，WebView 则允许多个并通过进程 ID 精确寻址。

本协议描述**同一个 Windows 用户内部**的实例组：根、桌面分身与 WebView。跨 Windows 用户的
通信由独立管道的 Worker 模式承担，见下文「跨用户 Worker（`--headless`）」；根管道上不做任务
下发与状态回传。

## 管道和 Windows 用户隔离

每个 Windows 用户只有一个固定根管道：

```text
BetterGI.v2.user-<Windows用户SID>.root
```

命名管道名称在整台计算机上可见，而不是按 Windows Session 隔离。因此根 BetterGI、
桌面分身 BetterGI 和 WebView 即使运行在不同 Session，也能连接同一个固定端点。
管道 ACL 只允许当前 Windows 用户 SID 完全控制，并显式拒绝 `Network` SID，所以
不同 Windows 用户分别拥有自己的根 BetterGI，彼此不会注册到对方的实例组中。

协议内容不敏感，不使用密钥、令牌、InstanceId 或父实例 ID。根端从已连接的命名管道
句柄调用 Windows API 获取客户端真实进程 ID，再由进程 ID取得真实 Windows Session；
客户端提交的用途只用于区分 BetterGI 与 WebView，不能伪造进程或 Session 身份。

### 跨用户 Worker（`--headless`）

跨用户 Worker 是**同一台机器上不同 Windows 用户之间**的唯一通信方式：用户 A 的 GUI
（Controller）借此控制用户 B 下运行的 Worker，而任务始终在 B 自己的进程与会话中执行。

```text
用户 A 的 GUI（Controller）
  │ 跨用户命名管道：机器级名称 + 受限 DACL + 服务端 SID 校验
  ▼
用户 B 的 Worker（BetterGI.exe --headless）
  │
  ▼
现有 TaskRunner / GameRuntimeService / ScriptService
```

命名管道名称在整台计算机上可见（不按 Session 隔离），因此不需要 SYSTEM 服务、Broker 或
`CreateProcessAsUser`：受限 DACL 负责「谁能连」，内核给出的客户端身份负责「连的是谁」。

#### 管道与授权

- 管道名：`BetterGI.v2.cross-user.<Worker用户SID>.worker`，由 Worker 自己持有；Worker
  不参与本用户的根管道，两者互不影响；
- 启动参数：`--headless`（不创建主界面）、`--controller-sid <允许的Controller用户SID>`，
  未指定 `--controller-sid` 时只允许 Worker 自身用户连接；
- Worker 创建管道实例时写入**受保护 DACL**（`SetAccessRuleProtection(isProtected: true)`）：
  显式 `Allow` 仅包含 Worker 用户、`SYSTEM` 与配置的 Controller 用户，未列出的用户
  （含 `Everyone`、`Authenticated Users`）被隐式拒绝；同时显式 `Deny` `Network` SID，
  避免管道被远程 SMB 访问；
- 客户端身份不采信自报值：由内核通过 `ImpersonateNamedPipeClient` → `TokenUser` 取得，
  在 `connection.open` 时校验；`WorkerAuthorization.IsControllerAllowed` 对 SID 大小写不敏感；
- 只有通过 SID 校验的连接才会被登记为 Controller 并收到 Worker 的推送（见下）。

#### 无头实例的界面与行为

`--headless` 不创建主窗口，因此若干依赖窗口的既有路径需要显式处理：

| 事项 | 行为 |
| --- | --- |
| 进程生命周期 | 设 `ShutdownMode = OnExplicitShutdown`，否则 WPF 会在没有窗口时立即关闭进程 |
| 控制台 | 用 `ConsoleHelper.AllocateConsoleWindow` **新建**可见控制台窗口；从资源管理器或 VS 启动时父进程没有控制台，否则看不到任何输出 |
| 游戏内叠加层 | 仍然启动截图器与遮罩窗口——遮罩窗口是无头实例**唯一的界面** |
| `Application.MainWindow` | 遮罩窗口创建时若 `MainWindow` 为空则把它设为 `MainWindow`，使 Toast、对话框能通过 `Window.GetWindow` 取到 Owner |
| 对话框 | `ThemedMessageBox` 在无头模式下只记日志并直接返回默认结果，不弹窗 |
| 致命异常 | 无头模式不再弹「报告」窗口（没有用户可以关闭它，会永久阻塞进程退出），只落日志 |

启动顺序上有一处易错点：`App` 是 `beforefieldinit` 类型，`_host` 的静态初始化器
（才会触发 `UseInstanceIpc()` → `InstanceBootstrap.Initialize()`）要等首次触碰 `App`
静态成员才执行，因此 `App.OnStartup` 里判断无头必须读 `CommandLineOptions.Instance.Headless`，
而不能读 `InstanceBootstrap.Current`（此时仍为 `null`）。

#### 操作集合

Worker 管道上提供以下操作，**都不进根管道**：

| 操作 | 方向 | 用途 |
| --- | --- | --- |
| `worker.status` | Controller → Worker | 读取 Worker 状态（含截图器是否运行、当前任务） |
| `task.start` | Controller → Worker | 下发任务（见「任务下发」） |
| `task.stop` / `task.pause` / `task.resume` | Controller → Worker | 停止 / 暂停 / 继续任务 |
| `task.status` | Controller → Worker | 读取任务状态（与 `worker.status` 同一份快照） |
| `capture.start` / `capture.stop` | Controller → Worker | 启停 Worker 自己的截图器（运行环境 + 遮罩叠加层） |
| `game.exit` | Controller → Worker | 退出 Worker 侧的游戏：Worker 激活游戏窗口后映射 `Alt+F4` |
| `worker.logMode` | Controller → Worker | 下发日志显示位置与通知聚合间隔 |
| `worker.notice` | Worker → Controller | 单向：回传用户提示（原 Toast 文本） |
| `worker.log` | Worker → Controller | 单向：回传日志批次，仅在显示位置为 `LocalWindow` 时下发 |
| `worker.progress` | Worker → Controller | 单向：回传配置组执行进度、预计剩余时间与预计总剩余时间，每秒一次 |

`worker.notice`、`worker.log` 与 `worker.progress` 是**推送**（无响应），复用同一套帧协议与
`requestId` 机制；接收侧（Controller）在 `HandleRequestAsync` 中识别并转交给 `WorkerController`
的事件。

#### 提示与日志回传

Worker 侧由 `WorkerNoticeHub` 承担出站：只有已通过 SID 校验的连接会被登记，没有 Controller
连接时静默丢弃（调用方另有本地日志），实际发送在后台完成且不抛异常，断开的连接会被移除并
交由 `ConnectionClosed` 统一清理。

- **提示**：无头 Worker 没有稳定的窗口承载 `Toast`，而且即使显示出来也只有 Worker 所在用户
  能看到，因此 Worker 侧的 `Toast` 调用点全部经由 `Helpers/BetterGiToast.cs` 门面
  （通过 `GlobalUsing.cs` 里的 `global using Toast = ...` 别名，调用点无需改动）。
  无头模式下改为回传；有窗口时显示行为与改动前一致；**在没有可用窗口时只记日志，而不是抛
  `ArgumentNullException`**（这正是改动前 `TaskRunner.Init` 里 Toast 崩溃的原因）。
  Controller 侧由 `WorkerController.NoticeReceived` 暴露，`MainWindowViewModel` 切回 UI 线程
  后以 `Worker：<文本>` 的形式显示为 Toast。

#### 日志显示位置

「Worker 日志」指原本写入遮罩叠加层日志框的那些内容。Controller 在启动页选择它的显示位置：

| 选项 | 枚举值 | 执行侧 | 说明 |
| --- | --- | --- | --- |
| 不显示 | `None` | Worker | 什么都不输出 |
| 使用通知渠道 | `Notification` | Worker | 按聚合间隔合并成一条通知，走通知设置里启用的渠道 |
| 游戏内叠加层 | `GameOverlay` | Worker | 默认值：写 Worker 自己的遮罩日志框，与改动前完全一致 |
| 远程独立窗口 | `RemoteWindow` | Worker | Worker 侧弹出「Worker 日志」窗口 |
| 本地独立窗口 | `LocalWindow` | Controller | 回传后由控制侧弹出「Worker 日志（远程）」窗口 |

- 日志内容与遮罩日志框**逐字一致**——同一 `[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}`
  模板、同样从 `Information` 起，只是换了输出位置；
- 只对 Worker 生效：`WorkerLogRouter.ShouldWriteToGameOverlay` 对非 Worker 恒为 `true`，
  普通实例、桌面分身、网页版的遮罩日志框行为不受影响；
- 日志频率很高，两条高通量路径都做了聚合：通知渠道按 `worker.logMode` 携带的间隔
  （默认 5 秒，启动页可调）合并，超长时保留最新行并标注省略条数；回传控制端按 500 ms /
  50 行一批；窗口路径按 200 ms / 50 行刷新；
- 通知渠道需要在通知设置里勾选 `worker.log`（「Worker 日志」）事件；订阅列表非空但未勾选时，
  选择「使用通知渠道」的当下会给出提示；
- 显示位置同时落盘，Worker 启动时按自己的配置初始化，Controller 连接成功后立即补推一次，
  两边不会不一致；
- 独立窗口两侧复用同一个 View 与 ViewModel（Worker 侧置顶、控制侧普通窗口）；用户手动关闭后
  不再自动弹出，重新选择一次即可重开，历史日志保留。

#### 执行进度回传

配置组的执行进度（进度条 + 预计剩余时间 + 多配置组时的预计总剩余时间）在 Worker 侧计算，每秒通过
`worker.progress` 推给已授权的 Controller：`ScriptGroupProgressTracker` 的状态变化经 `WorkerNoticeHub`
出站，控制端收到后直接刷到同一个单例上，因此主窗口右上角与 Worker 叠加层显示的是同一份数据。
Worker 连接断开时控制端主动清空进度，避免面板停在最后一帧。详见
[配置组执行进度与预计剩余时间](script-group-progress.md)。

#### 任务下发

`task.start` 只传**类型 + 标识**，任务内容全部由 Worker 在自己的安装目录下解析，不过管道传
文件内容或运行参数：

| 类型 | 标识 | Controller 侧入口 |
| --- | --- | --- |
| `scriptGroup` | 配置组名 | 调度器「运行」当前配置组 |
| `scriptGroups` | 配置组名列表 + 循环标记 | 调度器「连续执行」 |
| `taskProgress` | 任务进度名 | 调度器「继续执行」 |
| `oneDragon` | 一条龙配置名 | 一条龙页「一键执行」 |
| `solo` | 独立任务标识（14 个内置任务） | 任务设置页各独立任务 |
| `scriptFolder` | JS 脚本文件夹名 | 脚本列表「执行」 |
| `pathingFile` | 地图追踪文件名 + 相对安装目录的目录 | 地图追踪「执行」 |
| `startGame` | 无 | 启动页「启动截图器」/ 主按钮 |

- 配置组、脚本、地图追踪按**同安装目录**直接读取，因此前提是两个用户使用同一份安装目录；
  否则 Worker 会因找不到对应文件而拒绝；
- 独立任务与一条龙由 Worker 用自己的配置构造，不需要把文件内容过管道；
- 名称与路径都做了安全校验（拒绝路径分隔符、上跳等），地图追踪还要求文件位于安装目录内；
- 除 `startGame` 外的类型都要求 Worker 的运行环境已初始化（`TaskContext.Instance().IsInitialized`），
  否则以「Worker 截图器尚未启动，请先下发 startGame 任务。」拒绝——Worker 侧
  `ScriptService.StartGameTask` 在截图器起不来时会一直等待主界面；
- 已有任务在执行时（`Starting` / `RunningTask` / `Stopping`）拒绝重复启动，不打断当前任务；
- Worker 侧复用 GUI 的 ViewModel 入口执行，不重写任务逻辑。

#### Controller 侧行为

连接 Worker 后，Controller 本机不再启动截图器，也不在本机执行任务：

- 启动页主按钮与启停快捷键改为控制 Worker 的截图器（`capture.start` / `capture.stop`）；
- 上表所有入口改为下发到 Worker；
- 尚未映射到 Worker 的本机入口由 `TaskRunner.RunCurrentAsync` 统一拒绝并提示，
  `GameRuntimeService.StartAsync` 与 `ScriptService.StartGameTask` 也会拦住本机启动，
  **绝不会误在本机执行**；
- 连接期间每 2 秒轮询一次 `worker.status` 刷新状态文本与主按钮（Worker 不主动推送状态）；
- Worker 断开（含进程退出）由 `WorkerController.Disconnected` 统一处理并复位界面。

#### 配置项

| 配置 | 生效侧 | 说明 |
| --- | --- | --- |
| `OtherConfig.LastWorkerUserSid` | Controller | 上次连接/输入的 Worker 用户 SID，启动页自动回填 |
| `OtherConfig.WorkerLogDisplayMode` | Worker | 日志显示位置，Worker 启动时按它初始化 |
| `OtherConfig.WorkerLogNotificationIntervalSeconds` | Worker | 通知聚合间隔（秒），默认 5，最小 1 |

`WorkerLogDisplayMode` 按整数序列化，**新增选项只能追加在末尾**，否则会改变已落盘数值的含义；
其取值顺序与启动页下拉框的选项顺序一一对应，由单元测试守护。

#### 已知限制

- 两个用户必须使用**同一份安装目录**（配置组、脚本、地图追踪都按安装目录解析）；
- 进度与状态以轮询为准，Worker 不推送状态，Controller 也不主动推送；
- Worker 侧日志窗口依赖 Worker 进程有可用的 UI 线程，创建失败时静默降级为文件日志。

## 启动参数

客户端用途仅使用：

```text
--instance <childSession|webview>
```

- `--instance childSession` 表示该进程只能作为桌面分身客户端，根暂时不存在时也不会
  自行成为根；
- `--instance webview` 表示该进程只能作为 WebView 客户端，必须同时用
  `--instance-name <实例名>` 指定实例名（云原神网页版，见 [GameRuntime 设计](game-runtime.md) 7.1）。
  实例名随 `connection.open` 提交，根把它写入该 WebView 的端点（`InstanceEndpoint.InstanceName`），
  只用于识别和展示；同名互斥由实例名互斥体保证，根不判重；
- 不带 `--instance` 的 BetterGI 会先竞争固定根管道。竞争成功即成为根；竞争失败则
  连接已有根，由根根据真实 Session 决定是转发重复启动还是注册为桌面分身。

无头 Worker 使用：

```text
--headless [--controller-sid <允许的Controller用户SID>]
```

`--headless` 表示该进程不创建主界面、只托管跨用户 Worker 管道；`--controller-sid` 指定
允许连接的 Controller 用户，省略时只允许 Worker 自身用户。这两个参数的用法与本地调试方式
见[开发者文档](../development/README.md)；从零把两个用户与本地 RDP 环境搭起来的分步操作见
[单机双用户配置指南](../guides/multi-user-setup.md)。

应用自身重启时额外传递：

```text
--restart-from-pid <旧进程ID>
```

新进程会等待旧进程退出，再竞争根管道或替换同 Session 中的旧客户端连接。该参数只用于
消除重启交接期间的竞态，不作为长期身份。

## 连接拓扑和访问规则

```text
某个 Windows 用户的根 BetterGI
├─ Session 2 的桌面分身 BetterGI（最多一个）
├─ Session 3 的桌面分身 BetterGI（最多一个）
├─ Session 1 的 WebView（可多个，以进程 ID 区分）
├─ Session 2 的 WebView（可多个，以进程 ID 区分）
└─ Session 3 的 WebView（可多个，以进程 ID 区分）
```

所有客户端都直接连接根管道，不在桌面分身下面建立二级管道。这样根重启后只需恢复一个
固定端点，客户端也只需重连一个固定名称。

上图的每个实例都属于**同一个 Windows 用户**。跨 Windows 用户的通信不在这个拓扑里：
无头 Worker 既不作为客户端接入根管道，也不接受根的管理，它只在
`BetterGI.v2.cross-user.<SID>.worker` 上被另一用户的 GUI 直连。

WebView 访问规则：

- 根 BetterGI 可以查询和访问全部 WebView；
- 桌面分身 BetterGI 只能查询与自己 Windows Session 相同的 WebView；
- 单播通过 WebView 进程 ID 精确定位；
- 组播由调用方先查询可见 WebView，再逐个发送，不在协议中维护易失的实例组。

## 连接建立和重复启动

客户端连接固定根管道后，首先发送 `connection.open`。根根据管道连接的真实进程 ID 和
Session 处理：

1. `webview`：按进程 ID 注册，可在断线后替换旧连接。
2. BetterGI 与根位于相同 Session：视为重复启动，把业务启动参数转发给根窗口，然后
   新进程退出。
3. BetterGI 与根位于不同 Session：注册为桌面分身。
4. 同一 Session 已有桌面分身：把业务启动参数转发给已有进程，然后新进程退出。
5. 旧连接已经失效，或 `--restart-from-pid` 指向该 Session 中被替换的进程：接受新连接。

## 断线、闪退和退出语义

桌面分身 BetterGI 与 WebView 在根管道断开后，每秒尝试重新连接固定根管道，且没有总
超时时间。因此根 BetterGI 闪退、被结束进程或隔很久后才重新启动，都不改变客户端的
发现地址；新根取得固定管道后，仍存活的客户端会自动重新注册。

正常关闭根 BetterGI 时，现有 `ChildSessionService.Dispose` 流程继续主动断开 RDP 并
注销桌面分身 Session。只有未执行正常释放流程的闪退场景，桌面分身才会留存并等待根
恢复。

连接断开即移除对应运行时记录，不把客户端清单持久化。根重启后的关系由仍存活的客户端
重新连接重建，避免把已经退出的 PID 当作在线实例。

## 帧格式

同一条连接同时承载 JSON 控制消息和二进制相对鼠标批次。整数使用小端序。

```text
uint32 payloadLength
byte   payloadType
byte[] payload
```

`payloadLength` 最大为 1 MiB。

| payloadType | 内容 |
| --- | --- |
| `1` | UTF-8 JSON 控制消息 |
| `2` | 相对鼠标二进制批次 |

JSON 信封的主要字段：

```json
{
  "version": 2,
  "requestId": "00000000-0000-0000-0000-000000000000",
  "operation": "ping",
  "success": true,
  "errorCode": null,
  "errorMessage": null,
  "data": {}
}
```

响应使用相同 `requestId`，并将 `operation` 设为 `response`。信封中不再传输
`sourceInstanceId`。

当前控制操作：

| 操作 | 用途 |
| --- | --- |
| `ping` | 检查连接并读取端点描述 |
| `connection.open` | 客户端声明用途，根按真实 PID/Session 注册或转发激活 |
| `activation.dispatch` | 根向已有 BetterGI 转发二次启动参数 |
| `input.relativeMouse.subscribe` | 桌面分身请求根开始转发相对鼠标 |
| `input.relativeMouse.unsubscribe` | 桌面分身停止转发相对鼠标 |
| `input.relativeMouse.state` | 预留的相对鼠标状态通知名称 |
| `webview.list` | 查询当前调用方可见的 WebView，按实例名排序 |
| `webview.send` | 根按目标进程 ID 向 WebView 单播 |
| `webview.message` | 根向目标 WebView 下发消息 |

上表是**根管道**上的操作。跨用户 Worker 管道上的操作集合（`worker.*` / `task.*` / `capture.*`）
见「跨用户 Worker（`--headless`）」一节的说明，两者互不重叠。

相对鼠标批次结构：

```text
uint16 sampleCount            // 1..64
uint64 firstSequence
int64  baseUtcTicks
repeat sampleCount:
    int32 deltaX
    int32 deltaY
    int32 timestampOffsetUs
```

发送端最多等待 5 ms 或累计 64 个样本后发送。队列拥塞时将后续位移合并，避免阻塞
Raw Input 采集线程。根 BetterGI 仅在游戏鼠标模式已启用、桌面分身窗口可见、RDP 已连接且
`Input Capture Window` 具有键盘焦点时转发。
