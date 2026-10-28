# 基础设施统一：WebView2、网络与下载、缓存、临时文件

状态：设计已确认（第 2 版），2026-10-28，基于 `refactor/20260930` 分支代码。阶段 1 至 6 已全部实施。

第 2 版变更：程序生成的数据统一放到新目录 `Data/`；推理缓存、通用文件缓存、下载分目录存放；WebView2 支持多个普通环境并集中管理环境配置；旧目录每个版本清理一次，不再迁移；第 15 节的决策已确认。

相关文档：[三种实例的能力边界](instance-capability-boundaries.md)（多实例共享程序目录与 `User/`）、[配置持久化链路](config-persistence.md)、[GameRuntime 设计](game-runtime.md)（云原神运行环境）。

## 1. 目标与范围

本次重构把四类分散的基础能力收拢为统一的基础设施服务：

1. **WebView2**：所有 WebView2 环境由一个入口创建。云原神网页版使用"云原神环境"，其他网页使用"普通环境"，二者在代码、数据目录和清理方式上明确区分。普通环境目前只有一个，以后可以有多个，它们的配置集中定义、统一管理。
2. **网络**：对外 HTTP 请求统一由命名 HttpClient 发出，代理在一处设置、全局生效。文件下载统一由下载服务完成，下载进度由下载监视器统一提供给界面。
3. **缓存**：可再生的图片和数据缓存放在统一目录，由一个缓存服务读写，提供界面查看和清理。
4. **临时文件**：统一的临时目录和临时文件服务，按实例隔离、按功能分类，可按功能清理。

目标效果：新功能需要缓存、临时文件、下载、HTTP 或网页时，直接使用现成服务，并自动出现在"存储空间"和"下载中心"界面里，不再自建目录、HttpClient 或清理逻辑。

不在本次范围：`BetterGI.update.exe` 更新程序的内部实现、识别模块的进程内素材缓存、`User/` 下用户数据的管理。完整列表见第 11 节。

## 2. 现状与问题

### 2.1 WebView2

| 使用方 | 用途 | 环境创建方式 | 用户数据目录 |
|---|---|---|---|
| `WebpagePanel`（被 `WebpageWindow`、`CheckUpdateWindow` 使用） | 更新日志、脚本仓库、地图路径编辑器 | WPF 控件隐式创建 | `Environment.ProcessPath` 所在目录下的 `WebView2Data` |
| `HtmlMaskWindow` | 脚本 HTML 遮罩 | 每个窗口各自 `CreateAsync`，Profile 为 `HtmlMask` | `AppContext.BaseDirectory` 下的 `WebView2Data` |
| `CloudWebHostWindow` | 云原神网页版 | `CreateAsync` 加 4 项禁用后台降频的浏览器参数，窗口句柄直接托管 Controller | `Global.Absolute("WebView2Data/CloudGame/{实例名}")` |

问题：

- 三处用三种方式拼路径。以 `dotnet BetterGI.dll` 启动时，`Environment.ProcessPath` 指向 dotnet.exe，`WebpagePanel` 的数据会写进 dotnet 安装目录。
- 前两行共用同一个用户数据目录，目前都没有浏览器参数所以能共存。WebView2 要求同一目录的所有环境参数一致，只要有人给其中一处加参数，另一处就会初始化失败，而代码里没有任何地方保证这一点。
- 运行时检测只有 `WebpagePanel` 有。它的"下载 WebView2"按钮调用 `Process.Start(url)`，.NET 下默认 `UseShellExecute=false`，链接打不开。`HtmlMaskWindow` 缺运行时时记录日志后直接关闭窗口，用户看不到原因。
- 全仓库没有 `ProcessFailed` 处理。云原神页面进程崩溃后，只能靠每秒一次的就绪轮询间接发现。
- `WebpagePanel.EnsureWebView2DataFolder` 给 `Everyone` 授予完全控制权限，范围过大。
- 网页数据没有清理入口。云原神实例只能整个删除，同时丢失登录状态。

### 2.2 HTTP、下载与代理

- 主项目有 20 多处 HTTP 调用点，创建方式有三种：`new HttpClient()`（部分每次请求 `using` 新建）、静态字段、`Helpers/Http/HttpClientFactory` 按 key 静态缓存。没有使用 `IHttpClientFactory`。
- 超时和 UA 各不相同，从"默认 100 秒、无 UA"到自定义浏览器 UA 都有。`MapIconImageCache` 下载时传 `CancellationToken.None`，外部取消无效。
- JS 脚本的 `http.request`（`Core/Script/Dependence/Http.cs`）每次请求新建 HttpClient。允许列表只校验首个 URL，而 HttpClient 默认自动跟随重定向，重定向到列表外的地址不会被拦截。
- 代理：没有全局设置，全部走 .NET 默认的系统代理。只有 Telegram 通知有独立代理配置。LibGit2Sharp 克隆显式设为 `ProxyType.None`，注释说明是为了"无视本地代理"。
- 下载：只有 `RuntimePackageDownloader`（ONNX 运行库）实现了断点续传、哈希校验和进度回报，但只服务硬件加速页面。`ScriptRepoUpdater.DownloadRepoAndUnzip` 把整个响应读进内存，文件名直接取自服务器的 `Content-Disposition`，目前只被已注释的代码调用。
- 下载进度分散在各自的窗口（硬件加速页、脚本仓库窗口），离开页面后看不到后台下载状态。

### 2.3 缓存

| 缓存 | 目录 | 有效期 | 清理 |
|---|---|---|---|
| `MemoryFileCache`（通用内存加磁盘缓存） | `User/Cache/MemoryFileCache/{类型}` | 读取时按 TTL 判断 | 无总量上限，无过期扫描 |
| 地图点位图标 | 上述目录 `map-icon-image`，另有进程内 `ImageSource` 字典 | 20 天 | 内存字典无上限 |
| 空荧酒馆点位 | 上述目录 `kongying-tavern-marker-doc-json` | 30 天 | 按 key 集合剪除（唯一的主动清理） |
| 音乐封面 | `User/Music/Artwork` | 180 天 | 无 |
| 点位详情图片、Markdown 图片 | 只在内存 | - | - |
| 推理编译缓存 | `Cache/Inference/{指纹}` | 无 | 只能打开目录 |
| 按版本的模型缓存 | `Cache/{版本号}/Model/{模型名}` | 无 | `BgiOnnxModel.Register` 每次启动为每个模型创建空目录，`CachePath` 没有任何读写方，每个版本都会留下一组空目录 |
| ONNX 运行库安装包 | `Plugins/OnnxRuntime/.downloads` | 无 | 安装成功后删除；失败时保留供续传，TensorRT 包约 1.8 GB |

问题：缓存分布在 `User/`、`Cache/`、`Plugins/` 三处。程序根目录的 `Cache/` 下直接混放 ONNX Runtime 的编译缓存和按版本号命名的目录，没有按用途区分。放在 `User/` 下的缓存会在用户迁移版本、复制 `User/` 时一起被复制。没有总量上限、过期扫描和界面。

### 2.4 临时文件

| 用法 | 目录 | 问题 |
|---|---|---|
| `TempManager` | `User/Temp` | 启动和退出时删除整个目录。主实例、桌面分身、云原神实例共用该目录，一个实例退出会删掉其他实例正在用的文件。异常被吞掉，没有日志 |
| `WindowsUwpNotifier` 通知图片 | `User/Temp` | 用后不删，依赖整目录清理 |
| 订阅脚本更新前备份用户文件 | `User/Temp/{脚本路径}` | 更新中途崩溃后，下次启动的整目录清理会删掉备份，用户保存的脚本文件丢失。脚本路径来自订阅链接（剪贴板内容，属于外部输入），没有校验 `..` |
| 脚本仓库 ZIP 导入 | `Repos/Temp/importZipFile` | 自成一套目录 |
| 更换仓库地址时重新克隆 | `{仓库目录}_temp_{8位}` | 进程被结束时残留 |
| 各处原子写入 | 目标文件同目录的 `*.tmp` | 命名规则不一，只有 `ConfigService` 清理残留 |

### 2.5 调研中发现的缺陷

以下缺陷与重构范围重叠，随对应阶段一起修复。阶段编号见第 13 节。

| 缺陷 | 位置 | 影响 | 阶段 |
|---|---|---|---|
| 脚本更新备份会被启动清理删除 | `ScriptRepoUpdater.BackupScriptFiles` / `RestoreScriptFiles` | 更新中途崩溃时丢失用户保存的脚本文件 | 1 |
| 备份路径未校验 | 同上 | 订阅链接里带 `..` 的路径可以在 `User/Temp` 之外创建和覆盖文件 | 1 |
| 多实例共用临时目录且整目录删除 | `TempManager` | 一个实例退出会删除另一实例的临时文件 | 1 |
| 启动时的临时目录清理与订阅脚本自动更新同时进行 | `MainWindowViewModel.OnLoaded` 先调用 `StartAutoUpdate()`（后台执行），紧接着调用 `TempManager.CleanUp()` | 自动更新刚写入 `User/Temp` 的备份可能被立即删除 | 1 |
| manifest 的 `savedFiles` 路径未校验 | `ScriptRepoUpdater.BackupScriptFiles` | 仓库中的 manifest 写入 `..` 可以把用户文件复制到备份目录之外 | 1 |
| JS 脚本 HTTP 重定向绕过允许列表 | `Core/Script/Dependence/Http.cs` | 脚本可经重定向访问未授权地址 | 3 |
| 灰度判定每次启动都变 | `UpdateService.UpdateFromOss` 中的 `deviceId.GetHashCode()` | .NET 的字符串哈希按进程随机，灰度结果不是按设备稳定分组。改为取 `SHA256(deviceId)` 前 4 字节对 10 取余，同一设备每次结果相同（已确认修复，灰度用户分布会随之变化） | 3 |
| `DownloadRepoAndUnzip` 不安全且无调用方 | `ScriptRepoUpdater` | 文件名取自响应头，可越界写入；整包读入内存 | 3（删除） |
| 每次启动创建无用的空目录 | `BgiOnnxModel.Register` | 每个版本在 `Cache/{版本号}/Model` 下留下一组空目录 | 2（删除） |
| WebView2 下载按钮打不开链接 | `WebpagePanel.CreateDownloadButton` | 未安装运行时的用户无法跳转下载页 | 6 |
| dotnet 宿主下网页数据目录错误 | `WebpagePanel` | 数据写进 dotnet 安装目录 | 6 |

## 3. 总体设计

### 3.1 分层

```text
View         设置页子视图、存储空间窗口、标题栏下载中心、WebpagePanel
  │ 绑定
ViewModel    NetworkSettingsViewModel、StorageManagementViewModel、DownloadCenterViewModel
  │ 调用
业务服务     UpdateService、ScriptRepoUpdater、KongyingTavernApiService、RemoteImageService …
  │ 依赖接口
Infrastructure（新增）
  ├─ Storage   目录约定、存储区域登记、占用统计与清理、原子写入
  ├─ Temp      临时文件服务
  ├─ Cache     文件缓存服务与维护
  ├─ Network   命名 HttpClient、代理、下载服务、下载监视器
  └─ WebView   运行时检测、环境入口、Profile 定义、通用初始化
  │ 只读
Core/Config  NetworkConfig、StorageConfig 等配置模型
```

依赖规则：

- `Infrastructure` 不引用 `View`、`ViewModel`、`GameTask`、`Pulonia` 和具体业务服务。需要的配置以配置模型对象注入，不直接调用 `ConfigService`。
- `Infrastructure/WebView` 依赖 WebView2 SDK，但不弹窗、不创建窗口。需要提示用户的逻辑放在 View 层。
- 业务代码只依赖接口和公开的定义类（`TempCategories`、`CachePartitions`、`HttpClientNames`、`WebViewProfiles`），不自己拼这几类目录。
- 界面不直接读写磁盘，统一通过 `IStorageService` 和 `IDownloadMonitor`。

### 3.2 代码目录

```text
BetterGenshinImpact/Infrastructure/
  Storage/   AppPaths、StorageArea、IStorageAreaProvider、IStorageService、StorageService、PendingCleanupStore、LegacyDataCleaner、AtomicFile
  Temp/      TempCategory（含 TempCategories）、ITempFileService、TempFileService、TempScope
  Cache/     CachePartition（含 CachePartitions）、IFileCache、FileCache、CacheMaintenanceService
  Network/   HttpClientNames、AppWebProxy、BgiHttpHandlers、BgiHttpDefaultsHandler、GitProxy、NetworkDiagnostics
  Network/Download/  DownloadCategory（含 DownloadCategories）、DownloadRequest、DownloadProgress、DownloadTask、IDownloadService、DownloadService、IDownloadMonitor
  WebView/   WebViewRuntime、WebViewEnvironmentDefinition（含 WebViewEnvironments）、WebViewProfile（含 WebViewProfiles）、IWebViewEnvironmentProvider、WebViewEnvironmentProvider、WebViewSetup
```

