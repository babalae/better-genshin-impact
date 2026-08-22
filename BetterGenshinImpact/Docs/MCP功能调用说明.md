# BetterGI MCP / Function Calling 使用说明

本文面向需要让 AI、自动化客户端或其他本机程序调用 BetterGI 功能的使用者。实现基于官方 C# MCP SDK `ModelContextProtocol.AspNetCore 2.2.0`，使用 MCP Streamable HTTP 传输。

## 1. 能做什么

当前实现提供 85 个显式 MCP 工具，并自动把主应用依赖注入（DI）中已注册 ViewModel 的 `RelayCommand` 建成可发现、可调用的命令目录。

覆盖范围包括：

- BetterGI、截图器、游戏窗口和任务状态查询；
- 启动/停止截图器，取消当前任务；
- 配置组与一条龙配置查询、执行；
- 调度配置组创建、重命名、删除、组级设置替换；
- 调度项目新增、修改、移除、排序和单项目执行；
- 已安装 JS/地图追踪/键鼠脚本发现；
- JS manifest、`settings_ui` Schema、项目级设置读取/校验/修改和临时执行；
- 原神兑换码执行；
- 脚本仓库渠道切换、仓库拉取/更新；
- 脚本订阅查看、替换、新增、取消和批量更新；
- 全量设置读取、设置路径/类型发现、按路径修改并持久化；
- 已注册 ViewModel 中其余 `RelayCommand` 的统一发现和调用。

源码中目前约有 290 处 `[RelayCommand]` 声明。实际可调用数量以 `bgi_list_commands` 返回为准：只有已经注册到主应用 DI、且生成了公开 `ICommand` 属性的 ViewModel 命令会进入目录。以后新增并注册的 ViewModel 命令不需要修改 MCP 服务即可被发现。

## 2. 启动 MCP 服务

BetterGI 主实例默认启动本机 MCP，供外部客户端和内置 Agent 共用；桌面分身/WebView 子实例默认不监听。

默认地址：

```text
http://127.0.0.1:5042/mcp
```

默认无需命令行参数。自定义端口：

```powershell
BetterGI.exe --mcp-port 6123
```

`--mcp` 为兼容参数；主实例即使省略也会启动。BetterGI 内部重启时会保留 MCP 端口。

健康检查地址为 `http://127.0.0.1:5042/health`。MCP 服务器只绑定 IPv4 回环地址，并检查远端地址、`Host` 和 `Origin`；不会监听局域网或公网地址。

## 3. MCP 客户端配置

不同 MCP 客户端的配置文件位置不同，但 Streamable HTTP 配置的核心类似：

```json
{
  "mcpServers": {
    "bettergi": {
      "type": "http",
      "url": "http://127.0.0.1:5042/mcp"
    }
  }
}
```

MCP 不使用 Token 鉴权，仅接受本机回环连接，并校验 `Host`/`Origin`。具体字段名仍应以所用客户端的 MCP 配置文档为准。

## 4. 工具清单

如果 AI 不清楚 BetterGI 的业务对象或调用顺序，应先调用 `bgi_get_capabilities`。它会解释 JS 脚本、调度配置组、组内项目、项目级 JS 设置和任务运行时之间的关系，并返回推荐工具链。

### 状态与生命周期

| 工具 | 用途 | 关键参数 |
|---|---|---|
| `bgi_ping` | 检查 MCP 服务是否存活 | 无 |
| `bgi_get_status` | 查询版本、进程、截图器、游戏句柄、任务和当前脚本状态 | 无 |
| `bgi_cancel_current_task` | 请求取消当前独立/连续任务 | `confirm: true` |
| `bgi_start_capture` | 执行主页“启动”命令 | 无 |
| `bgi_stop_capture` | 停止截图器、实时触发器并取消任务 | `confirm: true` |
| `bgi_get_game_readiness` | 检查游戏窗口、关联启动、截图器、界面和任务锁 | 无 |
| `bgi_prepare_game` | 按标准流程启动游戏/截图器并等待界面就绪 | `confirm`、主界面要求、超时 |
| `bgi_return_to_main_ui` | 无任务运行时从普通菜单/对话返回主界面 | `confirm: true` |
| `bgi_close_game` | 先停止自动化，再正常关闭或超时终止原神进程 | `confirm`、`stopCurrentTask`、`waitSeconds` |
| `bgi_start_game` | 使用配置路径启动原神进程，不启动截图器 | `confirm`、`activateWindow` |
| `bgi_restart_game` | 停止任务/截图器、关闭并重启原神，可等待自动化就绪 | `confirm`、`prepareAutomation`、`timeoutSeconds` |
| `bgi_activate_game_window` | 恢复并前置原神窗口 | `confirm: true` |
| `bgi_minimize_game_window` | 最小化原神窗口，不停止任务 | `confirm: true` |

