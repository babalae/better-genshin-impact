本项目使用了 WPF-UI、 CommunityToolkit.Mvvm、Microsoft.Xaml.Behaviors.Wpf 来实现 MVVM 架构。在编写代码的时候请注意：

### 主要依赖框架

#### UI 框架

- **WPF-UI (4.0.2)** - 现代化 WPF UI 框架
- **gong-wpf-dragdrop(3.2.1)** - 拖拽框架

#### MVVM 框架

- **CommunityToolkit.Mvvm (8.2.2)** - 微软官方 MVVM 工具包
  - 所有 ViewModel 必须继承自 `ObservableObject`
  - 使用 `[ObservableProperty]` 特性自动生成属性
  - 使用 `[RelayCommand]` 特性自动生成命令
- **Microsoft.Xaml.Behaviors.Wpf(1.1.122)** - WPF 行为扩展库
  - 请尽量使用 Behaviors 库来实现交互，避免不符合 MVVM 规范的交互事件触发方式。

### 其他框架使用要求

1. 请优先使用 Newtonsoft.Json 作为json序列化工具，但是如果这个模型已经被System.Text.Json序列化过了，那么就直接使用System.Text.Json反序列化。
2. 所有简单的对话框弹出需求优先使用 ThemedMessageBox 弹出。而不是 WPF 自带的 MessageBox。

## MVVM 架构规则

### 基础架构

### ViewModel 编写规范

1. **继承规则**

   ```csharp
   public partial class ExampleViewModel : ViewModel
   {
       [ObservableProperty]
       private string _title = "";
       
       [RelayCommand]
       private void DoSomething()
       {
           // 实现逻辑
       }
   }
   ```

2. **属性命名**
   - 私有字段使用下划线前缀: `_fieldName`
   - 公共属性使用 PascalCase: `PropertyName`
   - 使用 `[ObservableProperty]` 自动生成属性

3. **命令实现**
   - 使用 `[RelayCommand]` 特性
   - 异步命令使用 `[RelayCommand]` + `async Task`

### View 编写规范

1. **代码后置**

   ```csharp
   public partial class ExamplePage : UserControl
   {
       public ExampleViewModel ViewModel { get; }
       
       public ExamplePage(ExampleViewModel viewModel)
       {
           ViewModel = viewModel;
           DataContext = this;
           InitializeComponent();
       }
   }
   ```

2. **XAML 绑定**
   - 使用 `{Binding}` 语法绑定 ViewModel 属性
   - 命令绑定: `Command="{Binding ExampleCommand}"`
   - 避免在 XAML 中编写复杂逻辑

### 依赖注入规范

1. **服务注册**

   ```csharp
   // 在 App.xaml.cs 中注册
   services.AddView<ExamplePage, ExampleViewModel>();
   services.AddSingleton<IExampleService, ExampleService>();
   ```

最后，程序能够编译就认为成功，无需实际运行程序。

编译指令参考，如果出现程序占用场景，直接放弃编译验证即可

```
dotnet build BetterGenshinImpact.sln -c Debug
```

---

# BetterGI 项目完整记忆(AI生成)

## 项目概览

**BetterGI**（更好的原神）是一个基于 .NET 8 的 WPF 桌面应用程序，旨在为《原神》提供游戏自动化辅助功能。项目版本为 `0.61.3-alpha.3`，进程名称为 `BetterGI`，采用 MVVM 架构，支持 Windows x64 平台及 Wine/Linux 兼容。

核心能力：通过实时捕获游戏画面 → 图像识别（OpenCV + ONNX + OCR）→ AI 视觉定位 → 模拟键鼠输入，实现战斗、采集、秘境、Boss 讨伐、钓鱼、伐木等一系列自动化任务。

## 解决方案结构