命名空间为 `BetterGenshinImpact.Infrastructure.*`。按项目约定，重要类一个文件一个类，小型 record 与对应的静态定义类可以合并。

新建顶层目录而不是放进 `Core` 或 `Service`：`Core` 里混有配置、识别和脚本，`Service` 里是业务服务。独立目录让依赖方向可以直接检查：`Infrastructure` 下的文件不应出现 `using BetterGenshinImpact.View`、`ViewModel` 或 `GameTask`。

`RemoteImageService` 产出 WPF 的 `ImageSource`，属于业务服务层，放在 `Service/`，与已有的 `ImageSourceDecoder` 相邻。

### 3.3 磁盘目录约定

程序自动生成的数据统一放在程序目录（`Global.StartUpPath`）下的新目录 `Data/`。`Data/` 下按用途分目录，每个目录只放一类内容；`Data/` 根目录只有这些子目录和两个清理用的状态文件：

```text
{程序目录}/
  Data/                          程序生成的数据，由基础设施服务管理
    Cache/
      Files/{分区}/              通用文件缓存：图片、接口数据
      Inference/{指纹}/          ONNX Runtime 的 TensorRT、OpenVINO 编译缓存
    Downloads/{分类}/            下载的文件和未完成的 .part
    Temp/{进程目录}/{分类}/       临时文件
    WebView2/
      App/                       软件内网页环境（Profile：Default、HtmlMask）
      CloudGame/{实例名}/         云原神实例，一个实例一个目录
      {以后新增的环境 Id}/
    legacy-cleanup.json          旧目录清理标记（见 3.4）
    pending-cleanup.json         推迟到下次启动的清理（见 3.5）
  User/                          用户数据，基础设施不清理
  log/                           日志、截图、调试图片（位置不变）
```

| 目录 | 内容 | 管理者 | 能否清理 |
|---|---|---|---|
| `Data/Cache/Files/{分区}/` | 可再生的图片和接口数据 | IFileCache | 随时可清；超过上限自动淘汰 |
| `Data/Cache/Inference/{指纹}/` | 推理编译缓存，按引擎、设备、运行库版本指纹分目录 | 推理模块 | 下次启动时清理 |
| `Data/Downloads/{分类}/` | 下载的安装包、未完成的 `.part` | IDownloadService | 可清，清理后需重新下载 |
| `Data/Temp/{进程目录}/{分类}/` | 临时文件 | ITempFileService | 进程退出时删除，残留在下次启动回收 |
| `Data/WebView2/{环境 Id}/` | 软件内网页数据 | IWebViewEnvironmentProvider | 清理缓存，或清除某个 Profile |
| `Data/WebView2/CloudGame/{实例名}/` | 云原神实例数据 | WebViewInstanceStore | 实例未运行时清理缓存；删除实例清除全部 |
| `User/` | 配置、脚本、录制、任务数据 | 各业务模块 | 不清理，存储空间窗口只展示占用 |
| `User/Backup/ScriptUpdate/` | 脚本更新前的用户文件备份 | ScriptRepoUpdater | 需确认后清除 |
| `log/` | 日志、截图、调试图片 | Serilog 与各功能 | 日志自动保留；截图和调试图片手动清理 |

取舍：

- 用一个 `Data/` 目录收拢缓存、下载、临时文件和网页数据，与 `User/`（用户数据）、`Repos/`（脚本仓库）、`log/`（日志）分开，用户一眼能分清"程序生成的"和"自己的"。目录名只在 `AppPaths.Root` 定义一次，以后要改名只改一处。
- 推理编译缓存与通用文件缓存分开。前者体积大、重建要重新编译，不参与文件缓存的容量淘汰；后者可以随时清。
- 下载单独成目录，不归入缓存。下载的是安装包这类"要拿来用的文件"，并且有未完成的续传进度，清理影响与缓存不同；在存储空间窗口中也单独成组。
- `Data/` 放在程序目录，而不是 `%TEMP%` 或 `%LocalAppData%`。`Data/Temp` 与 `Repos/` 在同一分区，解压结果可以直接 `Directory.Move` 进仓库目录；用户删除程序目录时不会留下垃圾。
- 不放在 `User/` 下。`User/` 只放用户数据，用户迁移版本时复制 `User/` 不会带上可再生的数据。
- 更新包不得包含 `Data/`。

`AppPaths` 是这些目录的唯一来源。它只覆盖本次涉及的目录，不替换其他模块中已有的 `Global.Absolute("User/...")`。

```csharp
/// <summary>
/// 基础设施目录约定。缓存、临时文件、下载和网页数据目录只从这里取。
/// </summary>
public static class AppPaths
{
    /// <summary>程序生成数据的根目录。</summary>
    public static string Root => Global.Absolute("Data");

    /// <summary>缓存根目录，下面只放按用途划分的子目录。</summary>
    public static string Cache => Path.Combine(Root, "Cache");

    /// <summary>通用文件缓存目录，按缓存分区分子目录。</summary>
    public static string FileCache => Path.Combine(Cache, "Files");

    /// <summary>ONNX Runtime 编译缓存目录（TensorRT engine/timing、OpenVINO）。</summary>
    public static string InferenceCache => Path.Combine(Cache, "Inference");

    /// <summary>下载目录，按下载分类分子目录。</summary>
    public static string Downloads => Path.Combine(Root, "Downloads");

    /// <summary>临时文件根目录，按进程分子目录。</summary>
    public static string Temp => Path.Combine(Root, "Temp");

    /// <summary>WebView2 用户数据根目录，按环境 Id 分子目录。</summary>
    public static string WebView => Path.Combine(Root, "WebView2");

    /// <summary>云原神网页版实例数据根目录。</summary>
    public static string WebViewCloudGame => Path.Combine(WebView, "CloudGame");
}
```

### 3.4 旧目录清理

旧目录里的数据都可以重建，不做迁移，直接删除。由 `LegacyDataCleaner` 负责：

- **触发**：主实例启动后在后台执行，不阻塞启动。新旧路径互不重叠，后台删除不会影响正在使用的新目录。
- **每个版本只触发一次**：`Data/legacy-cleanup.json` 记录最近一次全部清理成功的版本号。记录的版本等于当前版本时直接跳过。
- **成功才标记**：清单中每一项要么不存在，要么删除成功，才写入当前版本号。有文件被占用而删除失败时记录警告、不写标记，下次启动重试。
- **清单随阶段增长**：每个阶段把自己不再使用的旧目录加入清单。某个旧目录必须在使用它的代码切换到新路径之后才能加入，否则旧代码会在下次启动时重新创建它。版本号变化后清理会重新触发，因此新加入的条目会在新版本首次启动时执行。
- 删除时去掉只读属性，并校验路径位于程序目录内。

| 旧位置 | 内容 | 加入清单的阶段 |
|---|---|---|
| `User/Temp` | 旧临时目录 | 1 |
| `Repos/Temp` | 旧仓库导入临时目录 | 1 |
| `Cache/`（程序根目录） | 旧推理编译缓存 `Cache/Inference`、各版本的空目录 `Cache/{版本号}/Model` | 2 |
| `Plugins/OnnxRuntime/.downloads` | 旧运行库安装包与未完成的下载 | 4 |
| `User/Cache/MemoryFileCache`，删除后 `User/Cache` 为空时一并删除 | 旧通用缓存 | 5 |
| `User/Music/Artwork` | 旧音乐封面缓存 | 5 |
| `WebView2Data/` | 旧网页数据，包括 `WebView2Data/CloudGame` 下的云原神实例 | 6 |

用户可感知的影响，需要写进对应版本的更新说明：

- 阶段 2：TensorRT、OpenVINO 的编译缓存被删除，升级后第一次识别需要重新编译，会变慢一次。
- 阶段 4：运行库未完成的下载进度被删除，需要重新下载。
- 阶段 6：旧的云原神网页版实例（含登录状态）和 HtmlMask 页面保存的网页数据被删除，需要重新创建实例并登录。

### 3.5 存储区域：统一的展示与清理登记

缓存、临时文件、下载、网页数据各有自己的服务，但界面需要一份统一的列表来展示占用和执行清理。各功能把自己的磁盘区域登记为"存储区域"，存储空间窗口只认这一种对象。新功能登记一个区域，就会自动出现在界面里。

```csharp
/// <summary>存储区域分组，决定在存储空间窗口中的分组和图标。</summary>
public enum StorageGroup { Cache, Download, WebData, Temp, Diagnostics, UserData }

/// <summary>清理时机。</summary>
public enum StorageCleanupTiming
{
    /// <summary>立即删除，正在使用的文件跳过。</summary>
    Immediate,
    /// <summary>文件被进程长期占用（推理缓存、网页数据），记录后在下次启动、使用方加载前删除。</summary>
    NextStartup,
    /// <summary>只展示占用，不提供清理，例如用户数据。</summary>
    None,
}

/// <summary>一块可在存储空间窗口展示和清理的磁盘区域。</summary>
public sealed record StorageArea(string Id, string DisplayName, StorageGroup Group, IReadOnlyList<string> Paths)
{
    /// <summary>这块空间存的是什么。</summary>
    public string Description { get; init; } = "";

    /// <summary>清理后用户会感受到什么，例如“下次打开地图时重新下载图标”。</summary>
    public string CleanupEffect { get; init; } = "";

    /// <summary>清理时机。</summary>
    public StorageCleanupTiming Timing { get; init; } = StorageCleanupTiming.Immediate;

    /// <summary>会丢失用户能感知的数据（登录状态、脚本网页设置、截图）时为 true，清理前需确认。</summary>
    public bool RequiresConfirmation { get; init; }

    /// <summary>是否包含在“一键清理”中。需要确认的区域不应包含。</summary>
    public bool IncludedInQuickCleanup { get; init; } = true;

    /// <summary>返回非空字符串表示当前不能清理及原因，例如“实例正在运行”。</summary>
    public Func<string?>? GetBusyReason { get; init; }

    /// <summary>自定义清理；为空时删除 Paths 下的全部内容。</summary>
    public Func<CancellationToken, Task<StorageCleanupResult>>? CleanupAsync { get; init; }
}

/// <summary>清理结果：释放的字节数、因占用跳过的文件数、是否推迟到下次启动。</summary>
public sealed record StorageCleanupResult(long FreedBytes, int SkippedFiles, bool Deferred);
```

```csharp
/// <summary>存储区域来源。各功能实现它来登记区域，可以返回动态数量的区域（如每个云原神实例一项）。</summary>
public interface IStorageAreaProvider
{
    IEnumerable<StorageArea> GetAreas();
}

/// <summary>统计和清理存储区域，供存储空间窗口和自动维护使用。</summary>
public interface IStorageService
{
    /// <summary>汇总所有 IStorageAreaProvider 登记的区域。</summary>
    IReadOnlyList<StorageArea> GetAreas();

    /// <summary>统计区域占用的字节数，在后台线程执行，可取消。</summary>
    Task<long> MeasureAsync(StorageArea area, CancellationToken ct);

    /// <summary>按区域的清理时机执行或登记清理。</summary>
    Task<StorageCleanupResult> CleanupAsync(StorageArea area, CancellationToken ct);
}
```

行为约定：

- 默认清理逐个删除文件，被占用的文件跳过并计数，不抛异常。删除时去掉只读属性（Git 生成的对象文件是只读的），复用 `DirectoryHelper.DeleteReadOnlyDirectory` 的处理方式。
- 删除前校验路径位于程序目录内，防止登记错误时删到程序目录之外。
- `NextStartup` 区域的路径写入 `Data/pending-cleanup.json`。主实例启动时，在创建推理会话和任何 WebView 之前执行这些清理。
- 统计结果只在窗口打开期间保留，不落盘。

各阶段登记的区域：

| 提供方 | 区域 | 阶段 |
|---|---|---|
| TempFileService | 每个临时文件分类一项 | 1 |
| ScriptRepoUpdater | 脚本更新备份 `User/Backup/ScriptUpdate`（需确认，不参与一键清理） | 1 |
| 推理模块 | 识别模型编译缓存 `Data/Cache/Inference`（NextStartup） | 2 |
| 日志与诊断 | 运行日志（只展示）、截图（需确认）、调试图片 `log/DebugMine`、`log/RewardRecognition`、`log/InventoryCountComparison` | 2 |
| 用户数据 | `User/`（只展示） | 2 |
| DownloadService | 每个下载分类一项 | 4 |
| FileCache | 每个缓存分区一项 | 5 |
| WebView | 每个普通环境的每个 Profile、已不再使用的环境目录、每个云原神实例 | 6 |