运行脚本或游戏任务前推荐：

```text
bgi_get_game_readiness
→ 若未就绪：bgi_prepare_game(confirm=true, requireMainUi=true)
→ 检查 ready=true
→ 再调用目标 bgi_run_* 工具
```

准备流程复用现有 `HomePageViewModel.OnStartTriggerAsync`：查找原神窗口；启用关联启动时按配置启动原神；随后启动截图器、实时触发器和遮罩，并等待自动进门或可识别界面。登录、公告、更新、验证码等需要人工处理的界面不会被宣称为就绪。

### 设置

| 工具 | 用途 | 关键参数 |
|---|---|---|
| `bgi_get_settings` | 读取全部设置或一个点分隔路径 | `path`、`includeSensitive`、`confirmSensitive` |
| `bgi_describe_settings` | 列出可用设置路径、CLR 类型、可写性和敏感性 | 可选 `filter` |
| `bgi_list_setting_sections` | 列出 36 个业务分区的中文用途和设置数量 | 无 |
| `bgi_search_settings` | 多词、分区、路径前缀、类型、可写性和分页结构化搜索 | `terms`、`matchMode` 及组合过滤参数 |
| `bgi_get_setting_details` | 按精确路径批量读取说明、当前/默认值、枚举和范围 | `paths` |
| `bgi_update_settings` | 最多 100 项的批量预检/修改 | `changes`、`dryRun`、`confirm` |
| `bgi_set_setting` | 修改一个设置并立即保存到 `User/config.json` | `path`、JSON `value`、`confirm: true` |

运行时目录当前可发现约 635 个设置叶子项，划分为 36 个业务分区。说明来源分为：

- `xml-summary` / `xml-inheritdoc`：直接来自源码 XML 中文注释；
- `DescriptionAttribute`：来自代码特性；
- `inferred`：源码没有正式注释，服务使用分区用途、中文属性词元、精确路径、类型、当前值和默认值生成结构说明。

AI 不应从完整 `config.json` 中猜字段。推荐流程：

```text
1. bgi_list_setting_sections
2. bgi_search_settings(
     terms=["树脂", "领奖"],
     matchMode="all",
     section="autoDomainConfig",
     writableOnly=true,
     page=1,
     pageSize=30)
3. bgi_get_setting_details(paths=[...精确候选...])
4. bgi_update_settings(changes=[...], dryRun=true)
5. bgi_update_settings(changes=[...], dryRun=false, confirm=true)
```

枚举设置会同时返回 `allowedValues` 和中文 `allowedValueDescriptions`。批量修改会先在配置副本上验证全部路径和类型，只有全部有效才进入 UI 线程应用。

设置路径不区分大小写，推荐使用 `bgi_describe_settings` 返回的 camelCase 路径。例如：

```json
{
  "path": "scriptConfig.autoUpdateSubscribedScripts",
  "value": true,
  "confirm": true
}
```

读取设置默认会遮蔽密码、Token、Cookie、Webhook、Endpoint、URL 和各类 API Key。确实需要完整值时，必须同时设置：

```json
{
  "includeSensitive": true,
  "confirmSensitive": true
}
```

`bgi_set_setting` 修改的是公开可写属性，目标值会按照属性的真实类型反序列化。标记为 `[JsonIgnore]` 的运行时属性不能通过该工具持久化。当前不支持用数组下标表达式直接修改集合中的单项；可整体提交集合值。

### 脚本仓库与订阅