```
better-genshin-impact/
├── BetterGenshinImpact/        # 主项目 (net8.0-windows10.0.22621.0, WPF)
├── Fischless.GameCapture/      # 游戏画面捕获库 (net8.0-windows)
├── Fischless.HotkeyCapture/    # 全局热键捕获库 (net8.0-windows)
├── Fischless.WindowsInput/     # 键鼠输入模拟库 (netstandard2.0)
└── BetterGenshinImpact.sln     # 解决方案文件
```

### 三个外部支撑项目

| 项目 | 功能 | 核心技术 |
|------|------|----------|
| `Fischless.GameCapture` | 游戏画面实时捕获 | SharpDX + D3D11，支持 BitBlt / DWM 共享表面 / Windows Graphics Capture / HDR 四种模式，输出 `Mat` |
| `Fischless.HotkeyCapture` | 全局热键注册 | Win32 `RegisterHotKey` + `NativeWindow` + `WM_HOTKEY`，支持 Win/Ctrl/Shift/Alt 组合键 |
| `Fischless.WindowsInput` | 键盘鼠标模拟 | Win32 `SendInput` API，定制自 `InputSimulator 1.0.4` |

数据流: `Fischless.GameCapture`(截图) → BetterGenshinImpact(识别+决策) → `Fischless.WindowsInput`(操作)

## 主项目分层架构