阶段 2 先上线存储空间窗口时，尚未重构的部分（现有 `MemoryFileCache` 目录、运行库安装包目录、云原神实例整目录）以临时区域登记，到对应阶段再替换为正式区域。阶段 6 完成后这些临时区域已全部替换，过渡用的 `LegacyStorageAreaProvider` 已删除。

云原神实例的区域需要判断实例是否在运行（`WebViewInstanceStore` 的命名互斥体），按依赖规则不能放在 Infrastructure，因此由 Service 层的 `CloudGameStorageAreaProvider` 登记。

### 3.6 依赖注册

按项目约定在 `App.xaml.cs` 注册，均为单例：

```csharp
// 存储与临时文件：临时目录按实例类型和进程号隔离
services.AddSingleton<IStorageService, StorageService>();
services.AddHostedService<LegacyDataCleaner>();
services.AddSingleton<ITempFileService>(sp => new TempFileService(
    InstanceBootstrap.Current.Context, sp.GetRequiredService<ILogger<TempFileService>>()));
services.AddSingleton<IStorageAreaProvider>(sp => (TempFileService)sp.GetRequiredService<ITempFileService>());

// 缓存：FileCache 同时登记各缓存分区为存储区域；维护服务只在主实例执行
services.AddSingleton(sp => new FileCache(AppPaths.FileCache, sp.GetRequiredService<TimeProvider>(),
    sp.GetRequiredService<ILogger<FileCache>>()));
services.AddSingleton<IFileCache>(sp => sp.GetRequiredService<FileCache>());
services.AddSingleton<IStorageAreaProvider>(sp => sp.GetRequiredService<FileCache>());
services.AddHostedService(sp => new CacheMaintenanceService(sp.GetRequiredService<FileCache>(), all.StorageConfig,
    instanceContext.IsRoot, sp.GetRequiredService<TimeProvider>(), sp.GetRequiredService<ILogger<CacheMaintenanceService>>()));
services.AddSingleton<IRemoteImageService, RemoteImageService>();

// 网络与下载（命名客户端注册见 6.3）
services.AddSingleton(sp => new DownloadService(
    sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientNames.Download), AppPaths.Downloads,
    sp.GetRequiredService<ILogger<DownloadService>>()));
services.AddSingleton<IDownloadService>(sp => sp.GetRequiredService<DownloadService>());
services.AddSingleton<IDownloadMonitor>(sp => sp.GetRequiredService<DownloadService>());
services.AddSingleton<IStorageAreaProvider>(sp => sp.GetRequiredService<DownloadService>());

// WebView2：环境入口同时登记软件内网页的 Profile；云原神实例的缓存由 Service 层的 CloudGameStorageAreaProvider 登记
services.AddSingleton(sp => new WebViewEnvironmentProvider(
    instanceContext.InstanceName, sp.GetRequiredService<ILogger<WebViewEnvironmentProvider>>()));
services.AddSingleton<IWebViewEnvironmentProvider>(sp => sp.GetRequiredService<WebViewEnvironmentProvider>());
services.AddSingleton<IStorageAreaProvider>(sp => sp.GetRequiredService<WebViewEnvironmentProvider>());
services.AddSingleton<IStorageAreaProvider, CloudGameStorageAreaProvider>();
```

## 4. 临时文件服务

### 4.1 目录与进程隔离

```text
Data/Temp/
  primary-12345/                         实例类型-进程号，一个进程一个目录
    .lease                               进程存活期间独占打开
    script-repo/20261028-153012-a1b2c3d4/   一次操作一个子目录
    notification/3f9a1c0e.png
  webview-23456/
  childsession-34567/
```

- **启动**：创建本进程目录，以 `FileShare.None` 打开 `.lease` 并持有到进程退出。
- **退出**：释放 `.lease`，删除本进程目录。取代 `App.OnExit` 和 `MainWindowViewModel` 中的 `TempManager.CleanUp()`。
- **残留回收**：启动后在后台扫描其他进程目录。能以独占方式打开 `.lease`（或 `.lease` 不存在），说明所属进程已结束，删除整个目录；打不开说明该进程仍在运行，跳过。
- 用文件句柄而不是进程号判断存活，不受进程号复用影响。进程被强制结束时操作系统会释放句柄，所以残留一定能被回收。
- 所有实例都执行回收。判断依据是文件锁，多个实例同时扫描也安全。

### 4.2 接口

```csharp
/// <summary>临时文件分类，决定子目录名和在存储空间窗口中的名称。</summary>
public sealed record TempCategory(string Id, string DisplayName);

/// <summary>已定义的临时文件分类。新功能在这里新增一项，即可按功能清理。</summary>
public static class TempCategories
{
    public static readonly TempCategory ScriptRepository = new("script-repo", "脚本仓库导入与更新");
    public static readonly TempCategory Notification = new("notification", "通知图片");

    /// <summary>全部分类，供存储区域登记。</summary>
    public static IReadOnlyList<TempCategory> All { get; } = [ScriptRepository, Notification];
}

/// <summary>临时文件服务。临时文件在进程退出时删除，进程异常结束后由下次启动回收。</summary>
public interface ITempFileService
{
    /// <summary>创建一个独占的临时目录，释放时删除。用于解压、克隆等一次性操作。</summary>
    TempScope CreateScope(TempCategory category);

    /// <summary>返回一个尚未创建的临时文件路径，文件随进程退出删除。用于交给外部组件读取的文件。</summary>
    string CreateFilePath(TempCategory category, string extension);

    /// <summary>清理某个分类：删除已结束进程的残留和本进程未在使用的文件，跳过其他运行中实例的目录。</summary>
    Task<StorageCleanupResult> CleanupAsync(TempCategory category, CancellationToken ct);
}

/// <summary>一次性临时目录。</summary>
public sealed class TempScope : IDisposable
{
    /// <summary>临时目录的完整路径，创建时已存在。</summary>
    public string DirectoryPath { get; }

    /// <summary>返回目录内的路径；拒绝绝对路径和越出目录的 ..。</summary>
    public string GetPath(string relativePath);

    /// <summary>删除目录（含只读文件）。失败只记警告，由下次启动回收。</summary>
    public void Dispose();
}
```

使用示例：

```csharp
using var temp = _tempFileService.CreateScope(TempCategories.ScriptRepository);
await Task.Run(() => ZipFile.ExtractToDirectory(zipFilePath, temp.DirectoryPath, true), ct);
// 校验通过后移入仓库目录。Data/Temp 与 Repos 在同一分区，Directory.Move 不会跨盘复制
Directory.Move(temp.GetPath(repoFolderName), targetPath);
```

本进程正在使用的作用域记录在服务内存中，`CleanupAsync` 会跳过它们。`TempScope` 只做同步删除，不需要 `IAsyncDisposable`。

### 4.3 不属于临时文件的情况

- **保护用户数据的备份**。订阅脚本更新前备份的 `savedFiles` 是用户数据，不能随临时目录被自动删除。改放到 `User/Backup/ScriptUpdate/{时间}/`，恢复成功后删除。启动时发现残留备份，说明上次更新中断：记录警告，并用 Toast 提示"上次脚本更新中断，已保留你的脚本数据备份"，附打开目录的操作。备份和恢复前校验脚本路径只能是相对路径且不含 `..`。
- **就地原子写入的 `.tmp`**（配置、任务存储、缓存文件）。它们必须和目标在同一目录才能原子替换，保留就地方式。新增 `AtomicFile` 帮助类，统一命名为 `.{文件名}.{8位随机}.tmp`，并提供 `CleanupStale(directory, olderThan)`，供各存储方在启动时清理自己的残留。`ConfigService`（PID 命名加过期扫描）和 `PuloniaTaskStore`（`File.Replace` 加 `.bak`）已有完整语义，本次不改；本次重构的 FileCache、BannerImageService 使用 `AtomicFile`。
- **同级发布用的暂存目录**（`~xxxxxxxx.update`、`.{engine}.staging-*`、`.{engine}.backup-*`）。它们需要与目标目录同级才能原子发布，由各自模块管理，不迁移。

### 4.4 迁移清单

| 现状 | 改为 |
|---|---|
| `TempManager.GetTempDirectory()` / `CleanUp()` | 删除该类，调用方改用 `ITempFileService`，启动和退出时的清理由服务生命周期负责 |
| `WindowsUwpNotifier` 的通知图片 | `CreateFilePath(TempCategories.Notification, ".png")` |
| `ScriptRepoUpdater.BackupScriptFiles` / `RestoreScriptFiles` | 改用 `User/Backup/ScriptUpdate`，加路径校验 |
| `ImportLocalRepoZipCore` 的 `Repos/Temp/importZipFile` | `CreateScope(TempCategories.ScriptRepository)` |
| 更换仓库地址时的 `{仓库目录}_temp_{8位}` | `CreateScope(TempCategories.ScriptRepository)` 后 `Directory.Move` |
| `ScriptRepoUpdater.ReposTempPath` | 删除常量 |

Windows 通知中心里的历史通知在进程退出后会失去配图，与现状一致。

## 5. 缓存服务

### 5.1 缓存分区

```csharp
/// <summary>缓存分区：一个功能的一类缓存。决定磁盘子目录、有效期和在存储空间窗口中的名称。</summary>
public sealed record CachePartition(string Id, string DisplayName, TimeSpan Ttl)
{
    /// <summary>清理后的影响，展示在存储空间窗口中。</summary>
    public string CleanupEffect { get; init; } = "需要时重新下载";
}

/// <summary>已定义的缓存分区。新功能在这里新增一项，即可使用缓存并出现在存储空间窗口中。</summary>
public static class CachePartitions
{
    public static readonly CachePartition MapIcons = new("map-icons", "地图点位图标", TimeSpan.FromDays(20));
    public static readonly CachePartition MapPointImages = new("map-point-images", "点位详情图片", TimeSpan.FromDays(7));
    public static readonly CachePartition KongyingTavern = new("kongying-tavern", "空荧酒馆点位数据", TimeSpan.FromDays(30));
    public static readonly CachePartition MusicArtwork = new("music-artwork", "音乐封面", TimeSpan.FromDays(180));
    public static readonly CachePartition MarkdownImages = new("markdown-images", "文档图片", TimeSpan.FromDays(7));
}
```

有效期沿用现值。点位详情图片和 Markdown 图片目前只在内存，7 天是新增的磁盘有效期。

### 5.2 接口

以 `MemoryFileCache` 为基础改造，保留它已经做对的部分：同一 key 只调用一次 factory；磁盘写入先写临时文件再替换；按文件最后写入时间判断过期；factory 收到的是服务自身的令牌，一个调用方取消不会中断其他调用方共享的下载，调用方的 `ct` 只取消等待。

实施时的调整：并发合并改用进行中任务的字典，内存层改用带总量上限的 `MemoryCache`（不再用 LazyCache，因为 LazyCache 的条目在调用 factory 之前就已创建，无法按内容大小决定是否放进内存）。接口只实现了目前有调用方的 `GetOrAddBytesAsync`、`Remove`、`RetainOnly`，下面的 JSON、自定义序列化和返回文件路径的版本等有需要时再加。

```csharp
/// <summary>文件缓存：内存加磁盘两级，适合可再生的图片和接口数据。</summary>
public interface IFileCache
{
    /// <summary>读取二进制缓存，未命中或过期时调用 factory 获取并写入。factory 返回 null 时不缓存。</summary>
    Task<byte[]?> GetOrAddBytesAsync(CachePartition partition, string key,
        Func<CancellationToken, Task<byte[]?>> factory, CancellationToken ct = default);

    /// <summary>读取 JSON 缓存，使用 Newtonsoft.Json 序列化。</summary>
    Task<T?> GetOrAddJsonAsync<T>(CachePartition partition, string key,
        Func<CancellationToken, Task<T?>> factory, CancellationToken ct = default);

    /// <summary>自定义序列化的版本，兼容已用 System.Text.Json 等方式序列化的现有调用方。</summary>
    Task<T?> GetOrAddAsync<T>(CachePartition partition, string key, Func<CancellationToken, Task<T?>> factory,
        Func<T, byte[]> serialize, Func<byte[], T?> deserialize, CancellationToken ct = default);

    /// <summary>读取二进制缓存并返回磁盘文件路径，供需要文件的控件（如 GIF）使用。</summary>
    Task<string?> GetOrAddFileAsync(CachePartition partition, string key,
        Func<CancellationToken, Task<byte[]?>> factory, CancellationToken ct = default);

    /// <summary>删除一条缓存。</summary>
    void Remove(CachePartition partition, string key);

    /// <summary>只保留给定的 key，删除分区内其他条目。取代 PurgeCacheTypeByCacheKeys。</summary>
    void RetainOnly(CachePartition partition, IReadOnlyCollection<string> keys);
}
```