| 工具 | 用途 | 关键参数 |
|---|---|---|
| `bgi_get_script_repository` | 查询当前渠道、URL、本地路径和更新时间 | 无 |
| `bgi_set_script_repository_channel` | 切换 `CNB`、`GitCode`、`GitHub` 或自定义渠道 | `channel`、可选 `customUrl`、`confirm: true` |
| `bgi_update_script_repository` | 浅克隆或拉取脚本仓库 | 可选 `repositoryUrl`、`confirm: true` |
| `bgi_list_script_subscriptions` | 查看当前仓库的订阅路径 | 无 |
| `bgi_set_script_subscriptions` | 完整替换订阅清单，不改已安装文件 | `paths`、`confirm: true` |
| `bgi_subscribe_scripts` | 新增订阅，可立即安装/覆盖对应脚本 | `paths`、`install`、`confirm: true` |
| `bgi_unsubscribe_scripts` | 移除订阅，不删除已安装脚本 | `paths`、`confirm: true` |
| `bgi_update_subscribed_scripts` | 更新仓库并重装全部订阅脚本 | `confirm: true` |
| `bgi_refresh_repository_index` | 强制丢弃缓存并重读当前动态索引 | 无 |
| `bgi_get_repository_index_summary` | 查看当前索引版本、动态顶层分类和节点统计 | 无 |
| `bgi_browse_repository` | 按目录逐层浏览直接子节点并分页 | 精确 `path`、类型、分页 |
| `bgi_search_repository` | 多词 AND/OR、分类、路径、标签、作者、时间组合检索 | 结构化过滤参数 |
| `bgi_get_repository_item` | 精确节点详情、后代数量、订阅状态和本地安装位置 | 精确 `path` |
| `bgi_get_repository_facets` | 在不知道关键词时统计常见标签和作者 | 分类/路径范围、`limit` |
| `bgi_resolve_repository_paths` | 批量精确验证路径与安装位置 | `paths` |
| `bgi_subscribe_repository_items` | 只接受当前索引中真实存在的精确节点 | `paths`、`install`、`confirm: true` |
| `bgi_run_repository_pathing` | 必要时安装精确 pathing 节点，用内存 ScriptGroup 顺序运行，不写入调度器文件 | `path`、`installIfMissing`、`maxRoutes`、`confirm` |

仓库搜索没有内置某一版 `repo.json` 快照。每次调用都会定位当前渠道的 `repo.json`/`repo_updated.json`，检查文件路径、长度和修改时间；仓库更新或渠道切换后会主动清空索引。`bgi_refresh_repository_index` 可处理外部程序直接改文件的场景。顶层分类从当前 JSON 动态读取，未来仓库增加新分类仍可浏览和搜索。

大型仓库推荐导航流程：

```text
1. bgi_get_repository_index_summary
2. 不知道词时：bgi_get_repository_facets(rootType="pathing")
   知道分类时：bgi_browse_repository(path="pathing") 逐层进入
3. bgi_search_repository(
     terms=["蒙德", "慕风蘑菇"],
     matchMode="all",
     rootType="pathing",
     tagsAll=["地方特产"],
     pathPrefix="pathing/地方特产",
     pageSize=20)
4. bgi_get_repository_item(path=<候选精确路径>)
5. 检查 descendantCount、installDestination 后调用
   bgi_subscribe_repository_items(paths=[...], install=true, confirm=true)
```

不要用一个宽泛关键词直接订阅结果；目录节点可能覆盖大量后代，详情工具会给出粒度警告。

合法订阅路径必须以 `pathing`、`js`、`combat` 或 `tcg` 开头，不能是绝对路径，也不能包含 `.`/`..` 路径跳转。例如：

```json
{
  "paths": [
    "pathing/枫丹/示例路线",
    "js/示例脚本"
  ],
  "install": true,
  "confirm": true
}
```

`bgi_set_script_subscriptions` 和 `bgi_unsubscribe_scripts` 只修改 `User/Subscriptions` 中的清单，不删除用户脚本。`bgi_subscribe_scripts` 在 `install: true` 时，以及 `bgi_update_subscribed_scripts`，会覆盖订阅所对应的用户脚本；JS 脚本仍沿用项目现有的配置备份与依赖处理逻辑。

### 工作流

| 工具 | 用途 | 关键参数 |
|---|---|---|
| `bgi_list_script_groups` | 查看配置组和其中的项目 | 无 |
| `bgi_run_script_groups` | 按顺序执行一个或多个配置组 | `names` |
| `bgi_list_one_dragon_configs` | 查看一条龙配置和启用任务 | 无 |
| `bgi_run_one_dragon` | 执行指定或当前一条龙配置 | 可选 `configName` |
| `bgi_redeem_codes` | 在游戏内执行 12 位兑换码 | `codes` |

### 调度器与 JS 脚本