```
BetterGenshinImpact/
├── App.xaml(.cs)          # 应用入口，DI 容器配置，异常处理
├── Core/                  # 核心基础设施层
│   ├── BgiVision/         # AI 视觉识别系统 (BvImage, BvLocator, BvPage)
│   ├── Config/            # 全量配置聚合根 (AllConfig) 及所有子模块配置
│   ├── Monitor/           # 输入监控 (DirectInput, 键鼠Hook)
│   ├── Recognition/       # 图像识别层 (OCR/ONNX/OpenCV)
│   ├── Recorder/          # 宏录制与回放系统
│   ├── Script/            # JavaScript 脚本引擎桥接层
│   └── Simulator/         # 输入模拟器 (PostMessage/MouseEvent)
├── GameTask/              # 游戏自动化任务实现
│   ├── Common/            # 公共任务基础设施 (BgiVision, Map, Job, Exceptions)
│   ├── AutoBoss/          # 自动首领讨伐 (~40+ Boss 路径)
│   ├── AutoDomain/        # 自动秘境 (含多语言本地化)
│   ├── AutoFight/         # 自动战斗 (含战斗策略脚本)
│   ├── AutoFishing/       # 自动钓鱼 (行为树)
│   ├── AutoPick/          # 自动拾取 (OCR文字识别)
│   ├── AutoSkip/          # 自动跳过剧情
│   ├── AutoPathing/       # 自动寻路 (Map 导航)
│   ├── AutoTrackPath/     # 自动追踪路径 (传送任务)
│   ├── AutoWood/          # 自动伐木 (+第三方登录)
│   ├── AutoEat/           # 自动吃药
│   ├── AutoCook/          # 自动烹饪
│   ├── AutoMusicGame/     # 自动音游
│   ├── AutoGeniusInvokation/ # 自动七圣召唤 (TCG)
│   ├── AutoOpenChest/     # 自动开宝箱
│   ├── AutoArtifactSalvage/  # 自动圣遗物分解
│   ├── AutoLeyLineOutcrop/   # 自动地脉花
│   ├── QuickTeleport/     # 快速传送 (大地图点击)
│   ├── QuickSereniteaPot/ # 快速尘歌壶
│   ├── QuickBuy/          # 快速购买
│   ├── QuickForge/        # 快速锻造
│   ├── GameLoading/       # 自动开门 (加载检测)
│   ├── LogParse/          # 日志解析 (每日执行记录, 摩拉统计)
│   ├── MapMask/           # 地图遮罩 (资源点标记)
│   ├── SkillCd/           # 技能CD 显示
│   ├── GetGridIcons/      # 网格图标获取
│   ├── UseRedeemCode/     # 兑换码使用
│   ├── Macro/             # 快捷宏 (强化圣遗物, 转身)
│   ├── Shell/             # Shell 命令执行
│   ├── Placeholder/       # 占位触发器
│   ├── TaskProgress/      # 任务进度追踪
│   ├── FarmingPlan/       # 采集计划
│   ├── Model/             # 区域模型 (DesktopRegion, GameCaptureRegion, ImageRegion)
│   ├── GameTaskManager.cs # 触发器工厂 (注册所有ITaskTrigger)
│   ├── TaskTriggerDispatcher.cs # 核心调度器 (定时器驱动, 全局单例)
│   ├── TaskRunner.cs      # 独立任务执行器
│   ├── TaskContext.cs     # 全局运行时状态单例
│   ├── SystemControl.cs   # 游戏进程管理
│   ├── CaptureContent.cs  # 帧捕获数据封装
│   ├── ITaskTrigger.cs    # 触发器接口
│   ├── ISoloTask.cs       # 独立任务接口
│   └── RunnerContext.cs   # 运行上下文
├── Service/               # 服务层
│   ├── ConfigService.cs   # 配置读写 (User/config.json)
│   ├── ApplicationHostService.cs # 应用托管服务 (IHostedService)
│   ├── ScriptService.cs   # 脚本服务
│   ├── UpdateService.cs   # 更新服务
│   ├── MaskMapPointService.cs # 地图掩码点位
│   ├── Notification/      # 通知子系统 (14+ 通知通道)
│   ├── Notifier/          # 通知器实现 (Bark, Discord, 邮件, 飞书, Telegram, 钉钉 等)
│   ├── Tavern/            # 空莹酒馆 API 服务 (地图标记数据)
│   ├── Interface/         # 服务接口 (IConfigService 等)
│   ├── Model/             # API 请求/响应模型 (米哈游地图, Mirror酱, OAuth)
│   └── Translation/       # 翻译服务 (JsonTranslationService)
├── View/                  # WPF 视图层
│   ├── MainWindow.xaml    # 主窗口 (FluentWindow)
│   ├── MaskWindow.xaml    # 遮罩窗口
│   ├── Pages/             # 13 个导航页面
│   │   ├── HomePage.xaml  # 首页
│   │   ├── ScriptControlPage.xaml  # 脚本控制
│   │   ├── TriggerSettingsPage.xaml # 触发器设置
│   │   ├── MacroSettingsPage.xaml   # 宏设置
│   │   ├── CommonSettingsPage.xaml  # 通用设置
│   │   ├── TaskSettingsPage.xaml    # 任务设置
│   │   ├── HotKeyPage.xaml    # 热键设置
│   │   ├── NotificationSettingsPage.xaml # 通知设置
│   │   ├── KeyMouseRecordPage.xaml  # 键鼠录制
│   │   ├── JsListPage.xaml    # JS 脚本列表
│   │   ├── MapPathingPage.xaml # 地图寻路
│   │   ├── OneDragonFlowPage.xaml   # 一条龙流程
│   │   ├── KeyBindingsSettingsPage.xaml # 按键绑定
│   │   ├── OneDragon/     # 一条龙子页面 (Craft, DailyCommission, Domain 等 9 个)
│   │   └── View/          # 可嵌入子视图 (HardwareAcceleration, PathingConfig, ScriptGroupConfig)
│   ├── Windows/           # 15+ 独立弹窗
│   │   ├── ThemedMessageBox.xaml   # 自定义主题消息框
│   │   ├── MapViewer.xaml # 地图查看器
│   │   ├── MapPathingDevWindow.xaml # 寻路开发窗口
│   │   ├── KeyBindingsWindow.xaml  # 快捷键绑定窗口
│   │   ├── WelcomeDialog.xaml # 欢迎对话框
│   │   ├── Editable/ScriptGroupProjectEditor.xaml # 脚本组编辑
│   │   └── ... (AboutWindow, ArtifactOcrDialog, CheckUpdateWindow, FeedWindow 等)
│   ├── Controls/          # 自定义控件 (CodeEditor, Draggable, HotKey, Webview 等)
│   ├── Converters/        # 14 个 XAML 值转换器
│   ├── Behaviors/         # XAML 行为 (AutoTranslate, Clipboard, DomainCascading 等)
│   └── Drawable/          # 绘制抽象 (DrawContent, RectDrawable, LineDrawable 等)
├── ViewModel/             # MVVM 视图模型层
│   ├── ViewModel.cs       # 抽象基类 (继承 ObservableObject, 实现 INavigationAware)
│   ├── MainWindowViewModel.cs  # 主窗口 VM
│   ├── MaskWindowViewModel.cs  # 遮罩窗口 VM
│   ├── NotifyIconViewModel.cs  # 系统托盘 VM
│   ├── Pages/             # 与 View/Pages 一一对应
│   │   ├── OneDragon/     # 一条龙子页面 VM (OneDragonBaseViewModel 基类)
│   │   └── View/          # 子视图 VM (含 AutoFightViewModel - 无独立 XAML)
│   ├── Windows/           # 弹窗 VM (FeedWindow, JsonMono, MapPathingDev, MapViewer)
│   └── Message/           # MVVM Messenger 消息
├── Helpers/               # 工具/辅助类
│   ├── Crud/              # JSON 文件 CRUD (JsonCrudHelper, ICrudHelper)
│   ├── DpiAwareness/      # 高 DPI 适配 (含 Wine 支持)
│   ├── Extensions/        # C# 扩展方法 (Bitmap, Mat, Point, Rect, Task 等)
│   ├── Http/              # HttpClient 工厂 + 代理速度测试
│   ├── Security/          # MD5Helper
│   ├── Ui/                # 文件树节点 + 窗口辅助
│   ├── Win32/             # Win32 P/Invoke (ConsoleHelper, CredentialManager)
│   ├── AssertUtils.cs     # 断言工具 (含 16:9 分辨率检测)
│   ├── DpiHelper.cs       # DPI 缩放辅助
│   ├── ServerTimeHelper.cs # 服务器时间 (时区偏移)
│   ├── TempManager.cs     # 临时文件管理
│   ├── UIDispatcherHelper.cs # UI 线程调度
│   ├── UrlProtocolHelper.cs  # BetterGI:// URL 协议注册
│   └── ... (AutoBossCascadingItems, CultureHelper, DeviceIdHelper, MathHelper, OsVersionHelper 等)
├── Hutao/                 # 胡桃工具箱 IPC (Named Pipe 通信)
├── Genshin/               # 原神游戏设置 (注册表, 分辨率, 语言, 输入数据)
├── Platform/Wine/         # Wine/Linux 平台适配
├── Resources/             # 内置资源 (字体: Fgi-Regular/MiSans/deluge-led, 图标)
├── User/                  # 用户数据目录 (运行时不编译)
└── Assets/                # 模型/地图等大型资源 (通过 NuGet 包引用: BetterGI.Assets.Map, Model, Other)
```