存储细节：

- 文件路径为 `Data/Cache/Files/{分区Id}/{MD5(key)}.bin`。MD5 只用来生成文件名，不用于安全用途。
- 内存层只保存小于 256 KB 的条目，滑动过期 10 分钟。更大的条目只走磁盘，避免大图常驻内存。
- 多个实例共享该目录。写入通过 `AtomicFile` 完成；读取时遇到被占用或损坏的文件按未命中处理。

### 5.3 维护与容量上限

`CacheMaintenanceService` 是后台服务，只在主实例运行：

- 启动 2 分钟后执行，每 24 小时最多一次。上次执行时间记在 `Data/Cache/Files/.maintenance`，不写进 `config.json`，避免配置被频繁保存。
- 删除各分区中超过有效期的文件，以及代码里已不存在的分区目录（功能下线后的遗留）。
- 文件缓存总量超过 `StorageConfig.FileCacheLimitMb`（默认 1024）时，按最后写入时间从旧到新删除，直到降到上限的 80%。
- 推理编译缓存（`Data/Cache/Inference`）和下载目录（`Data/Downloads`）不计入这个上限。它们体积大、重建慢，由用户在存储空间窗口手动清理。

### 5.4 远程图片服务

```csharp
/// <summary>远程图片：下载经 IFileCache 落盘，解码后冻结，并放入有上限的内存缓存。</summary>
public interface IRemoteImageService
{
    /// <summary>内存命中时同步返回，供列表滚动和画布重绘时立即显示。</summary>
    bool TryGetCached(CachePartition partition, string url, out ImageSource? image);

    /// <summary>获取图片，失败返回 null。同一 URL 的并发请求只下载一次。</summary>
    Task<ImageSource?> GetAsync(CachePartition partition, string url, CancellationToken ct = default);

    /// <summary>获取图片原始数据（经文件缓存），供 GIF 等需要自行解码的控件使用；下载失败时抛出异常，便于界面说明原因。</summary>
    Task<byte[]?> GetBytesAsync(CachePartition partition, string url, CancellationToken ct = default);
}
```

- 下载使用 `HttpClientNames.Images` 客户端，单张上限 20 MB（与文档图片原有的限制一致）。
- 点位详情的 GIF 原先用内存流播放，改为文件路径需要换播放方式，因此提供 `GetBytesAsync` 而不是返回文件路径。
- 解码复用 `ImageSourceDecoder`，它已支持 WebP 并会冻结结果。
- 解码后的图片按 LRU 保留，默认最多 512 张。
- `MapIconImageCache` 保留为静态门面，因为画布控件依赖它的 `ImageUpdated` 事件；内部改为调用本服务，避免一次改动所有画布。

### 5.5 迁移清单

| 现状 | 改为 |
|---|---|
| `MemoryFileCache` | 删除，DI 注册改为 `IFileCache` |
| `MapIconImageCache`：自建 HttpClient、无上限字典、下载不可取消 | 静态门面加 `IRemoteImageService(MapIcons)` |
| `MaskMapPointInfoPopupViewModel`：静态 HttpClient | `IRemoteImageService(MapPointImages)`，原始数据走 `GetBytesAsync`，GIF 仍用内存流播放 |
| `MarkdownImageLoader` | `IRemoteImageService(MarkdownImages)` |
| `MusicCoverService` 的封面 | `IFileCache.GetOrAddBytesAsync(MusicArtwork)` |
| `KongyingTavernApiService` | `IFileCache.GetOrAddAsync(KongyingTavern)` 加 `RetainOnly` |
| `MaskMapPointService` 的 5 小时内存缓存 | 不变，纯内存且生命周期合理 |
| `BannerImageService` 的网络背景图 | 它是用户设置的文件，不是缓存，保留在 `User/Images`；下载改用下载服务 |
| 推理编译缓存 | 阶段 2 处理：`ResolvedInferenceRuntime.ModelCacheDirectory` 改为 `AppPaths.InferenceCache`，硬件加速页的"打开模型缓存目录"随之指向新目录；登记为存储区域 |
| 按版本的模型缓存 `Cache/{版本号}/Model` | 阶段 2 删除：去掉 `BgiOnnxModel` 的 `ModelCacheRelativePath`、`CacheRelativePath`、`CachePath` 和 `Register` 中创建目录的代码，`YoloPredictorFactory` 不再传入缓存路径；旧目录由旧目录清理删除 |

## 6. 网络：HTTP 客户端与代理

### 6.1 网络配置

```csharp
/// <summary>代理模式。</summary>
public enum NetworkProxyMode
{
    /// <summary>跟随系统代理，与改造前行为一致（默认）。</summary>
    System,
    /// <summary>不使用代理，全部直连。</summary>
    None,
    /// <summary>使用自定义代理地址。</summary>
    Custom,
}

/// <summary>网络设置，位于 AllConfig.NetworkConfig。</summary>
public partial class NetworkConfig : ObservableObject
{
    /// <summary>代理模式。</summary>
    [ObservableProperty] private NetworkProxyMode _proxyMode = NetworkProxyMode.System;

    /// <summary>自定义代理地址，支持 http:// 和 socks5://，例如 http://127.0.0.1:7890。</summary>
    [ObservableProperty] private string _proxyAddress = string.Empty;

    /// <summary>不走代理的地址，分号分隔，支持 * 通配。</summary>
    [ObservableProperty] private string _proxyBypassList = "localhost;127.*;10.*;192.168.*";
}
```

第一版不支持需要用户名密码的代理（本地代理工具通常不需要）。以后支持时，密码存入 Windows 凭据管理器（与 `MirrorChyanHelper` 保存 CDK 的方式相同），不写进 `config.json`。

### 6.2 代理如何生效

`AppWebProxy` 实现 `IWebProxy`，每次请求按当前配置决定目标地址是否走代理：

- System：转给启动时取得的系统代理，即 `HttpClient.DefaultProxy` 的初始值。
- None：直连。
- Custom：命中绕过列表时直连，否则使用自定义地址。地址无效时直连，并记录一次警告。

`SocketsHttpHandler` 每次请求都会询问 `IWebProxy`，所以修改设置后新请求立即生效，不需要重建 HttpClient。

启动时、在任何 HttpClient 发出请求之前，执行 `HttpClient.DefaultProxy = appWebProxy`。这样第三方 SDK（OpenAI、Anthropic 客户端）和尚未迁移的 `new HttpClient()` 也会跟随设置。显式设置了 `Proxy` 的客户端（Telegram 的独立代理）不受影响。

多实例：配置由所有实例共享，但各实例只在启动时读取配置（见能力边界文档的原则 5）。在一个实例里修改代理后，其他正在运行的实例重启后才生效。

### 6.3 命名客户端

使用 `IHttpClientFactory`。项目已引用 `Microsoft.AspNetCore.App` 框架，其中包含 `Microsoft.Extensions.Http`，不需要新增包。

```csharp
/// <summary>命名 HttpClient。业务代码通过 IHttpClientFactory.CreateClient(name) 获取。</summary>
public static class HttpClientNames
{
    /// <summary>BetterGI 自身服务：版本检查、公告、兑换码、更新日志。</summary>
    public const string Default = "bgi-default";
    /// <summary>游戏相关第三方接口：米游社、HoYoLab、空荧酒馆、日志解析。</summary>
    public const string GameApi = "bgi-game-api";
    /// <summary>图片：地图图标、点位图片、封面、文档图片。</summary>
    public const string Images = "bgi-images";
    /// <summary>下载服务专用，业务代码不直接使用。</summary>
    public const string Download = "bgi-download";
    /// <summary>通知推送渠道。</summary>
    public const string Notification = "bgi-notification";
    /// <summary>JS 脚本的 http.request。</summary>
    public const string Script = "bgi-script";
    /// <summary>AI 模型提供方，请求带密钥，禁止自动重定向。</summary>
    public const string AiProvider = "bgi-ai-provider";
}
```

| 客户端 | 超时 | UA | 其他 |
|---|---|---|---|
| Default | 30 秒 | `BetterGI/{版本}` | |
| GameApi | 30 秒 | 浏览器 UA | Referer 等由各服务按请求设置 |
| Images | 15 秒 | `BetterGI/{版本}` | |
| Download | 不限，由下载服务控制首包和停滞超时 | `BetterGI/{版本}` | 关闭自动解压缩，避免与续传的字节偏移冲突 |
| Notification | 30 秒 | `BetterGI/{版本}` | |
| Script | 60 秒 | 由脚本请求头决定 | 关闭自动重定向，由 `Http.cs` 逐跳校验允许列表，最多 5 跳 |
| AiProvider | 20 秒 | SDK 默认 | 关闭自动重定向，沿用 `AutoComboModelListFetcher` 现有配置 |

所有客户端共用同一种主处理器：`SocketsHttpHandler`，`Proxy = appWebProxy`，`AutomaticDecompression = All`（Download 除外），`PooledConnectionLifetime = 5 分钟`，`ConnectTimeout = 15 秒`。

`BgiHttpDefaultsHandler`（`DelegatingHandler`）补上默认 UA，并以 Debug 级别记录方法、主机、状态码和耗时。它不记录路径、查询参数和请求头，因为 Telegram 等渠道的令牌就在 URL 路径里。

不做全局自动重试。通知推送等 POST 请求重试可能导致重复发送；需要重试的下载由下载服务自己处理。

注册示例：

```csharp
// 代理：启动时接管默认代理，未迁移的 new HttpClient() 也跟随网络设置
var appWebProxy = new AppWebProxy(all.NetworkConfig, HttpClient.DefaultProxy);
HttpClient.DefaultProxy = appWebProxy;
services.AddSingleton(appWebProxy);
services.AddTransient<BgiHttpDefaultsHandler>();
services.AddHttpClient(HttpClientNames.Default, c => c.Timeout = TimeSpan.FromSeconds(30))
    .ConfigurePrimaryHttpMessageHandler(() => BgiHttpHandlers.Create(appWebProxy))
    .AddHttpMessageHandler<BgiHttpDefaultsHandler>();
// 其余命名客户端按上表同样注册
```

使用规则：

- 注入 `IHttpClientFactory`，每次操作调用 `CreateClient(name)`（开销很小），不把 HttpClient 存进静态字段。
- 无法注入的静态类通过 `App.GetService<IHttpClientFactory>()!.CreateClient(name)` 获取。
- 需要设置 `DefaultRequestHeaders`（例如登录后的 `Authorization`）的单例服务，可以在构造时创建一个客户端并长期持有。主处理器设置了 `PooledConnectionLifetime`，连接每 5 分钟重建，长期持有也能跟上 DNS 变化。
- 原 `Helpers/Http/HttpClientFactory` 已在迁移完成后删除。`ProxySpeedTester` 改用 Default 客户端。

### 6.4 HTTP 调用点迁移

| 调用点 | 客户端 |
|---|---|
| `UpdateService`（MirrorChyan、OSS notice、GitHub 更新日志） | Default |
| `MainWindowViewModel`、`FeedWindowViewModel` 的兑换码，`GetLiveRedeemCode` | Default |
| `MihoyoMapApiService`、`HoYoLabMapApiService`、`KongyingTavernApiService`、`YsHttp` | GameApi |
| `MapIconImageCache`、`MaskMapPointInfoPopupViewModel`、`MarkdownImageLoader`、`MusicCoverService` | Images（经 `IRemoteImageService` 或 `IFileCache`） |
| `OnnxRuntimeInstaller`、`BannerImageService` | Download（经下载服务） |
| `NotificationService` 共享客户端，Bark、xxtui、WechatClawbot、QqWebSocketHelper 的 HTTP 部分 | Notification |
| `TelegramNotifier` | 启用独立代理时保留自建客户端；未启用时使用 Notification |
| `Core/Script/Dependence/Http.cs` | Script；请求头改为写在 `HttpRequestMessage` 上 |
| `AutoComboModelListFetcher` | AiProvider |

### 6.5 HTTP 以外的连接

| 连接 | 处理 |
|---|---|
| LibGit2Sharp 克隆和拉取脚本仓库 | `GitProxy.Create(appWebProxy, repoUrl)` 生成 `ProxyOptions`：自定义代理模式且仓库地址不在绕过列表时使用代理；跟随系统和不使用代理两种模式保持 `ProxyType.None`，与现状一致 |
| `ClientWebSocket`（WebSocket 通知、QQ 绑定） | 设置 `Options.Proxy = appWebProxy` |
| MailKit SMTP | 不接入 |
| WebView2 页面 | 跟随系统代理，原因见 8.6 |
| `BetterGI.update.exe` | 独立进程，不受影响；如需支持，后续由更新程序增加代理参数 |