| 工具 | 用途 | 关键参数 |
|---|---|---|
| `bgi_get_capabilities` | 获取业务概念、推荐调用顺序和风险提示 | 无 |
| `bgi_list_javascript_scripts` | 列出有效 JS 脚本及 manifest 摘要 | 可选 `filter` |
| `bgi_get_javascript_script` | 读取完整 manifest、设置 Schema 和所有配置组引用 | `folderName` |
| `bgi_list_available_scripts` | 列出可加入调度器的 JS、地图追踪、键鼠脚本 | 可选 `type`、`filter`、`limit` |
| `bgi_get_script_group` | 读取配置组完整 JSON 模型 | `groupName` |
| `bgi_create_script_group` | 创建空配置组，可指定完整组级配置 | `groupName`、可选 `groupConfig`、`confirm: true` |
| `bgi_rename_script_group` | 重命名配置组和文件 | `groupName`、`newName`、`confirm: true` |
| `bgi_delete_script_group` | 删除配置组，不删脚本文件 | `groupName`、`confirm: true` |
| `bgi_set_script_group_config` | 替换组级 Pathing/Shell 配置，不动项目 | `groupName`、`groupConfig`、`confirm: true` |
| `bgi_add_script_group_project` | 添加 Javascript/Pathing/KeyMouse/Shell 项目 | 类型、名称、目录、周期、次数、权限、设置、确认参数 |
| `bgi_update_script_group_project` | 修改项目状态、周期、次数、JS 设置和权限 | `groupName`、`projectIndex`、待修改字段、`confirm: true` |
| `bgi_remove_script_group_project` | 移除项目并重新编号，不删脚本文件 | `groupName`、`projectIndex`、`confirm: true` |
| `bgi_reorder_script_group_project` | 修改项目执行顺序 | `groupName`、`fromIndex`、`toIndex`、`confirm: true` |
| `bgi_get_js_script_settings` | 读取一个组内 JS 项目的设置 Schema 和当前值 | `groupName`、`projectIndex` |
| `bgi_set_js_script_settings` | 校验后替换或合并项目级 JS 设置 | `groupName`、`projectIndex`、`settings`、`replace`、`confirm: true` |
| `bgi_run_javascript_script` | 不建配置组，使用临时设置直接运行 JS | `folderName`、可选 `settings`、通知/HTTP 权限 |
| `bgi_run_script_project` | 运行配置组中的单个项目 | `groupName`、`projectIndex`、禁用/Shell 确认参数 |

JS 脚本的 `folderName` 是 `User/JsScript` 下的目录名，也是脚本稳定标识；manifest 中的 `name` 只是显示名称。不要猜 `folderName`，先调用 `bgi_list_javascript_scripts`。

JS 自定义设置属于“配置组中的某个项目实例”，不是脚本目录的全局设置。同一个 JS 可以在多个组中出现，每个项目都有不同的 `JsScriptSettingsObject`。推荐流程：

```text
1. bgi_get_javascript_script(folderName)
   读取 settingsSchema 和 groupUsages
2. bgi_get_js_script_settings(groupName, projectIndex)
   确认这个项目实例的当前值
3. bgi_set_js_script_settings(groupName, projectIndex, settings, replace, confirm=true)
4. bgi_run_script_project(groupName, projectIndex)
```

示例设置定义可能要求字符串、布尔值或字符串数组。服务端会按 `settings_ui` 中的 `type`、`options` 和 `cascadeOptions` 校验；未声明字段、错误类型或无效选项会作为工具错误返回。

临时执行、不保存到配置组：

```json
{
  "folderName": "AutoCrystalfly",
  "settings": {
    "someOption": true
  },
  "allowNotification": false,
  "allowHttp": false
}
```

如果脚本需要访问 manifest 的 `http_allowed_urls`，同时传 `allowHttp: true` 和 `confirmHttpAccess: true`。这只授权 manifest 声明的 URL。Shell 项目直接执行本机命令，因此新增或运行时还必须传 `confirmShellCommand: true`。

`projectIndex` 是配置组 JSON 中 1 开始的执行顺序。任何增删或排序后都应重新调用 `bgi_get_script_group` 获取最新索引。

### 独立游戏任务

| 工具 | 用途/设置来源 |
|---|---|
| `bgi_list_game_tasks` | 返回任务 ID、中文用途、设置路径、专用运行工具和本次运行参数 |
| `bgi_get_game_task_settings` | 按 taskId 读取任务完整设置，并给出 `bgi_set_setting` 修改路径 |
| `bgi_run_genius_invokation` | 自动七圣召唤；`autoGeniusInvokationConfig` |
| `bgi_run_auto_wood` | 自动伐木；`autoWoodConfig`，另有本次 `rounds`、`dailyMaxCount` |
| `bgi_run_auto_fight` | 自动战斗；`autoFightConfig` |
| `bgi_run_auto_domain` | 自动秘境；`autoDomainConfig` + `autoFightConfig`，另有本次 `rounds` |
| `bgi_run_auto_boss` | 自动首领讨伐；`autoBossConfig` |
| `bgi_run_stygian_onslaught` | 自动幽境危战；`autoStygianOnslaughtConfig` |
| `bgi_run_auto_music_game` | 自动音游；`autoMusicGameConfig` |
| `bgi_run_auto_album` | 自动专辑；`autoMusicGameConfig` |
| `bgi_run_auto_cook` | 自动烹饪；`autoCookConfig` |
| `bgi_run_auto_fishing` | 自动钓鱼；`autoFishingConfig`，可临时启用按键截图 |
| `bgi_run_ley_line_outcrop` | 自动地脉花；`autoLeyLineOutcropConfig` |
| `bgi_run_artifact_salvage` | 自动分解圣遗物；`autoArtifactSalvageConfig`，必须确认永久分解 |
| `bgi_collect_grid_icons` | 开发工具：按 `getGridIconsConfig` 采集背包网格图标 |
| `bgi_test_grid_icon_accuracy` | 开发工具：按 `getGridIconsConfig` 测试模型准确率 |