## 核心架构设计

### 1. 任务调度系统

整个自动化系统的核心是一个**图像帧驱动的触发器轮询架构**：

```
TaskTriggerDispatcher (定时器, 默认 50ms 间隔)
    │
    ├─ 维护 IGameCapture 实例 (统一截图入口)
    ├─ 监听游戏窗口位置/大小变更 (HWINEVENTHOOK)
    ├─ 按 Priority 从高到低依次调用所有 ITaskTrigger.OnCapture(CaptureContent)
    │
    └─ 已注册的触发器 (在 GameTaskManager.LoadInitialTriggers 中注册):
         ├─ GameLoadingTrigger (Priority: 999, 自动开门)
         ├─ QuickTeleportTrigger (Priority: 21, 快速传送)
         ├─ AutoPickTrigger (自动拾取)
         ├─ AutoSkipTrigger (自动跳过剧情)
         ├─ AutoFishingTrigger (自动钓鱼)
         ├─ AutoEatTrigger (自动吃药)
         ├─ MapMaskTrigger (地图遮罩)
         ├─ SkillCdTrigger (技能CD)
         └─ RecognitionTest (识别测试, Debug 模式)
```

**ITaskTrigger 接口**: `Name`(名称), `Priority`(优先级), `IsExclusive`(独占模式), `IsBackgroundRunning`(后台运行), `SupportedGameUiCategory`(UI过滤), `OnCapture(CaptureContent)`(每帧回调).