### 6.6 连接测试

`NetworkDiagnostics.TestAsync(ct)` 通过 Default 客户端并发请求几个固定目标（CNB、GitHub API、米游社），返回每个目标的结果和耗时，供设置页的"测试连接"使用。单个目标超时 8 秒。失败原因区分为超时、无法解析主机、代理拒绝连接、其他错误。

## 7. 下载服务与下载进度

### 7.1 职责划分

- `IDownloadService` 执行下载：断点续传、重试、校验、并发上限、落盘位置。
- `IDownloadMonitor` 保存本次运行中用户可见的下载任务，为下载中心提供列表、取消和重试。
- 图标、封面等小文件不走下载服务。它们由缓存层直接请求，并发请求在缓存层合并。

判断标准：文件大于约 1 MB，或需要进度、续传、校验时，使用下载服务。

### 7.2 接口

```csharp
/// <summary>下载分类：子目录名、存储空间窗口中的名称和说明。新分类加入 DownloadCategories.All。</summary>
public sealed record DownloadCategory(string Id, string DisplayName, string Description);

/// <summary>下载请求。Category 用于下载目录的子目录和存储空间窗口中的分组。</summary>
public sealed record DownloadRequest(Uri Url, string DisplayName, DownloadCategory Category)
{
    /// <summary>完成后的目标路径，必须位于程序目录内。为空时保留在 Data/Downloads 中，并返回该路径。</summary>
    public string? DestinationPath { get; init; }

    /// <summary>已知的文件大小，用于校验和提前检查磁盘空间。</summary>
    public long? ExpectedSize { get; init; }

    /// <summary>期望的哈希，支持 SHA256、SHA512。</summary>
    public DownloadHash? ExpectedHash { get; init; }

    /// <summary>允许的最大字节数，超过时中止。</summary>
    public long MaxBytes { get; init; } = long.MaxValue;

    /// <summary>备用地址，主地址失败后依次尝试。</summary>
    public IReadOnlyList<Uri> Mirrors { get; init; } = [];

    /// <summary>是否显示在下载中心。后台自动下载的小文件可以设为 false。</summary>
    public bool ShowInDownloadCenter { get; init; } = true;

    /// <summary>发起下载的位置，例如"硬件加速设置"。设置后表示下载完成还要由发起方继续处理（解压、安装），
    /// 下载中心不提供重试和打开文件夹，失败时提示用户回到这里重试。</summary>
    public string? Origin { get; init; }
}

/// <summary>下载哈希。</summary>
public sealed record DownloadHash(string Algorithm, string Value);

/// <summary>下载状态。</summary>
public enum DownloadState { Queued, Downloading, Verifying, Completed, Failed, Canceled }

/// <summary>下载进度快照。</summary>
public sealed record DownloadProgress(DownloadState State, long ReceivedBytes, long? TotalBytes,
    double BytesPerSecond, string? Error = null)
{
    /// <summary>完成百分比，总大小未知时为 null。</summary>
    public double? Percentage => TotalBytes > 0 ? ReceivedBytes * 100d / TotalBytes : null;

    /// <summary>预计剩余时间，总大小或速度未知时为 null。</summary>
    public TimeSpan? Remaining => TotalBytes > 0 && BytesPerSecond > 0
        ? TimeSpan.FromSeconds((TotalBytes.Value - ReceivedBytes) / BytesPerSecond)
        : null;
}
```

```csharp
/// <summary>一个下载任务。</summary>
public sealed class DownloadTask
{
    public Guid Id { get; }
    public DownloadRequest Request { get; }

    /// <summary>最新进度。</summary>
    public DownloadProgress Progress { get; }

    /// <summary>进度变化，约每 150 毫秒一次，在后台线程触发。</summary>
    public event EventHandler<DownloadProgress>? ProgressChanged;

    /// <summary>完成后返回文件路径；失败或取消时抛出异常。</summary>
    public Task<string> Completion { get; }

    /// <summary>取消下载，已下载部分保留，可续传。</summary>
    public void Cancel();
}

/// <summary>下载服务。</summary>
public interface IDownloadService
{
    /// <summary>开始下载并立即返回任务。ct 取消时取消下载。</summary>
    DownloadTask Start(DownloadRequest request, CancellationToken ct = default);

    /// <summary>分类的下载目录，完成的文件和未完成的 .part 都在这里。</summary>
    string GetDirectory(DownloadCategory category);
}

/// <summary>下载监视器：本次运行中显示在下载中心的任务。</summary>
public interface IDownloadMonitor
{
    IReadOnlyList<DownloadTask> Tasks { get; }

    /// <summary>任务增减或状态变化，在后台线程触发。</summary>
    event EventHandler? Changed;

    /// <summary>用原请求重新下载，已下载部分会续传。</summary>
    DownloadTask Retry(Guid id);

    /// <summary>移除已完成、失败和已取消的任务。</summary>
    void ClearFinished();
}
```

调用示例（硬件加速页安装运行库）：

```csharp
var task = _downloadService.Start(new DownloadRequest(package.Url, package.Name, DownloadCategories.OnnxRuntime)
{
    ExpectedSize = package.DownloadBytes,
    ExpectedHash = package.Hash is null ? null : new DownloadHash(package.HashAlgorithm, package.Hash),
    Origin = "硬件加速设置",
}, ct);
// 页面继续显示自己的进度条，同一任务也出现在下载中心
task.ProgressChanged += (_, p) =>
    progress?.Report(new($"正在下载 {package.Name}", p.ReceivedBytes, p.TotalBytes ?? 0));
var archivePath = await task.Completion;
```

第一版不合并重复的下载请求。高频小文件的并发合并已在缓存层完成，大文件下载通常由单一功能发起。

### 7.3 下载流程

从 `RuntimePackageDownloader.DownloadAsync` 抽取并补充：

1. 下载文件为 `Data/Downloads/{分类}/{SHA256(URL)}`。已有完整文件且校验通过时直接复用。
2. 存在 `.part` 时发送 `Range` 续传。服务器返回 206 时校验 `Content-Range` 的起点；返回 200 时从头写。
3. 等待响应头最多 30 秒。读取过程中 60 秒没有收到数据视为停滞，按可重试错误处理。现状是读取阶段不限时，网络卡住时会一直等待。
4. 网络错误和 5xx 最多重试 3 次，间隔 2、5、15 秒，每次从已下载位置续传。4xx 不重试，直接换下一个镜像。
5. 写入 `.part`，约每 150 毫秒报告一次进度，速度按最近 3 秒计算。
6. 校验大小和哈希。失败时删除 `.part`，错误信息为"文件校验失败，请重新下载"。
7. 指定了目标路径时移动到目标位置（同一分区用 `File.Move`，否则复制后删除），否则留在 `Data/Downloads`。
8. 全局最多 3 个任务同时下载，其余排队。
9. 已知大小时先检查磁盘剩余空间，复用 `OnnxRuntimeInstaller` 现有的检查。
10. 取消或失败时保留 `.part`，下次可以续传。存储空间窗口会标出"含未完成的下载"。

文件名只由服务决定，不使用服务器的 `Content-Disposition`。ZIP 的 CRC 校验和解压（`RuntimePackageDownloader.ExtractAsync`）属于运行库安装逻辑，留在 ONNX 模块。

### 7.4 非 HTTP 传输（可选）

脚本仓库的 Git 拉取不经过 HttpClient，但用户同样关心它的进度。`IDownloadMonitor.TrackExternal(displayName, category)` 返回一个可报告进度的句柄，让 `CloneRepository` 的 `OnTransferProgress` 也显示在下载中心。这一项放在阶段 4 末尾，视工作量决定是否纳入。阶段 4 未纳入：仓库的克隆、拉取和 Pulonia 更新各有进度入口，需要逐个接入，留到以后单独处理。

### 7.5 迁移清单

| 现状 | 改为 |
|---|---|
| `RuntimePackageDownloader.DownloadAsync` 与 `VerifyHashAsync` | 由 `DownloadService` 取代 |
| `OnnxRuntimeInstaller` 自建 HttpClient | 注入 `IDownloadService`，安装包目录改为 `Data/Downloads/onnxruntime`；旧的 `Plugins/OnnxRuntime/.downloads` 由旧目录清理删除 |
| `BannerImageService` | `DownloadService`（`MaxBytes` 20 MB，`ShowInDownloadCenter = false`，目标为临时文件）；保留 ImageSharp 校验和丢弃过期操作的逻辑，校验通过后移入 `User/Images` |
| `ScriptRepoUpdater.DownloadRepoAndUnzip` 与静态 `_httpClient` | 删除，没有活跃调用 |

## 8. WebView2 统一管理

### 8.1 环境分类

WebView2 的"环境"对应一个用户数据目录和一组固定的浏览器参数，同一环境下可以有多个 Profile 隔离 Cookie 和网页存储。本设计把环境分为两类：

| | 普通环境 | 云原神环境 |
|---|---|---|
| 使用者 | 更新日志、脚本仓库、地图路径编辑器、脚本 HTML 遮罩，以及以后新增的普通网页 | 云原神网页版实例 |
| 数量 | 目前 1 个（`App`），以后可以按需新增 | 每个实例一个 |
| 定义位置 | `WebViewEnvironments` 中的静态定义 | 环境入口内部，按实例名生成 |
| 所在进程 | 任意实例进程，每个进程每个环境一个对象 | 只在同名的网页版实例进程中 |
| 用户数据目录 | `Data/WebView2/{环境 Id}/` | `Data/WebView2/CloudGame/{实例名}/` |
| 浏览器参数 | 写在定义里的常量，`App` 环境为空 | 禁用遮挡检测和后台降频（现有 4 项） |
| 数据隔离 | 环境之间目录隔离；环境内按 Profile 隔离 | 每个实例一个目录 |
| 宿主方式 | WPF `WebView2` 控件 | 窗口句柄直接托管 `CoreWebView2Controller`，固定 1920x1080、缩放 1 |
| 代理 | 跟随系统 | 跟随系统 |
| 清理 | 清理缓存（保留网页数据），或清除某个 Profile 的全部数据 | 实例未运行时清理缓存（保留登录），或删除实例 |

浏览器参数必须是定义里的常量，不能读取用户配置。WebView2 要求同一用户数据目录的所有环境参数一致，否则后创建的环境会初始化失败。主实例、桌面分身和云原神实例进程可能同时使用同一个普通环境目录，参数随配置变化就会出现这种失败。

"统一管理"落在四件事上：只有环境入口能创建环境；所有普通环境的配置集中写在一个定义类里；普通网页只通过 `WebViewSetup` 初始化，套用同一套安全设置和异常处理；网页数据统一登记为存储区域。

### 8.2 普通环境的定义与配置管理

所有普通环境的配置写在一个定义类里，这是查看和修改普通环境配置的唯一位置：

```csharp
/// <summary>普通 WebView2 环境的定义：一个用户数据目录和它固定不变的配置。</summary>
public sealed record WebViewEnvironmentDefinition(string Id, string DisplayName)
{
    /// <summary>浏览器参数。必须是常量，不能读取用户配置，见 8.1。</summary>
    public string BrowserArguments { get; init; } = "";

    /// <summary>用户数据目录，由 Id 决定，不允许单独指定。</summary>
    public string DataFolder => Path.Combine(AppPaths.WebView, Id);
}

/// <summary>已定义的普通环境。新增环境只在这里加一项。</summary>
public static class WebViewEnvironments
{
    /// <summary>软件内网页的默认环境：更新日志、脚本仓库、地图路径编辑器、脚本遮罩。</summary>
    public static readonly WebViewEnvironmentDefinition App = new("App", "软件内网页");

    /// <summary>全部普通环境，供环境入口校验、存储区域登记和清理不再使用的目录。</summary>
    public static IReadOnlyList<WebViewEnvironmentDefinition> All { get; } = [App];
}

/// <summary>普通环境中的 Profile，用于在同一环境内隔离 Cookie 和网页存储。</summary>
public sealed record WebViewProfile(WebViewEnvironmentDefinition Environment, string ProfileName, string DisplayName);

/// <summary>已定义的 Profile。需要独立登录态或存储的新功能在这里新增一项。</summary>
public static class WebViewProfiles
{
    /// <summary>软件内网页：更新日志、脚本仓库、地图路径编辑器。</summary>
    public static readonly WebViewProfile Default = new(WebViewEnvironments.App, "Default", "软件内网页");

    /// <summary>脚本 HTML 遮罩，与其他网页隔离。</summary>
    public static readonly WebViewProfile HtmlMask = new(WebViewEnvironments.App, "HtmlMask", "脚本遮罩网页");

    /// <summary>全部 Profile，供存储区域登记。</summary>
    public static IReadOnlyList<WebViewProfile> All { get; } = [Default, HtmlMask];
}
```