独立任务推荐流程：

```text
1. bgi_list_game_tasks
2. bgi_get_game_task_settings(taskId)
3. 必要时调用 bgi_describe_settings(filter=<settingsPath>)
4. 用 bgi_set_setting 修改具体叶子属性并 confirm=true
5. 调用该任务的 bgi_run_* 专用工具
6. 需要中止时调用 bgi_cancel_current_task(confirm=true)
```

这些具名工具复用任务设置页的原有执行命令，因此策略文件检查、截图器启动、游戏主界面等待、独立任务互斥、取消和任务结束清理逻辑保持一致。

### 运行状态、中断与暂停

| 工具 | 用途 |
|---|---|
| `bgi_get_execution_status` | 查看当前组、项目、类型、进度、循环、暂停和取消状态 |
| `bgi_interrupt_current_script` | 手动取消当前 Javascript/Pathing/KeyMouse/Shell 项目并可等待清理 |
| `bgi_stop_current_task_and_wait` | 取消任何独立任务/连续配置组并等待 `TaskRunner finally` 完成 |
| `bgi_wait_for_current_task` | 不取消，只等待当前任务自然完成或超时 |
| `bgi_pause_current_task` | 请求协作式暂停 |
| `bgi_resume_current_task` | 解除协作式暂停 |
| `bgi_release_all_simulated_keys` | 紧急释放模拟按键，不停止任务 |
| `bgi_get_detached_task_status` | 查看 Agent/MCP 已脱离的最近后台任务状态 |

取消 MCP HTTP 请求仅代表客户端不再等待，不等于取消 BetterGI 游戏任务。需要真正停止时使用：

```text
bgi_get_execution_status
bgi_stop_current_task_and_wait(confirm=true, timeoutSeconds=30)
```

若明确知道当前是调度脚本，也可以使用 `bgi_interrupt_current_script`。停止操作会调用 `CancellationContext.ManualCancel()`、解除暂停并释放模拟按键；不会使用不安全的线程强杀。超时会返回 `stopped=false`，此时可检查状态或停止截图器。

暂停是协作式的：BetterGI 内置路径和调用 `TaskControl` 的脚本会在安全检查点暂停，但第三方 JS 的纯 CPU 循环或外部阻塞不保证立即响应。

独立游戏任务、配置组、一条龙、JS、单项目和仓库路线默认采用脱离式启动：启动器注册唯一 execution ID，通过 `AsyncLocal` 随调用链传递；`TaskRunner` 完成初始化并真正取得独立任务锁后直接完成一次性 `TaskCompletionSource`。这里没有信号量轮询或采样窗口，也不会把其他并发任务误判为本次启动。返回 `accepted=true, running=true` 后 Agent 立即结束本轮，后台由 BetterGI 继续运行。需要结束后台任务时使用 `bgi_stop_current_task_and_wait`；用户之后明确询问进度时再调用执行状态工具。

任务工具复用 BetterGI 原有的 `TaskRunner`、`CancellationContext` 和独立任务信号量，因此不会绕过“同一时间只运行一个独立任务”等现有约束。截图器未启动、游戏窗口未找到或当前已有任务时，调用会按原业务逻辑失败或结束。

### 通用 RelayCommand/function calling

| 工具 | 用途 | 关键参数 |
|---|---|---|
| `bgi_list_commands` | 获取命令名、ViewModel、参数类型、异步标记和确认要求 | 可选 `filter`、`includeDangerous` |
| `bgi_invoke_command` | 执行目录中的任意命令 | `command`、可选 JSON `argument`、可选 `confirm` |

建议先发现、再调用：

```text
1. bgi_list_commands(filter = "auto_domain")
2. 从返回项读取完整 command 和 parameterType
3. bgi_invoke_command(command = "...", argument = ..., confirm = ...)
```

命令名格式为：

```text
<view_model_snake_case>.<command_property_snake_case>
```