**TaskRunner**: 用于执行带锁的独立异步任务 (`ISoloTask`)，提供信号量互斥、上下文初始化/清理、分层异常处理，支持 `RunCurrentAsync` / `FireAndForget` / `RunThreadAsync` 多种模式。

### 2. 配置系统

`AllConfig` 继承 `ObservableObject`，是配置聚合根，使用 `[ObservableProperty]` 自动生成可绑定属性。配置持久化到 `User/config.json`（`System.Text.Json`）。

关键子模块配置类（均位于 `Core/Config/`）：

- `CommonConfig`: 通用设置
- `GenshinStartConfig`: 游戏启动配置
- `MaskWindowConfig`: 遮罩窗口配置
- `HotKeyConfig`: 热键配置
- `KeyBindingsConfig`: 按键绑定配置
- `MacroConfig`: 宏配置
- `RecordConfig`: 录制配置
- `ScriptConfig`: 脚本配置
- `OneDragonFlowConfig`: 一条龙流程配置
- `PathingConditionConfig` / `PathingPartyConfig` / `PathingPartyTaskCycleConfig`: 寻路条件
- `HardwareAccelerationConfig`: 硬件加速配置
- `TaskCompletionSkipRuleConfig`: 任务完成跳过规则

`Global` 静态工具类提供版本号、启动路径、JSON 序列化配置 (`ManifestJsonOptions`) 和版本比较。

### 3. 图像识别系统 (Core/Recognition/)

三层架构：

| 层次 | 技术 | 位置 | 功能 |
|------|------|------|------|
| OCR 文字识别 | PaddleOCR | `OCR/` | `IOcrService` 接口，`OcrFactory` 工厂，`PaddleOcrService` 实现，支持 OcrResult 结果处理 |
| ONNX 推理 | Microsoft.ML.OnnxRuntime (DirectML) | `ONNX/` | `BgiOnnxFactory` 工厂，YOLO 目标检测 (`Predictor`)，SVTR 文字推理 (`ITextInference`) |
| OpenCV 视觉 | OpenCvSharp4 | `OpenCv/` | 模板匹配 (`MatchTemplateHelper`)、通用识别 (`CommonRecognition`)、图像运算 (`ArithmeticHelper`)、轮廓 (`ContoursHelper`)、裁剪/缩放 |

**`RecognitionObject`**: 封装图像识别的所有参数（模板图、阈值、匹配模式、遮罩配置、感兴趣区域），支持 `RecognitionTypes` 枚举的 7 种识别策略：

- `TemplateMatch` - 模板匹配
- `ColorMatch` - 颜色匹配
- `OcrMatch` - OCR 文字匹配
- `Ocr` - 纯 OCR
- `ColorRangeAndOcr` - 颜色提取后 OCR
- `Detect` - 检测
- `None` - 未指定

**`BgiVision` (Bv) 系统**: 位于 `Core/BgiVision/` 和 `GameTask/Common/BgiVision/` 的高层视觉 API，封装常用识别操作（`BvImage` 图像识别、`BvLocator` 定位器、`BvPage` 页面级视觉宏观操作、`BvStatus` 状态检测、`BvChatUi` 聊天界面、`BvSkill` 技能、`BvOcr` OCR 封装、`BvResxHelper` 多语言资源辅助、`BvSimpleOperation` 简单操作）。

### 4. 脚本系统 (Core/Script/)

基于 **Microsoft.ClearScript.V8** (V8 引擎) 的 JavaScript 脚本系统。

`EngineExtend.InitHost()` 向 JS 引擎注册宿主对象和类型，使 JavaScript 脚本可调用 C# 能力：