何时新增 Profile、何时新增环境：

- 只需要隔离登录状态和网页存储时，新增 Profile。同一环境共用一个浏览器进程，占用更少。
- 需要不同的浏览器参数，或者希望数据和浏览器进程完全分开时，新增环境。
- 新增环境只需在 `WebViewEnvironments` 加一项并放进 `All`。调用方通过它的 Profile 使用，不需要改环境入口。

环境入口对这些定义做统一管理：

- 启动时校验：Id 唯一，且只能是字母、数字和短横线；Id 不能是 `CloudGame`。
- 每个进程、每个环境只创建一次环境对象，之后复用。
- 创建时在日志中记录环境 Id、数据目录、浏览器参数和运行时版本，排查问题时可以直接对照。
- 存储空间窗口按 `WebViewProfiles.All` 为每个 Profile 生成一个存储区域，按所属环境分组显示。
- 每个 Profile 都必须属于 `WebViewEnvironments.All` 中的环境，启动校验会检查这一点。
- `Data/WebView2/` 下既不在 `All` 中、也不是 `CloudGame` 的目录，视为已删除环境的遗留数据，在存储空间窗口中显示为"已不再使用的网页数据"，可以直接清理。

### 8.3 接口

```csharp
/// <summary>WebView2 环境的唯一创建入口。</summary>
public interface IWebViewEnvironmentProvider
{
    /// <summary>获取普通环境，每个进程每个环境只创建一次。</summary>
    Task<CoreWebView2Environment> GetAsync(WebViewEnvironmentDefinition environment);

    /// <summary>云原神实例的环境。只能在同名的网页版实例进程中调用，否则抛出异常。</summary>
    Task<CoreWebView2Environment> CreateCloudGameAsync(string instanceName);

    /// <summary>记录网页进程异常退出，供 WebViewSetup 和云原神宿主窗口统一写日志。</summary>
    void ReportProcessFailed(string source, CoreWebView2ProcessFailedEventArgs e);
}
```

```csharp
/// <summary>普通网页的初始化选项。</summary>
public sealed record WebViewOptions
{
    /// <summary>页面打开新窗口时交给系统浏览器（仅 http、https），为 false 时拒绝。</summary>
    public bool OpenNewWindowInBrowser { get; init; } = true;

    /// <summary>页面下载的保存目录；为空时拒绝页面发起的下载。</summary>
    public string? DownloadFolder { get; init; }

    /// <summary>网页进程异常退出且自动恢复无效时调用，由界面显示说明和重新加载入口。Infrastructure 不创建界面。</summary>
    public Action<CoreWebView2ProcessFailedKind>? ProcessCrashed { get; init; }
}

/// <summary>WPF WebView2 控件的统一初始化。</summary>
public static class WebViewSetup
{
    /// <summary>用 Profile 所属的普通环境初始化控件，并套用通用设置和事件处理。</summary>
    public static Task InitializeAsync(this WebView2 webView, IWebViewEnvironmentProvider provider,
        WebViewProfile profile, WebViewOptions? options = null);
}
```

`WebViewSetup` 统一处理：

- 设置：开发者工具只在 Debug 下开启，关闭状态栏。
- `NewWindowRequested`：按 `OpenNewWindowInBrowser` 交给系统浏览器（`UseShellExecute = true`）或拒绝。现状只有脚本仓库页面处理了它。
- `DownloadStarting`：没有下载目录时取消；有目录时保存到该目录。替代 `WebpagePanel` 中对 `DefaultDownloadFolderPath` 的设置。
- `ProcessFailed`：记录日志。渲染进程崩溃时自动重新加载一次；再次崩溃时在页面位置显示"网页进程异常退出"和"重新加载"按钮。
- 把 `CoreWebView2` 以弱引用登记到环境入口，供清理网页数据时使用（见 8.5）。

各使用方保留自己的特有逻辑：脚本仓库的虚拟主机映射和 HostObject、HtmlMask 的资源请求拦截和消息桥、地图编辑器的文件访问桥。`WebpagePanel` 改为内部调用 `WebViewSetup`，外部用法不变。

云原神：`CloudWebHostWindow.OnLoaded` 改为调用 `CreateCloudGameAsync`，浏览器参数常量连同现有注释移入环境入口。Controller 的创建和固定尺寸配置仍由宿主窗口负责，它们属于云原神运行环境。宿主窗口另外处理 `ProcessFailed`：标记为未就绪、停止截图，并提示用户重新加载页面，不再只靠就绪轮询发现。

### 8.4 运行时检测

- `WebViewRuntime.GetVersion()`（Infrastructure）：返回已安装的版本，未安装时返回 null。
- `WebViewRuntimePrompt.EnsureAvailableAsync()`（View 层）：未安装时用 `ThemedMessageBox` 说明"该功能需要 Microsoft Edge WebView2 运行时"，提供"前往下载"（以 `UseShellExecute = true` 打开官方下载页）和"取消"。

| 位置 | 检测时机 |
|---|---|
| `WebpagePanel` | 构造时；保留内嵌按钮，修复打不开链接的问题 |
| `HtmlMaskWindow` | 创建窗口前；不可用时记录日志并用 Toast 提示，不再静默关闭 |
| `CloudWebInstanceWindowViewModel` | 新建或打开实例前，避免启动一个注定失败的进程 |
| `CloudWebHostWindow` | 保留现有的初始化失败提示 |

### 8.5 网页数据清理

| 存储区域 | 清理方式 | 确认 | 一键清理 |
|---|---|---|---|
| 软件内网页缓存（`Default` Profile） | 本进程有存活的 WebView 时调用 `Profile.ClearBrowsingDataAsync(DiskCache \| CacheStorage)`，同时登记下次启动删除缓存子目录 | 否 | 是 |
| 脚本遮罩网页数据（`HtmlMask` Profile） | 清除该 Profile 的全部数据，脚本保存在网页中的设置会丢失 | 是 | 否 |
| 以后新增的 Profile | 由 Profile 定义说明清理影响，默认按"只清缓存"处理 | 按定义 | 按定义 |
| 已不再使用的网页数据 | 删除不在定义中的环境目录 | 否 | 是 |
| 云原神 · {实例名} | 实例未运行时删除该实例目录下的缓存子目录，保留 Cookie 和登录状态；运行中时按钮不可用，并说明"请先关闭该实例" | 否 | 否 |

开发环境中已观察到云原神实例的缓存位于 `{实例目录}/EBWebView/Default/Cache/Cache_Data`。普通环境多 Profile 时各 Profile 子目录的位置、`Code Cache` 和 `GPUCache` 的位置，需要实测确认后再写入代码。删除实例（含登录状态）沿用现有流程。云原神实例的数据区域只在主实例中登记，与"子实例不操作其他实例"的边界一致。

实施结果（阶段 6）：

- 已实测确认目录：默认 Profile 为 `EBWebView/Default`，命名 Profile 为 `EBWebView/WV2Profile_{小写名称}`（与 `CoreWebView2Profile.ProfilePath` 一致）。缓存子目录为 `Cache`、`Code Cache`、`GPUCache`、`DawnGraphiteCache`、`DawnWebGPUCache`；登录状态在 `Network/Cookies`，网页存储在 `Local Storage`、`IndexedDB`，都不在清理范围内。
- 软件内网页缓存和脚本遮罩数据都登记为"下次启动时清理"：网页进程长期占用这些文件，并且多个实例进程可能共用 `Data/WebView2/App`。没有实现"本进程有存活 WebView 时调用 `ClearBrowsingDataAsync`"，因此也不需要把 `CoreWebView2` 弱引用登记到环境入口。
- 云原神实例在未运行时立即清理缓存目录；存储空间窗口和云原神实例管理窗口（"更多操作"菜单中的"清理缓存（保留登录）"）都提供入口。实例行没有增加数据大小，占用在存储空间窗口查看。

### 8.6 代理

WebView2 页面第一版跟随系统代理，不接入自定义代理：

- WebView2 的代理只能通过浏览器参数设置，而浏览器参数必须是定义里的常量，并在所有进程中保持一致。多实例运行时修改代理，会让后启动的进程初始化失败。
- 云原神是实时串流，经代理会增加延迟。

设置页会明确写出这一范围（见 9.2）。

### 8.7 其他

- 数据目录统一由 `WebViewEnvironmentDefinition.DataFolder` 和 `AppPaths.WebViewCloudGame` 提供，修复 dotnet 宿主下的目录错误。`WebViewInstanceStore.Root` 改为 `AppPaths.WebViewCloudGame`。
- `EnsureWebView2DataFolder` 给 Everyone 授予完全控制权限，建议改为只授予当前用户。需要先确认当初要解决的场景（推测是程序装在 C 盘并以管理员身份运行）。
- 桌面分身运行在另一个 Windows 会话，与主实例共用 `Data/WebView2/App/` 是否冲突需要实测。如果冲突，由环境入口为桌面分身改用 `Data/WebView2/App-Session{会话号}/`，调用方无需改动。
- `WebpagePanel.Dispose` 没有调用方。需要确认所在窗口关闭时 WPF 控件是否自动释放；如果不会，由 `WebpageWindow` 在关闭时释放。

实施结果（阶段 6）：数据目录统一为 `Data/WebView2/App` 与 `Data/WebView2/CloudGame`；`EnsureWebView2DataFolder` 的 Everyone 授权已删除（程序以管理员运行，网页进程与程序为同一用户，不需要额外授权），如有用户反馈目录无法写入再按"只授予当前用户"处理；`WebpageWindow` 关闭时释放面板；桌面分身仍与主实例共用 `Data/WebView2/App`，是否冲突留待实测。

## 9. 界面与交互

### 9.1 设计原则

- 先说明"这是什么、清理后会怎样"，再给按钮。每个存储区域都有一句说明和一句清理影响。
- 可再生的缓存直接清理，不弹确认。会丢失登录状态、脚本网页设置、截图的操作必须确认，并写明会丢失什么。
- 正在使用的文件自动跳过，不报错。结果用一句话告诉用户：释放了多少、跳过了多少、哪些会在下次启动时完成。
- 统计和清理都在后台进行，界面不卡顿，窗口可以随时关闭。
- 下载进度在发起下载的页面显示，同时在全局下载中心显示，离开页面也能看到。
- 默认值保持现有行为：跟随系统代理，不自动删除截图。
- 不可用的操作把原因直接写在说明文字里，不只放在 ToolTip 中。

### 9.2 设置页：网络与代理

位置：`CommonSettingsPage` 中"本机外部访问服务"卡片之后，新增一个 `ui:CardExpander`。内容做成子视图 `NetworkSettingsView`，参照 `HardwareAccelerationView` 的做法，避免设置页继续变长。

```text
[图标] 网络与代理                                            跟随系统代理
       软件联网时使用的代理，修改后对新请求立即生效
  ────────────────────────────────────────────────────────────
  代理模式                                         [跟随系统代理    ▾]
  系统代理即 Windows 设置中的代理
  ────────────────────────────────────────────────────────────
  代理地址                              [http://127.0.0.1:7890        ]   ← 仅自定义模式
  支持 http:// 和 socks5://
  ⚠ 地址格式不正确，示例：http://127.0.0.1:7890
  ────────────────────────────────────────────────────────────
  不使用代理的地址                      [localhost;127.*;192.168.*    ]   ← 仅自定义模式
  分号分隔，支持 * 通配。国内服务可以加入这里以直连
  ────────────────────────────────────────────────────────────
  测试连接                                                  [测试连接]
  ✓ CNB 45 ms    ✓ GitHub 230 ms    ✗ 米游社：连接超时
  ────────────────────────────────────────────────────────────
  ⓘ 生效范围：软件内的网络请求、通知推送、JS 脚本请求；自定义代理模式下
    也用于脚本仓库的 Git 拉取。网页窗口和云原神网页版跟随系统代理，
    更新程序不受影响。其他已打开的 BetterGI 实例需要重启后生效。
```

交互：

- 卡片标题右侧显示当前模式，折叠时也能看到。
- 代理模式下拉框有三项：跟随系统代理（默认）、不使用代理、自定义代理。选择"自定义代理"时才显示地址和绕过列表两行。
- 地址输入框失去焦点时校验，合法才写入配置。不合法时在输入框下方显示提示，配置保留上一次的合法值，避免输错一个字符就让所有请求失败。
- 测试连接时按钮显示进度环并禁用，结果逐项显示，失败项写明原因。
- 通知设置页的 Telegram 代理项增加一行说明："关闭时使用「设置 → 网络与代理」"。