CommunityToolkit 会从生成的命令属性名中去掉方法的 `On` 前缀。例如源方法 `OnStopTrigger` 生成 `StopTriggerCommand`，目录名称是 `home_page.stop_trigger`。包含 `Delete`、`Remove`、`Clear`、`Reset`、`Exit`、`Shutdown`、`Restart`、`Install`、`Update`、`Import`、`Save`、`Write`、`Overwrite` 或 `Uninstall` 等名称的命令会标记 `requiresConfirmation: true`，调用时必须传 `confirm: true`。

专用工具拥有更准确的语义、参数和保护措施，能使用专用工具时应优先使用专用工具；`bgi_invoke_command` 是覆盖其余 UI 功能的通用后备入口。

## 5. 内置 AI Agent

主窗口左侧底部提供“AI Agent”页面。Agent 使用微软正式维护的 `Microsoft.Agents.AI.OpenAI 1.18.0`（Microsoft Agent Framework）和官方 OpenAI .NET Provider，不再使用 BetterGI 自己编写的 tool-call 循环。外部模型负责推理，所有 BetterGI 工具仍由本机 MCP 执行，外部服务不需要也无法反向访问 `127.0.0.1:5042`。

最少只需填写：

- 外部 URL：OpenAI-compatible 服务根地址、`/v1` 地址，或完整 `/chat/completions` 地址；
- API Key：发送到该外部服务的 Bearer Key。

模型 ID 可以留空。BetterGI 会从同一服务的 `/models` 读取列表并自动选择第一个，也可以在高级项中手动填写模型。模型会话、工具请求解析、参数绑定、工具执行、结果回填、多轮终止和错误恢复由 Microsoft Agent Framework / `Microsoft.Extensions.AI.FunctionInvokingChatClient` 负责。

DeepSeek 官方接口示例：

```text
外部 URL：https://api.deepseek.com
模型：deepseek-v4-pro 或 deepseek-v4-flash（也可留空自动读取）
```

两种 DeepSeek 地址均支持。为避免 OpenAI 兼容层把 DSML 工具标记当普通文本，`api.deepseek.com` 官方域名会统一自动使用 Anthropic Provider：

- `https://api.deepseek.com`：自动规范化为 `/anthropic`；
- `https://api.deepseek.com/anthropic`：Microsoft Agent Framework Anthropic Provider，使用 `tool_use/tool_result`，可避免 DeepSeek 把 DSML 工具标记当普通文本输出。

BetterGI 不会擅自给服务根地址追加 `/v1`；需要 `/v1` 的提供商请在 URL 中明确填写。Anthropic Provider 当前由微软以 preview 包发布，相关 `MAAI001` 仅在该实现文件局部取消。

Agent 工作方式：

```text
用户消息
  → Microsoft Agent Framework ChatClientAgent
  → OpenAI 或 Anthropic Provider（携带 BetterGI 85 个动态工具 Schema）
  → Microsoft.Extensions.AI 自动 function invocation
  → McpClientTool 通过本机 MCP 执行工具
  → Agent Framework 回填结果并继续，直到生成最终中文回复
```

Agent 的固定项目知识来自随程序发布的 `Assets/Config/AgentSystemPrompt.md`。页面中的“打开可编辑提示词文件”会首次复制为 `User/Agent/system-prompt.md`；用户文件存在时优先加载，保存后下一条消息立即生效，无需重启。

每次消息还会动态附加以下项目上下文，而不是把整个源码、配置 JSON 或仓库 JSON 塞给模型：

- BetterGI 当前版本和 MCP 工具数量；
- 设置叶子项和业务分区数量；
- 已安装 JS 和调度配置组数量；
- 当前仓库索引时间、节点数和动态根分类，或不可用原因；
- 截图器、独立任务、当前调度项目、暂停和取消状态；
- 实际使用的提示词文件路径。

提示词要求大数据通过分页搜索工具按需读取，并规定了设置、仓库、订阅、JS、调度器、独立任务、中断、确认参数和最终答复的完整路线。用户自定义提示词上限为 100000 字符。

“取消”按钮只取消本次外部请求。如果 Agent 已经启动游戏任务，应再要求它调用 `bgi_stop_current_task_and_wait`，或直接使用相应 MCP 停止工具。

Agent Framework 的 `AgentSession`、本机 `McpClient` 和工具列表会在同一配置下长期复用，因此前一轮查到的脚本、设置和工具结果会留在完整会话中，不会每条消息重新发现。可见用户/助手消息同时持久化到：

```text
User/Agent/conversation.json
```

完整 Microsoft Agent Framework Session（包括工具调用、结果和压缩摘要）同时序列化到：