**脚本可用的宿主对象**: `keyMouseScript`(键鼠模拟), `pathingScript`(自动寻路), `genshin`(原神功能封装), `log`(日志), `file`(受限文件访问), `http`(HTTP), `notification`(通知), `dispatcher`(任务调度), `OpenCvSharp`(OpenCV), `strategyFile`(策略), `host`(自定义宿主函数), `htmlMask`(HTML遮罩).

**脚本可用的宿主类型**: `Mat`, `RecognitionObject`, `Region`, `DesktopRegion`, `GameCaptureRegion`, `ImageRegion`, `BvPage`, `BvLocator`, `BvImage`, `CancellationTokenSource`, `CombatScenes`, `Avatar` 等.

`CancellationContext` 提供线程安全的全局取消令牌管理，支撑所有任务的中断/取消。

依赖模块 (`Dependence/`): `Genshin` (窗口信息)、`GlobalMethod` (全局方法)、`KeyMouseHook` (键鼠钩子)、`Http` (HTTP 请求)、`Log` (日志)、`Notification` (通知)、`ServerTime` (服务器时间)、`StrategyFile` (策略文件)、`HtmlMask` (HTML 遮罩)、`AutoPathingScript` (寻路脚本)。

### 5. 通知系统 (Service/Notification/)

支持 **14+ 种通知通道**，统一通过 `NotifierManager` 管理：

| 通知器 | 实现类 | 通知器 | 实现类 |
|------|------|------|------|
| Bark 推送 | `BarkNotifier` | Discord Webhook | `DiscordWebhookNotifier` |
| 邮件 | `EmailNotifier` | 飞书 | `FeishuNotifier` |
| Telegram | `TelegramNotifier` | 企业微信 | `WorkWeixinNotifier` |
| 钉钉 Webhook | `dingdingwebhookNotifier` | Server 酱 | `ServerChanNotifier` |
| Windows UWP | `WindowsUwpNotifier` | WebSocket | `WebSocketNotifier` |
| 通用 Webhook | `WebhookNotifier` | OneBot | `OneBotNotifier` |
| Meow | `MeowNotifier` | xxtui | `xxtuiNotifier` |

通知事件类型 (`NotificationEvent` 枚举): 秘境完成、Boss 讨伐完成、七圣召唤完成、脚本执行完成、测试通知等。

### 6. 宏录制与回放 (Core/Recorder/)

`GlobalKeyMouseRecord`: 全局单例录制器，通过全局键鼠 Hook + DirectInput 监控，自动检测游戏主界面（通过识别 `FriendChat` 图标），主界面内录相对位移，其他界面录绝对位置。导出 JSON 格式宏。

`KeyMouseMacroPlayer`: 宏回放器，反序列化 JSON 宏 → 适配分辨率/DPI → 按时间戳逐个事件回放。`MouseMoveBy` 回放时包含相机朝向偏差自动修正（< 8 度）。

### 7. 一条龙流程 (OneDragon)

配置驱动的自动化流水线系统，通过 `OneDragonFlowConfig` 将独立任务串联成完整流程。任务项通过 `OneDragonTaskItem` 的 switch 分发：

| 任务 | 实现类 |
|------|--------|
| 领取邮件 | `ClaimMailRewardsTask` |
| 合成树脂 | `GoToCraftingBenchTask` |
| 自动秘境 | `AutoDomainTask` |
| 自动首领讨伐 | `AutoBossTask` |
| 自动幽境危战 | `AutoStygianOnslaughtTask` |
| 自动地脉花 | `AutoLeyLineOutcropTask` |
| 领取每日奖励 | `GoToAdventurersGuildTask` |
| 领取尘歌壶奖励 | `GoToSereniteaPotTask` |

生成后在 OneDragonFlowPage 可视化操作，支持配置的增删改、参数设置和按序一键执行，可完成后自动关机/关游戏/关软件。

### 8. 自动化任务工作流示例

