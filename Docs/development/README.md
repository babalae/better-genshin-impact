# 开发者文档

本目录存放面向贡献者的开发文档，包括编译、调试、代码规范与协作流程。

## 无头 Worker（`--headless`）

无头 Worker 用于「用户 A 的 GUI 控制用户 B 下运行的 BetterGI」这一场景：Worker 不创建主界面，
只托管跨用户命名管道，任务仍在 Worker 自己的进程与会话中执行。协议与设计见
[多实例命名管道协议](../design/multi-instance-ipc.md) 的「跨用户 Worker（`--headless`）」一节。

把整个环境（创建第二个用户、本地 RDP 多会话、RDM 登录、启动 Worker）一步步搭起来的过程，
写在面向使用者的 [单机双用户配置指南](../guides/multi-user-setup.md) 里，本节只讲开发相关的内容。

### 命令行用法

```powershell
# 在目标用户（要跑游戏的那个用户）下启动 Worker
BetterGI.exe --headless --controller-sid <控制端用户的 SID>
```

- `--headless`：不创建主界面，只托管跨用户 Worker 管道；
- `--controller-sid`：允许连接本 Worker 的控制端用户 SID。省略时只允许 Worker 自身用户连接
  （即本机同用户调试也能连上）；
- 建议以**管理员身份**启动：Worker 需要操作游戏窗口与截图，权限不足会失败。

启动后会**自动新建一个可见的控制台窗口**（标题 `BetterGI Worker Console`），日志同时输出到
控制台与 `log\better-genshin-impact<日期>.log`。从资源管理器或 Visual Studio 启动时父进程
没有控制台，因此必须新建，否则看不到任何输出。

### 查看当前用户的 SID

```powershell
[System.Security.Principal.WindowsIdentity]::GetCurrent().User.Value
```

或使用 `whoami /user`，输出中最后的 `S-1-5-21-...` 即 SID。GUI 侧在启动页的「跨用户 Worker」
卡片里也能看到并复制**本用户 SID**，连接成功后 Worker 用户 SID 会自动保存并在下次启动回填。

### 两个用户之间的完整流程

1. 在**要跑游戏的那个用户**（用户 B）下以管理员身份启动 Worker，并把用户 A 的 SID 传给
   `--controller-sid`；
2. 在**要操作界面的那个用户**（用户 A）下正常启动 BetterGI，打开启动页的「跨用户 Worker」卡片，
   填入用户 B 的 SID 并点击「连接」；卡片上的「本用户 SID」正是第 1 步要传给 `--controller-sid` 的值；
3. 连接成功后，A 的界面上的任务入口都会下发到 B 执行；卡片上的「启动截图器」用于拉起 B 的
   截图器与游戏内叠加层。

两个用户必须使用**同一份 BetterGI 安装目录**：配置组、脚本、地图追踪都按安装目录解析，
Worker 找不到对应文件时会拒绝任务。

### 在 Visual Studio 中调试

[launchSettings.json](../../BetterGenshinImpact/Properties/launchSettings.json) 里的
`commandLineArgs` 已经带上了 `--headless --controller-sid <SID>`，把 SID 换成自己的即可直接 F5。
注意两点：

- 参数之间用**空格**分隔，不要用换行符（`\r\n` 会被当成同一个参数的一部分，导致 `--headless` 不被识别）；
- 需要管理员权限时，以管理员身份启动 Visual Studio，调试出的进程才具有管理员权限。

连接成功后主界面上的「启动/停止、调度器、一条龙、独立任务、脚本、地图追踪」都会下发到 Worker，
本机不再启动截图器。无头实例没有主窗口，**游戏内的遮罩叠加层就是它唯一的界面**；Worker 产生的
提示会回传给控制端显示，日志显示位置可在启动页选择（见协议文档的「日志显示位置」）。

跨用户 Worker 的单元测试位于
`Test/BetterGenshinImpact.UnitTest/ServiceTests/Worker/`，覆盖命令行解析、管道命名、授权策略、
管道 ACL、状态与日志序列化，以及基于真实命名管道的提示/日志回传；需要两个真实 Windows 用户
才能验证的部分无法自动断言。

## 发行包专属资源（本地构建会缺的东西）

有一部分体积较大的资源**不随源码仓库分发**，源码仓库与 `BetterGI.Assets.*` NuGet 资源包里都没有，
只在完整发行包里（见 [Build/setup_build.cmd](../../Build/setup_build.cmd) 中
「添加一些配置文件开始 / 大文件不适合放在 Github」那一步）。

目前已知的这类资源：

| 相对程序目录的路径 | 用途 | 缺失时的表现 |
| --- | --- | --- |
| `Assets\Web\ScriptRepo\index.html` | 「脚本仓库 → 打开仓库」的网页界面（WebView2） | 打开后只有一个白屏 |

因此，**从 Visual Studio 调试运行、或直接用 `dotnet publish` 的产物运行时，这些功能会缺资源**。

补齐方式（二选一）：

1. 把发行包里对应的目录直接复制到程序目录（例如 `BetterGenshinImpact\bin\x64\Debug\net8.0-windows10.0.22621.0\`）；
2. 让构建自动补齐：在本机未被版本控制的 `BetterGenshinImpact\BetterGenshinImpact.csproj.user` 里指定
   本机已有的完整发行包目录，构建与 `publish` 时会自动把 `Assets\Web` 复制到输出目录：

```xml
<PropertyGroup>
  <BetterGiReleaseAssetsDir>D:\BetterGI</BetterGiReleaseAssetsDir>
</PropertyGroup>
```

未指定该属性（或该目录下没有 `Assets\Web`）时，构建会照常跳过这一步并在输出里给出提示，不影响编译。
