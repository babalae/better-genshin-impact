# 网络代理设置

对应 issue #2305。入口为主窗口左侧「设置」页中的「网络代理」分组，默认关闭，默认地址为 `http://127.0.0.1:7890`。

该分组沿用设置页既有卡片样式：`ui:CardExpander` 分组标题 + 两行说明、右列 `ui:ToggleSwitch` / `ui:TextBox` / `ui:Button`，不单独占用左侧导航项。
界面文案只描述选项用途，不展示能力边界；测试结果仅在执行过测试后显示。

## 生效范围

| 功能 | 生效时机 | 支持范围 |
| --- | --- | --- |
| .NET HTTP 请求 | 后续请求立即生效，不取消在途请求 | HTTP、HTTPS、SOCKS5 代理 |
| GitHub/CNB 脚本仓库同步 | 下一次同步使用同一个配置快照 | HTTP 代理访问 HTTPS 仓库 |
| 内嵌 WebView2 网页 | 重启 BGI 后生效 | HTTP、HTTPS、SOCKS5 代理 |

- 关闭自定义代理后，HTTP 委托给启动时的系统/环境代理，不强制直连，也不修改 Windows 或环境变量。
- Git 关闭代理时保持原来的 `ProxyType.None`。HTTPS/SOCKS5 代理不用于 Git，并在设置页和日志中提示直连降级。
- 当前 libgit2 的普通 HTTP 远端不经过代理隧道；启用 HTTP 代理时拒绝同步这种远端，请改为 HTTPS 仓库地址。GitHub/CNB 默认地址不受影响。
- Telegram 的独立代理配置与自动连招 LLM 保持原行为。不覆盖外部更新器、外部浏览器或游戏流媒体。
- 已打开的内嵌网页不会重载。多个 BGI 实例共用浏览器用户数据时，需要将相关实例全部退出后重启；不删除用户数据或登录状态。

## 输入与测试

- 地址必须包含 `http://`、`https://` 或 `socks5://`，以及主机和显式端口（1–65535），支持 IPv6。
- 不支持代理账号密码、路径、查询参数或片段。失焦/回车后保存有效地址；非法草稿不覆盖已保存值，也不能用于开启代理。
- 启动配置中如有非法地址，会停用自定义代理并在设置页的「网络代理」分组中显示原因，保留原地址供修正。
- 「测试连接」使用输入框当前值，不要求保存或启用；点击或用键盘移到测试按钮不会触发保存。
- 分别请求 `https://api.github.com` 与 `https://cnb.cool`，每项最多 10 秒，可取消，测试期间显示进度，结束后按行显示结果。

## 实现要点

- `ProxyService` 在配置读取后、网络服务初始化前发布稳定的动态 `IWebProxy`。现有客户端不需替换或释放，因此切换时保持 Cookie、请求头、超时与在途请求。
- `HttpClientFactory.GetClient` 返回共享客户端，不由调用方释放；`CreateClient` 返回独立客户端，由调用方释放。
- `SetVirtualHostNameToFolderMapping` 注册的虚拟域名由 WebView2 内部解析，不受 `--proxy-server` 影响，因此 WebView2 只下发 `--proxy-server`，与本次功能改造前的行为保持一致。
- 脚本仓库页面依赖 `Assets\Web\ScriptRepo`，该前端资源由 `.github/workflows/publish.yml` 在发布时注入，仓库内不含该目录。使用本地编译产物调试时需自行补上该目录，否则页面会停留在空白状态。
- `LibGit2Sharp 0.31.0` 的 `Network.ListReferences` 带凭据重载丢弃代理参数，使用 `Repository.ListRemoteReferences` 静态 API 避免该问题。
- Git 更新先在同级唯一临时目录下载，验证提交、`repo.json` 和 `repo/` 后再替换；连接失败、校验失败或原仓库占用时保留本地数据。不清除 `.gitconfig` 中的代理项。
- 界面使用现有 i18n 机制，仅补充本次新增翻译键；空翻译回退到中文原文。

## 验证

```powershell
dotnet build BetterGenshinImpact/BetterGenshinImpact.csproj -c Debug -p:Platform=x64 -p:RestoreDisableParallel=true -m:1
dotnet test Test/BetterGenshinImpact.UnitTest/BetterGenshinImpact.UnitTest.csproj -c Debug -p:Platform=x64 -p:RestoreDisableParallel=true -m:1 --filter "FullyQualifiedName~HelpersTests.Http|FullyQualifiedName~RepositoryDirectoryTransactionTests"
```

测试只使用本机模拟 HTTP/SOCKS5/TLS 端点；Git 用本机代理返回可控失败来验证查询、克隆和拉取的路由。TLS 测试生成临时自签名证书，按指纹仅供测试客户端信任，不安装到系统信任库。

人工验收（不要求启动游戏）：

1. 开关、修改代理后，检查更新、Markdown 图片和脚本仓库后续请求使用新代理。
2. 浏览器代理变更后显示重启提示，当前网页及云原神会话不被中断。
3. 退出所有相关实例再启动，确认网页使用新设置并保留登录状态。

本次另修正上游钓鱼截图逻辑的一处编译阻塞：改用 `TaskContext.Instance().Runtime?.Capture`，不再访问已经私有化的 `TaskTriggerDispatcher.GameCapture`。