```text
User/Agent/session.json
```

程序重启后优先恢复完整 Session；Provider 配置变化、Session 不兼容或文件超限时，退回 `conversation.json` 最近 40 条可见消息恢复。“新对话”会同时清空内存 Session 和两个磁盘缓存。

上下文采用三阶段自动压缩流水线：

1. `ToolResultCompactionStrategy`：优先把较旧的大型工具调用/结果折叠成简短记录；
2. `SummarizationCompactionStrategy`：达到阈值后使用当前模型生成中文执行记忆，保留目标、精确路径、设置变更和任务状态；
3. `TruncationCompactionStrategy`：摘要失败或仍超限时，硬性丢弃最旧消息组，同时保护最近消息及工具调用/结果配对。

默认限制：

| 项目 | 默认值 |
|---|---:|
| 摘要触发消息数 | 48 |
| 保留最近消息组 | 12 |
| 硬截断消息数 | 96 |
| `conversation.json` 最大消息数 | 80 |
| 可见对话总字符数 | 120000 |
| 单条可见消息最大字符数 | 20000 |
| `session.json` 最大字符数 | 500000 |

单条磁盘消息超限时保留开头和结尾并标记省略区；总量超限时从最旧消息开始丢弃。完整 Session 序列化超限时不写入磁盘，避免文件无界增长，当前进程内 Session 仍受 Agent Framework 压缩流水线限制。所有阈值可在 Agent 页“上下文压缩与缓存上限”中调整，也可通过 `agentConfig.*` 设置项修改。

聊天页只保留左右消息气泡、可复制文本、空状态、自动滚动、忙碌指示、Ctrl+Enter、当前模型、设置入口和新对话。模型连接、提示词、上下文压缩与缓存限制全部移到独立“Agent 设置”窗口，不占用日常对话区域。助手消息通过 Agent Framework `RunStreamingAsync` 增量更新同一个气泡，不是对最终字符串做假分片。

Agent UI 只显示最后一条不含 function call 的助手消息，不再把 Agent Framework 聚合的中间工具旁白拼到最终回答。系统提示词同时要求默认静默编排，并优先使用原子工具。

### 本机 Agent HTTP 接口

除 UI 外，本机程序可以直接调用同一个 Agent：

| 方法 | 地址 | 用途 |
|---|---|---|
| `GET` | `/agent/status` | 配置、模型、会话和提示词状态，不返回 API Key |
| `GET` | `/agent/models` | 从外部 Provider 读取模型列表 |
| `GET` | `/agent/conversation` | 读取持久化可见对话 |
| `DELETE` | `/agent/conversation` | 清空 AgentSession 和磁盘会话 |
| `POST` | `/agent/chat` | 直接发送消息并执行本机 MCP 工具 |
| `POST` | `/agent/chat/stream` | 使用 Server-Sent Events 流式发送 Agent 文本和工具活动 |

默认根地址为 `http://127.0.0.1:5042`。请求示例：

```json
POST /agent/chat
{
  "message": "检查游戏是否就绪，然后运行名为每日任务的配置组",
  "resetConversation": false
}
```

接口与 UI 复用同一个互斥 AgentSession；并发消息会串行处理。`resetConversation: true` 会先开启新会话。

流式接口接受相同 JSON，请求返回 `text/event-stream`：

- `started`：模型运行开始；
- `delta`：回答文本增量；
- `tool_activity`：本机工具名，只用于活动状态；
- `reset`：模型转入工具调用时丢弃之前的中间旁白草稿；
- `final`：框架聚合后的最后正式回答；
- `error`：流式错误。

UI 收到 `final` 后会用正式回答校正气泡。模型在决定调用工具前输出的过程文本不会丢失：`reset` 时会移动到独立“执行活动”区域；工具名也在该区域保留。文本增量会主动让出 UI 渲染帧，避免所有 chunk 在最后一瞬间同时显示。

配置保存于 `agentConfig`：

- `baseUrl`：外部接口地址；
- `apiKey`：外部 API Key，设置搜索会将其标记为敏感；
- `model`：可选模型 ID；
- `maxToolRounds`：单次消息最大本地工具调用轮数，默认 12。

## 6. MCP 声明与错误行为

所有显式工具均包含：

- 稳定的英文工具名；
- 中文工具说明；
- 每个业务参数的说明与自动生成 JSON Schema；
- `ReadOnly`、`Destructive`、`Idempotent`、`OpenWorld` 等 MCP tool annotations；
- `CancellationToken` 支持；
- 参数格式、路径、URL、危险操作确认等服务端校验。