### 9.3 存储空间窗口

入口：`CommonSettingsPage` 新增 `ui:CardControl`「存储空间」。说明文字显示"缓存、下载、网页数据和临时文件共占用 3.2 GB"，页面打开后在后台计算，计算期间显示"正在计算…"。右侧"管理"按钮打开 `StorageManagementWindow`。

使用独立窗口而不放在设置页内展开：内容是十几行带统计和操作的列表，还有总览和自动清理设置，放进已经很长的设置页不便浏览；项目已有同类的管理窗口（脚本仓库、云原神实例管理）。窗口启用主题背景。

```text
存储空间
数据目录 E:\BetterGI\Data                           [打开目录]  [重新计算]

  ████████████████▓▓▓▓▓▓▓▓▒▒▒▒░░                       共 4.8 GB
  ■ 缓存 1.2 GB   ■ 下载 2.1 GB   ■ 网页数据 0.9 GB
  ■ 临时文件 0.2 GB   ■ 日志与诊断 0.4 GB
  可安全清理 1.6 GB，不会删除脚本、配置、截图和登录状态       [一键清理]

缓存 · 删除后会在需要时重新下载或生成
  地图点位图标        打开地图时重新下载                  23 MB  [清理]
  空荧酒馆点位数据    下次加载点位时重新获取               5 MB  [清理]
  识别模型编译缓存    下次启动后重新编译，首次识别会变慢  612 MB  [清理]

下载 · 安装包和未完成的下载
  运行库安装包        含 1 个未完成的下载，可继续         1.9 GB  [清理]

网页数据
  软件内网页 · 网页缓存      保留网页登录与设置          120 MB  [清理]
  软件内网页 · 脚本遮罩数据  会清除脚本保存在网页中的设置   8 MB  [清除…]
  已不再使用的网页数据       旧功能留下的网页目录          15 MB  [清理]
  云原神 · 小号A      保留登录状态。运行中，请先关闭该实例 340 MB  [清理]（禁用）

临时文件
  脚本仓库导入与更新  正在使用的文件会跳过                  0 B  [清理]
  通知图片                                                2 MB  [清理]

日志与诊断
  运行日志            自动保留最近 21 天                  85 MB  [打开]
  截图                你用快捷键保存的截图               300 MB  [打开] [清除…]
  调试图片            任务排查用的识别截图                40 MB  [打开] [清理]

用户数据 · 不会被清理
  脚本、配置、录制等                                     1.1 GB  [打开]
  脚本更新备份        上次更新中断时保留的文件              0 B  [打开] [清除…]

自动清理
  文件缓存上限        超过后自动删除最早的缓存          [1 GB        ▾]
```

交互：

- 打开窗口后各行并行统计，未完成的行显示进度环。总览条在全部统计完成后显示，"可安全清理"的数值随统计更新。关闭窗口会取消统计。
- 行内"清理"：可再生内容直接执行，按钮变为进度环；完成后刷新大小，窗口底部的 Snackbar 显示"已释放 23 MB"。有文件被跳过时补充"3 个文件正在使用，已跳过"。
- 带"…"的按钮先弹确认框，写明会丢失什么。
- 下次启动才清理的区域，点击后大小旁标注"待清理"，Snackbar 显示"将在下次启动时清理"。
- 一键清理弹出确认框，列出将清理的项目和预计释放的空间，并写明不会删除的内容。只包含 `IncludedInQuickCleanup` 为 true 的区域。
- 不能清理时按钮禁用，原因直接显示在说明文字中。
- 缓存上限下拉框：256 MB、512 MB、1 GB（默认）、2 GB、5 GB。
- 每个分组按占用从大到小排序，帮助用户先看到大头。
- 网页数据按"环境 · Profile"命名，以后新增普通环境时自动多出对应的行；"已不再使用的网页数据"只在存在遗留目录时显示。
- 用户数据和日志不在 `Data/` 下，窗口顶部只显示数据目录，这两组的"打开"按钮各自打开对应目录。
- 桌面分身和云原神实例也能打开这个窗口，统计的是同一个程序目录；云原神实例的数据行只在主实例中显示。

### 9.4 下载中心

入口：主窗口标题栏右侧，子实例入口和窗口按钮之间，新增一个下载按钮。本次运行没有下载任务时不显示。

```text
标题栏：  …  [⤓]  [—] [□] [×]
          下载中：图标外圈显示总进度；有失败任务时显示红点

下载                                                    [清除已完成]
  TensorRT 运行库
  ██████████░░░░░░░░  612 MB / 1.8 GB · 5.2 MB/s · 剩余 4 分钟    [取消]
  ──────────────────────────────────────────────────────────
  cuDNN 运行库                                              排队中 [取消]
  ──────────────────────────────────────────────────────────
  脚本仓库更新            拉取对象 3200 / 5000
  ──────────────────────────────────────────────────────────
  ONNX Runtime            ✗ 文件校验失败，请重新下载             [重试]
  ──────────────────────────────────────────────────────────
  自定义背景图片          ✓ 已完成                     [打开所在文件夹]
```

- 弹出层使用 WPF-UI 的 `Flyout`，内容由 `DownloadCenterViewModel` 提供。ViewModel 订阅 `IDownloadMonitor.Changed`，节流后切到 UI 线程刷新。
- 下载失败时额外弹 Toast 说明原因；下载完成只在列表中标记，不打扰用户。
- 退出程序时仍有下载在进行，提示"有 1 个下载正在进行，退出后下次可以继续下载"，提供"退出"和"取消"。
- 发起下载的页面（如硬件加速）保留原有进度条，两处显示的是同一个任务。

### 9.5 云原神实例管理窗口

实例列表每行增加数据大小和"清理缓存（保留登录）"按钮，实例运行时禁用并写明原因。删除实例的确认文字强调"会清除该实例的登录状态"。

阶段 6 的实现：在每行的"更多操作"菜单中加入"清理缓存（保留登录）"，与删除相同只在实例未运行时可用，提示文字说明原因，完成后提示释放的空间；数据大小暂未加入行内，在存储空间窗口查看。

### 9.6 MVVM 落点

| 视图 | ViewModel | 说明 |
|---|---|---|
| `NetworkSettingsView`（子视图） | `NetworkSettingsViewModel` | 绑定 `NetworkConfig`；地址校验，测试连接命令 |
| `StorageManagementWindow` | `StorageManagementViewModel`、`StorageAreaItemViewModel` | 行项由 `IStorageService` 生成；统计与清理是返回 `Task` 的 `[RelayCommand]` |
| 标题栏下载按钮与 Flyout | `DownloadCenterViewModel`（单例） | 把 `DownloadTask` 包装为可观察的行项 |

遵守项目约定：只读和展示用的绑定显式写 `Mode=OneWay`，尤其是 `Run.Text`；子视图用到的 `StaticResource` 必须能在本视图或明确合并的字典中解析；局部样式用 `BasedOn` 继承 WPF-UI 默认样式；新窗口启用主题背景；对话框使用 `ThemedMessageBox`；交互优先用 Behaviors。

## 10. 配置项

| 配置 | 默认值 | 说明 |
|---|---|---|
| `NetworkConfig.ProxyMode` | `System` | 与改造前一致 |
| `NetworkConfig.ProxyAddress` | 空 | 只在 Custom 模式生效 |
| `NetworkConfig.ProxyBypassList` | `localhost;127.*;10.*;192.168.*` | 只在 Custom 模式生效 |
| `StorageConfig.FileCacheLimitMb` | `1024` | 文件缓存总量上限 |

两个配置类都挂在 `AllConfig` 上，由现有 `ConfigService` 保存到 `User/config.json`。`NotificationConfig` 中的 Telegram 代理配置保留，作为 Telegram 渠道的单独代理。

## 11. 不纳入统一的部分

| 内容 | 原因 |
|---|---|
| `BetterGI.update.exe` 的下载 | 独立程序，主程序启动它后即退出 |
| LibGit2Sharp 传输 | 自带网络栈，只接入代理映射（6.5） |
| MailKit SMTP | 代理需求少，且 MailKit 的代理配置方式不同 |
| 外部访问服务（Kestrel） | 入站服务，不是对外请求 |
| WebView2 页面流量 | 跟随系统代理（8.6） |
| 识别模块的进程内缓存（`RecognitionAssets`、`AssetsCache`、模板 Mat 等） | 不占磁盘，生命周期由识别模块负责 |
| `MaskMapPointService` 的 5 小时内存缓存 | 纯内存，生命周期合理 |
| 同目录原子写入和同级发布暂存目录 | 必须与目标同目录才能原子替换（4.3） |
| `User/` 下的用户数据 | 只展示占用，不提供清理 |
| `ProxySpeedTester`（GitHub 镜像测速） | 没有活跃调用；镜像加速和代理是两个不同功能 |

## 12. 安全

- 代理地址只接受 http、https、socks4、socks5 协议和合法的主机端口。第一版不保存代理密码；以后支持时存入 Windows 凭据管理器。
- HTTP 日志只记录方法、主机、状态码和耗时，不记录路径、查询参数和请求头。
- 下载的目标路径必须位于程序目录内，文件名由服务生成；支持大小上限和哈希校验。
- 存储清理只删除已登记、且位于程序目录内的路径。
- `TempScope.GetPath` 和脚本更新备份拒绝绝对路径和 `..`。
- JS 脚本 HTTP 关闭自动重定向，每一跳重新校验允许列表。
- WebView 的新窗口只把 http、https 地址交给系统浏览器；HtmlMask 的资源请求拦截保持不变；开发者工具只在 Debug 下开启。

## 13. 实施计划

阶段按依赖和风险排序，每个阶段可以单独合并。阶段 1 先修复数据丢失风险；阶段 2 先让用户看到存储空间界面，后续阶段把各自的区域接进来。

每个阶段完成后，按 AGENTS.md 在仓库根目录执行编译验证：

```powershell
dotnet msbuild BetterGenshinImpact.sln -restore -p:Configuration=Debug -nologo -tl:off -clp:ErrorsOnly
```