**自动 Boss 讨伐 (`AutoBossTask`)**: 切队 → 树脂检查 → JSON 路径文件寻路 → 战斗 → 领奖 → 循环。约 40+ Boss 的路径数据位于 `GameTask/AutoBoss/Assets/Pathing/`。

**自动秘境 (`AutoDomainTask`)**: 传送 → 进入秘境 → 初始化队伍 → 走位启动 → 自动战斗 → 寻石化古树 → 领奖 → 圣遗物分解。支持多语言 OCR（中文简/繁、英文、法文）。

**自动钓鱼 (`AutoFishingTask`)**: 基于行为树 (`BehaviourTree` NuGet 包) 实现，通过 `RodInput` / `RodNet` 控制抛竿和收竿时机。

**自动战斗 (`AutoFightTask`)**: 使用 `CombatScriptParser` 解析策略脚本 → `CombatScriptBag` 执行，支持角色宏 (`avatar_macro_default.json`)。

**快速传送 (`QuickTeleportTrigger`)**: 在大地图界面检测传送按钮和传送点图标（支持 18 种类型：七天神像、锚点、秘境、尘歌壶等），自动点击传送。

**自动寻路 (`PathExecutor`)**: 根据 JSON 路径文件，支持 `walk`/`fly` 两种移动模式，`teleport`/`path` 两种点类型，以及 `stop_flying`、`auto_fight`、`fishing`、`mining`、`set_time`、`use_gadget` 等多种动作。

## 关键 NuGet 依赖

| 类别 | 包名 | 版本 | 用途 |
|------|------|------|------|
| UI | WPF-UI | 4.3.0 | 现代化 WPF UI 框架 |
| UI | WPF-UI.DependencyInjection | 4.3.0 | DI 扩展 |
| UI | WPF-UI.Tray | 4.3.0 | 系统托盘 |
| UI | WPF-UI.Violeta | 4.3.0 | 主题/外观 |
| MVVM | CommunityToolkit.Mvvm | 8.2.2 | MVVM 工具包 |
| Behaviors | Microsoft.Xaml.Behaviors.Wpf | 1.1.122 | XAML 行为扩展 |
| DragDrop | gong-wpf-dragdrop | 3.2.1 | 拖拽框架 |
| Vision | OpenCvSharp4.* | 4.11.0 | 计算机视觉 |
| Vision | Microsoft.ML.OnnxRuntime (DirectML) | 1.21.0 | ONNX GPU 推理 |
| Vision | YoloSharp | 6.0.3 | YOLO 目标检测 |
| Vision | TorchSharp | 0.105.0 | PyTorch (行为树辅助) |
| Script | Microsoft.ClearScript.V8 | 7.4.5 | V8 JS 引擎 |
| Input | MouseKeyHook | 5.7.1 | 全局键鼠钩子 |
| Input | Vanara.PInvoke.* | 4.1.3 | Win32 P/Invoke |
| JSON | Newtonsoft.Json | 13.0.3 | JSON 序列化 |
| Log | Serilog.* | 9.x | 结构化日志 |
| Cache | LazyCache | 2.4.0 | 内存+文件缓存 |
| HTTP | MailKit / MimeKit | 4.x | 邮件通知 |
| Code | AvalonEdit | 6.3.1.120 | 代码编辑器 |
| AI | BehaviourTree | 1.0.73 | 行为树引擎 (钓鱼) |
| Misc | Emoji.Wpf | 0.3.4 | Emoji 渲染 |
| Misc | LibGit2Sharp | 0.31.0 | Git 操作 (脚本仓库) |
| Misc | Semver | 3.0.0 | 语义化版本比较 |
| Misc | DeviceId | 6.9.0 | 设备标识 |

## 平台适配 (Wine/Linux)

`Platform/Wine/WinePlatformAddon.cs`:

- 通过 P/Invoke 检测 `ntdll.dll` 中的 `wine_get_version` 判断是否在 Wine 环境
- Wine 下强制软件渲染 (禁用硬件加速)
- 提供定时器轮询按键状态的替代方案 (Windows 下自动跳过)
- DPI 感知适配 (`DpiAwarenessController`)

## 本地化 (i18n)

- 使用 .NET `IStringLocalizer` + `.resx` 资源文件实现多语言
- UI 翻译: `Service/JsonTranslationService` 从 JSON 文件 (`User/I18n/en.json`, `ja.json`) 加载
- 游戏内 OCR 本地化: `AutoDomainTask.*.resx` 和 `TpTask.*.resx` 支持 en/fr/zh-Hans/zh-Hant
- `BvResxHelper` 提供运行时的游戏语言感知翻译

## 通用 Job 设施 (GameTask/Common/Job/)

可复用的原子任务：

- `CheckRewardsTask`: 检查奖励
- `ChooseTalkOptionTask`: 选择对话选项
- `ClaimMailRewardsTask`: 领取邮件
- `CountInventoryItem`: 统计背包物品
- `ExitAndReloginJob`: 退出并重新登录
- `GoToSereniteaPotTask`: 前往尘歌壶
- `GoToCraftingBenchTask`: 前往合成台 (支持蒙德/璃月/稻妻/枫丹)
- `GoToAdventurersGuildTask`: 前往冒险家协会
- `LinneaMiningTask`: 琳妮特采矿
- `ReturnMainUiTask`: 返回主界面
- `ScanPickTask`: 扫描拾取
- `SetTimeTask`: 设置游戏时间
- `SwitchPartyTask`: 切换队伍
- `WalkToFTask`: 走向交互
- `EnterAndExitWonderlandJob`: 进出探索幻境
- `LowerHeadThenWalkToTask`: 低头走向目标

## 地图系统 (GameTask/Common/Map/)

- 地图管理器 (`MapManager`) 支持多地标：提瓦特 (`TeyvatMap`)、渊下宫 (`EnkanomiyaMap`)、层岩巨渊 (`TheChasmMap`)
- `MapAssets` / `MapLazyAssets` 管理地图点位数据
- `CameraOrientation` / `CharacterOrientation` 追踪相机和角色朝向
- `Navigation` / `NavigationInstance` 提供寻路计算 (WASD 移动 + 视角控制)

## DI 注册总览

在 `App.xaml.cs` 中注册的所有服务：

- **基础设施**: `IConfigService`, `IAppCache`, `MemoryFileCache`, `TimeProvider`, `IServerTimeProvider`
- **UI**: `INavigationService`, `ISnackbarService`, `NotifyIconViewModel`
- **识别**: `BgiOnnxFactory`, `OcrFactory`
- **服务**: `ScriptService`, `UpdateService`, `NotificationService`, `OverlayMetricsService`
- **通知**: `NotifierManager` (+ 所有 INotifier 实现)
- **地图 API**: `IMihoyoMapApiService`, `IKongyingTavernApiService`, `IHoYoLabMapApiService`, `IMaskMapPointService`
- **IPC**: `HutaoNamedPipe`
- **翻译**: `ITranslationService`, `IMissingTranslationReporter`
- **核心调度**: `TaskTriggerDispatcher`

## 关键设计模式

1. **图像帧驱动调度**: 定时器按固定间隔截图 → 所有触发器依次处理 → 独立任务通过 TaskRunner 异步执行
2. **单一截图源**: `TaskTriggerDispatcher` 持有唯一的 `IGameCapture`，所有触发器共享同一帧图像
3. **信号量互斥**: `TaskRunner` 确保同一时刻只有一个独立任务执行
4. **UI 类别过滤**: `SupportedGameUiCategory` 控制触发器仅在特定游戏界面生效
5. **插件式任务**: `ITaskTrigger` 接口实现即插即用，新增自动化功能只需实现接口并注册
6. **配置驱动**: 所有行为参数通过 `ObservableObject` 配置类管理，支持 MVVM 双向绑定和 JSON 持久化