参数错误、路径不存在、命令当前不可执行、未确认危险操作等情况会作为 MCP 工具错误返回，不需要客户端解析 BetterGI 日志文本来判断。

游戏运行工具默认在确认任务启动后结束工具调用，后台任务继续运行。显式等待工具仍可能保持调用；取消 MCP 请求不等于停止 BetterGI。需要真正中止时调用 `bgi_stop_current_task_and_wait(confirm: true)`。

## 7. 本机与外部连接说明

MCP 可以控制游戏输入、覆盖订阅脚本、修改设置：

- MCP 只监听 `127.0.0.1`，不使用本地 Token，也不要通过端口转发暴露；
- 内置 Agent 只做出站 HTTP 请求；外部服务无法直接连接本机 MCP；
- 外部 API Key 不应写入聊天消息、日志或提交到 Git；
- AI 客户端要求确认时，先阅读工具名、参数和影响；
- `includeSensitive + confirmSensitive` 会返回通知渠道凭据等秘密，只在确有必要时使用；
- 仓库与订阅更新只接受受限路径，但脚本本身仍属于可执行自动化内容，应只订阅可信仓库。

## 8. 开发者扩展方式

显式工具位于 `Service/Mcp`。新增工具类的基本形式：

```csharp
[McpServerToolType]
public sealed class ExampleTools
{
    [McpServerTool(Name = "bgi_example", ReadOnly = true, Idempotent = true)]
    [Description("面向模型和人的清晰说明。")]
    public static object Example(
        [Description("参数说明。")]
        string value)
    {
        return new { value };
    }
}
```

然后在 `McpHostedService` 的 `AddMcpServer()` 链中加入：

```csharp
.WithTools<ExampleTools>()
```

需要调用主应用服务时，通过构造函数注入 `McpApplicationServices`，再从 `Services` 获取现有 DI 服务。涉及 WPF 对象、Observable 属性或命令时，必须切换到 `Application.Current.Dispatcher`；耗时的纯 I/O/计算不要同步阻塞 UI 线程。

如果功能本来就是某个已注册 ViewModel 的 `[RelayCommand]`，它会自动进入命令目录；只有需要更稳定的业务语义、更严格参数模型或额外安全校验时，才需要再增加显式 MCP 工具。

## 9. 主要实现文件

- `Service/Mcp/McpHostedService.cs`：Streamable HTTP 托管、回环限制和 Host/Origin 校验；
- `Service/Agent/McpAgentService.cs`：Microsoft Agent Framework、官方 OpenAI Provider、模型发现和本机 MCP 工具接入；
- `Assets/Config/AgentSystemPrompt.md`：随程序发布的 BetterGI 项目知识和 Agent 行为规范；
- `View/Pages/AgentPage.xaml`：内置 Agent 的纯聊天主界面；
- `View/Windows/AgentSettingsWindow.xaml`：独立的模型连接、提示词和上下文限制设置窗口；
- `Service/Mcp/McpSystemTools.cs`：状态与取消工具；
- `Service/Mcp/McpConfigurationTools.cs`：设置发现、脱敏读取和持久化修改；
- `Service/Mcp/McpSettingsCatalog.cs`：635 项设置的 XML 注释、分区、默认值和结构化检索元数据；
- `Service/Mcp/McpRepositoryTools.cs`：仓库渠道、更新和订阅管理；
- `Service/Mcp/McpRepositoryIndex.cs`：动态大型 `repo.json` 扁平索引和自动失效；
- `Service/Mcp/McpRepositorySearchTools.cs`：目录浏览、多条件搜索、分面、精确解析和安全订阅；
- `Service/Mcp/McpSchedulerTools.cs`：调度器 CRUD、脚本发现、JS 设置与单项目执行；
- `Service/Mcp/McpGameTaskTools.cs`：自动战斗、秘境、Boss、钓鱼、伐木等独立任务的显式声明；
- `Service/Mcp/McpRuntimeControlTools.cs`：执行状态、等待、中断、暂停和紧急按键释放；
- `Service/Mcp/McpWorkflowTools.cs`：截图器、配置组、一条龙和兑换码；
- `Service/Mcp/McpCommandCatalog.cs`：自动 RelayCommand 目录与调用分发；
- `Helpers/CommandLineOptions.cs`：主实例默认 MCP、`--mcp` 兼容参数和端口设置；
- `Core/Script/ScriptRepoUpdater.cs`：线程安全的订阅清单修改接口。

官方资料：

- [Model Context Protocol](https://modelcontextprotocol.io/)
- [官方 C# SDK](https://github.com/modelcontextprotocol/csharp-sdk)