| 阶段 | 内容 | 验收 |
|---|---|---|
| 1. 存储基础与临时文件 | `AppPaths`、`StorageArea` 与 `IStorageService`、`PendingCleanupStore`、`LegacyDataCleaner`、`AtomicFile`、`ITempFileService`；迁移 `TempManager` 的全部用法、脚本更新备份、仓库导入与克隆的临时目录、通知图片；旧目录清单加入 `User/Temp`、`Repos/Temp` | 主实例和云原神实例同时运行时，任一实例退出不影响另一实例的临时文件；结束进程后重启会回收残留；脚本更新中途结束进程后备份仍在，并有提示；带 `..` 的订阅路径被拒绝；旧目录清理每个版本只执行一次，有文件被占用时下次启动重试 |
| 1 的实施说明（已完成） | 与上一行的差异：`DownloadRepoAndUnzip` 依赖被删除的 `Repos/Temp`，且没有活跃调用，提前在阶段 1 删除；脚本更新备份改为每个脚本一个时间戳目录，全部恢复成功才删除，未恢复的备份在下次启动时以对话框提示并可打开目录；订阅路径和 `savedFiles` 都校验为安全的相对路径；不定义"其他"临时分类，新功能按需新增 | 编译通过；临时验证测试覆盖了路径校验、租约与遗留目录回收、分类清理、存储区域清理和推迟清理，验证后删除；多实例同时运行与中途结束进程的场景需在真实环境验收 |
| 2. 存储空间窗口与推理缓存 | 窗口、ViewModel、设置页入口；推理缓存改到 `Data/Cache/Inference`，删除 `BgiOnnxModel` 的按版本缓存目录；旧目录清单加入根目录 `Cache/`；登记推理缓存、日志与诊断、用户数据，以及尚未重构部分的临时区域（见 3.5） | 统计与清理不阻塞界面；被占用的文件被跳过并有提示；下次启动清理的区域在重启后被清理；启动后程序根目录不再出现 `Cache/` |
| 2 的实施说明（已完成） | 存储空间窗口为模态窗口，入口在设置页"本机外部访问服务"卡片之后；过渡区域由 `LegacyStorageAreaProvider` 登记（`MemoryFileCache` 目录、音乐封面、运行库安装包、软件内网页数据、云原神实例），网页数据与云原神实例暂只展示占用；推理编译缓存不参与一键清理（重新编译耗时长）；调试图片可单独清理但不参与一键清理；"文件缓存上限"设置随阶段 5 加入 | 编译通过；临时测试验证了分组、排序、一键清理金额、待清理标记，并实际加载窗口 XAML、实例化数据模板、完成布局，验证后删除 |
| 3. 网络与代理 | `NetworkConfig`、`AppWebProxy`、接管 `DefaultProxy`、命名客户端与公共处理器；迁移 6.4 的全部调用点；Git 与 WebSocket 代理；JS 重定向校验；网络设置子视图与连接测试；删除 `DownloadRepoAndUnzip`；修复灰度判定哈希 | 跟随系统模式下行为与改造前一致；切换为自定义代理后，新请求经过本地代理（以代理软件的连接记录确认）；除 Infrastructure、Telegram 独立代理和测试代码外，不再出现 `new HttpClient(`；同一设备多次启动的灰度判定结果相同 |
| 3 的实施说明（已完成） | 与上一行的差异：`OnnxRuntimeInstaller` 仍自建客户端（跟随 `DefaultProxy`），阶段 4 改用下载服务；`ScriptRepoWindow` 中的 `new HttpClient()` 位于已注释的代码中，不处理；`KongyingTavernApiService` 需要设置登录后的 `Authorization` 默认请求头，因此长期持有一个 GameApi 客户端；获取远程分支列表（`ListReferences`）与克隆、拉取一样按网络设置决定是否走代理；Script 客户端逐跳校验重定向，最多 5 跳，不允许从 https 降级到 http；命名客户端不保存 Cookie，并关闭工厂默认的请求日志 | 编译通过；临时验证测试覆盖了三种代理模式与绕过列表、代理地址校验、Git 代理选择、重定向校验、灰度哈希稳定性，并实际加载网络设置视图，验证后删除；经本地代理转发的实际效果需在真实环境验收 |
| 4. 下载服务与下载中心 | `DownloadService`、`IDownloadMonitor`、标题栏下载中心；迁移 `OnnxRuntimeInstaller`、`BannerImageService`；旧目录清单加入 `Plugins/OnnxRuntime/.downloads`；可选接入 Git 进度 | 下载 TensorRT 包中途断网，恢复后续传；取消后重试会续传；校验失败有明确提示；离开硬件加速页后仍能在下载中心看到进度 |
| 4 的实施说明（已完成） | 与上一行的差异：下载分类改为 `DownloadCategory` 类型（与临时文件分类相同的写法），存储空间窗口按分类各一行，并标出未完成的下载数；`DownloadRequest` 增加 `Origin`，运行库安装包设为"硬件加速设置"，下载中心对这类任务不提供重试和打开文件夹，提示回到设置页（下载完成后还要解压安装，单独重试下载不会安装）；`IDownloadService` 增加 `GetDirectory`，安装器据此清理下载目录；同一地址的任务依次执行，后来的任务复用或续传；`RuntimePackageDownloader` 只保留解压与 CRC 校验；标题栏弹出层使用 WPF-UI `Flyout`，定位点放在按钮右下角并以 `Left` 方式展开，使右边缘与按钮对齐；退出确认覆盖主窗口关闭和托盘退出，确认一次后不再重复询问；Git 进度（7.4）本阶段不接入 | 编译通过；临时验证测试覆盖了 200 重写、206 续传、断线与提前关闭后续传、5xx 重试次数、4xx 换备用地址、哈希失败删除 `.part`、大小上限、取消后续传重试、最多 3 个并发、已完成文件复用、同一地址并发、目标路径校验、存储区域、行文字，并实际加载下载中心视图、检查弹出层位置，验证后删除；真实断网续传需在真实环境验收 |
| 5. 缓存 | `IFileCache`、`CachePartitions`、`CacheMaintenanceService`、`IRemoteImageService`；迁移 5.5 中的用法；删除 `MemoryFileCache`；旧目录清单加入 `User/Cache/MemoryFileCache`、`User/Music/Artwork`；缓存分区登记为存储区域 | 缓存超过上限后自动降到 80%；地图上大量图标滚动时，内存中的图片数量不超过上限；清理某个分区后功能正常并重新下载 |
| 5 的实施说明（已完成） | 与上一行的差异：`IFileCache` 只实现有调用方的方法，内存层改用带总量上限（64 MB）的 `MemoryCache`，内存条目与磁盘条目同时到期（见 5.2）；`IRemoteImageService` 以 `GetBytesAsync` 取代 `GetLocalFileAsync`，单张上限 20 MB（见 5.4）；`MapIconImageCache` 的程序内置图标（pack 资源和程序目录文件）不再写入磁盘缓存，直接解码保存在内存；在存储空间窗口清理缓存分区时同时清空内存层；缓存维护还会删除超过 1 小时的残留临时文件，记录文件写入失败时本进程内也不会重复执行；`User/Cache` 在清理后为空时一并删除；存储空间窗口新增"自动清理"卡片，提供缓存上限下拉框 | 编译通过；临时验证测试覆盖了并发只调用一次 factory、跨实例读磁盘、过期重新获取、空结果不缓存、大条目不进内存、调用方取消不影响共享获取、`RetainOnly` 与 `Remove`、分区存储区域与清理、维护的过期删除和上限淘汰、远程图片的请求合并和 512 张上限，并实际加载存储空间窗口检查分区行和下拉框绑定，验证后删除；地图图标的实际显示需在真实环境验收 |
| 6. WebView2 | 环境定义与 Profile、环境入口、`WebViewSetup`、运行时提示；迁移 `WebpagePanel`、`HtmlMaskWindow`、`CloudWebHostWindow`、`WebViewInstanceStore`、实例管理窗口；`ProcessFailed` 处理；网页数据存储区域；旧目录清单加入 `WebView2Data/` | 全仓库只有环境入口调用 `CoreWebView2Environment.CreateAsync`；未安装运行时能看到说明并跳转下载页；以 dotnet 宿主启动时数据目录仍在 `Data/WebView2/`；云原神实例清理缓存后登录状态保留；在 `WebViewEnvironments` 中临时增加一个测试环境，能在存储空间窗口看到对应行，删除定义后其目录显示为"已不再使用的网页数据" |
| 6 的实施说明（已完成） | 与上一行的差异：网页数据清理统一推迟到下次启动，未实现存活 WebView 的即时清理（见 8.5）；云原神实例的存储区域由 Service 层的 `CloudGameStorageAreaProvider` 登记，`LegacyStorageAreaProvider` 已删除；`IWebViewEnvironmentProvider` 增加 `ReportProcessFailed` 统一记录网页进程退出，`WebViewOptions` 增加 `ProcessCrashed`，由 `WebpagePanel` 隐藏网页并显示说明和"重新加载"（网页是原生窗口，提示不能叠在上面）；`WebpagePanel` 改为第一次加载或导航时初始化，导航请求等初始化完成后执行，初始化 Action 在首次导航之前调用；脚本仓库自己的 `NewWindowRequested` 处理已删除，统一只把 http、https 交给系统浏览器；HtmlMask 与软件内网页一样只在 Debug 下开启开发者工具；云原神页面进程退出时立即标记未就绪，并询问是否重新加载；`.agents/code-review.md` 加入第 14 节的检查项 | 编译通过；临时验证测试覆盖了定义校验、Profile 与缓存目录的存储区域、已不再使用的环境目录、云原神环境只能在同名实例进程创建、清理实例缓存后 Cookie 保留，并用真实 WebView2 初始化两个 Profile，确认 `ProfilePath` 与代码中的目录一致、同一环境只创建一次，验证后删除；运行时缺失提示、崩溃恢复和云原神实例的实际运行需在真实环境验收 |

建议补充的单元测试（`Test/BetterGenshinImpact.UnitTest`）：

- `TempFileService`：`.lease` 释放后残留目录被回收，持有 `.lease` 时不被回收，`GetPath` 拒绝越界路径。
- `LegacyDataCleaner`：标记版本等于当前版本时跳过；全部成功才写标记；部分失败时不写标记。
- `AppWebProxy`：三种模式和绕过列表的路由结果。
- `DownloadService`：用假的 `HttpMessageHandler`（参照 `TelegramNotifierTests` 的写法）验证 206 续传、200 重新下载、哈希失败、重试次数。
- `FileCache`：用 `TimeProvider` 验证过期和容量淘汰。

## 14. 后续开发接入规范

狛荷屋物品图标暂为独立存储例外：扫描或人工上传的图标不能在缓存未命中时即时重建，保存在 `Data/Cache/AccountRecords/icons/{模型版本}`，由 `InventoryIconCache` 实现 `IStorageAreaProvider` 登记。该区域不按时间或容量自动淘汰、不参与一键清理，显式清理需要确认。待 OCR 接入及公共缓存支持相应保留和主动更新策略后，再评估接入 `IFileCache`；库存文件及其 `.bak` 恢复机制不变。旧 `Cache/AccountRecords` 沿用上游旧目录清理策略，不自动迁移，旧图标需重新提供。

| 我需要 | 使用 | 不要 |
|---|---|---|
| 解压、克隆等一次性临时目录 | `ITempFileService.CreateScope(TempCategories.X)` | `Path.GetTempPath()`、自己拼 `User/Temp`、在业务目录旁建 `_temp` 目录 |
| 交给外部组件读取的临时文件 | `ITempFileService.CreateFilePath` | 写完不删 |
| 缓存可再生的图片或接口数据 | 在 `CachePartitions` 新增分区，调用 `IFileCache` 或 `IRemoteImageService` | 新建缓存目录、用静态 `ConcurrentDictionary` 存图片 |
| 发 HTTP 请求 | `IHttpClientFactory.CreateClient(HttpClientNames.X)` | `new HttpClient()`、静态 HttpClient 字段 |
| 下载较大的文件，或需要进度 | 在 `DownloadCategories` 新增分类，调用 `IDownloadService.Start`；下载后还要安装时设置 `Origin` | 自己写下载循环、用 `ReadAsByteArrayAsync` 读整个文件 |
| 显示网页 | `WebpagePanel`，或 `WebViewSetup.InitializeAsync` 加 `WebViewProfiles` | 直接调用 `CoreWebView2Environment.CreateAsync`、自定数据目录 |
| 需要独立的网页环境 | 在 `WebViewEnvironments` 新增定义，再为它定义 Profile | 从用户配置读取浏览器参数 |
| 废弃旧目录 | 在使用它的代码切到新路径的同一版本，把旧目录加入 `LegacyDataCleaner` 的清单 | 写一次性的迁移代码，或保留旧目录 |
| 新的会占用磁盘的目录 | 实现 `IStorageAreaProvider` 登记 | 只写不管、没有清理入口 |

约束手段：

- 在 `.agents/code-review.md` 中加入上表的检查项。
- 本期不引入 `Microsoft.CodeAnalysis.BannedApiAnalyzers`，规范靠代码审查清单执行。以后如果违规反复出现，再考虑用它在 Infrastructure 之外禁止 `new HttpClient()`、`Path.GetTempPath()` 和 `CoreWebView2Environment.CreateAsync`。

## 15. 决策记录与待实测项

### 15.1 已确认的决策（2026-10-28）

1. 程序生成的数据放在程序目录下的 `Data/`，不放在 `User/` 下；`Data/` 内按缓存、下载、临时文件、网页数据分目录，推理缓存与通用文件缓存分开（3.3）。
2. 默认"跟随系统代理"。Git 拉取只在"自定义代理"模式下走代理，另外两种模式保持现在的直连（6.5）。
3. WebView2 页面（含云原神）第一版不接入自定义代理（8.6）。
4. 存储空间使用独立窗口，入口在设置页；下载中心入口在标题栏（9.3、9.4）。
5. 截图可以清除但需确认，不参与一键清理，也不自动删除（9.3）。
6. 灰度判定改为按设备稳定的哈希，在阶段 3 修复（2.5）。
7. `Cache/{版本号}/Model` 没有读写方，停止创建并删除（5.5）。
8. 本期不引入 BannedApiAnalyzers（14）。
9. 旧目录不迁移，每个版本启动时清理一次，全部成功才标记（3.4）。
10. 普通 WebView2 环境可以有多个，配置集中在 `WebViewEnvironments` 定义（8.2）。

### 15.2 需要实测的技术点

- 桌面分身与主实例共用 `Data/WebView2/App/` 是否冲突（8.7）。
- 普通环境多 Profile 时，各 Profile 缓存子目录在 `EBWebView/` 下的实际位置（8.5）。
- `WebpagePanel` 所在窗口关闭时，WebView2 控件是否自动释放（8.7）。
- WebView2 数据目录给 Everyone 授予完全控制权限是为了解决什么场景，改为只授予当前用户后该场景是否仍正常（8.7）。
- 运行期间修改 Windows 系统代理后，.NET 的系统代理是否即时生效。这影响"跟随系统"模式，与现状相同。
