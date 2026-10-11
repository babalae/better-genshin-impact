# Pulonia：社区需求与现有实现依据

[返回总览](../automation-system.md) · [用户故事](requirements.md) · [开发与验收计划](development-plan.md)

本文件保留 2026-10-02 核查的 23 个 issues、10 个 JS 脚本以及本体和 d-v3 代码依据。它解释需求来源；当前设计决策以各专题为准，不直接照搬脚本的成功推断或配置方式。

## GitHub issues 补充的用户故事与验收要求

2026-10-02 检索主仓库 [GitHub issues](https://github.com/babalae/better-genshin-impact/issues)，阅读下列条目的正文，并检查与判断有关的讨论。下表是对需求的归纳，不是对所有报告问题的复现结论。状态为本次查询时的状态；已关闭条目作为历史回归样例，开放也不表示当前代码一定尚未处理。机器人生成的分析不作为用户需求或技术结论依据。

| 关联故事 | 来源与用户反馈 | 纳入设计的用户故事 / 验收补充 |
| --- | --- | --- |
| US-01 培养与树脂规划 | [#3396 按提升指南选秘境](https://github.com/babalae/better-genshin-impact/issues/3396)、[#3716 体力计划](https://github.com/babalae/better-genshin-impact/issues/3716)（均开放）：多角色少量材料需要频繁换副本，希望按需求安排多个秘境和次数。 | 我不需要记住材料来自哪个副本，也不希望一天的树脂被第一个目标全部耗尽；先展示目标、开放日、预计次数和共用预算。提升指南可作为后续目标来源，不强制变成首版扫描依赖。 |
| US-04 组合与复用 | [#678 子配置组](https://github.com/babalae/better-genshin-impact/issues/678)、[#1234 配置组优化](https://github.com/babalae/better-genshin-impact/issues/1234)、[#2924 重复添加任务](https://github.com/babalae/better-genshin-impact/issues/2924)（均开放）：希望嵌套组合、显式换队、重复安排邮件/奖励等动作。 | 我能直接插入“换队”能力，前后各放一次同种任务，并引用一组现成步骤；不能靠先放一条无关路线触发隐藏初始化。重复节点有独立配置与进度，实际资源资格仍共享。 |
| US-09 设置可信 | [#3168 未覆盖时未按预期继承](https://github.com/babalae/better-genshin-impact/issues/3168)、[#3235 切换预设后界面不更新](https://github.com/babalae/better-genshin-impact/issues/3235)、[#3323 设置过多](https://github.com/babalae/better-genshin-impact/issues/3323)（均开放）。 | 我切换计划/预设后看到的必须就是将要执行的参数；公共设置和节点差异可展开查看。吸收 #3323 的设置展示诉求，不把其全部专项功能建议扩成执行内核需求。 |
| US-10 大目录管理 | [#2530 路线折叠](https://github.com/babalae/better-genshin-impact/issues/2530)（开放，讨论中已指出分支实现）、[#1307 大量项目删除卡住并读档失败](https://github.com/babalae/better-genshin-impact/issues/1307)（已关闭）。 | 我能按目录折叠、批量操作；取消或操作失败后原配置仍完整。采用 d-v3 已有树交互，不把“已有分支实现”误写成全新需求。 |
| **US-12 编辑成果可保留（补充）** | [#2204 跨配置组复制粘贴](https://github.com/babalae/better-genshin-impact/issues/2204)、[#2205 删除撤销](https://github.com/babalae/better-genshin-impact/issues/2205)（开放）、[#2797 复制后 JS 配置异常](https://github.com/babalae/better-genshin-impact/issues/2797)（已关闭）。 | 作为已花时间配好任务的用户，我希望跨计划复制子树时保留完整参数类型，误删/误拖后能撤销；无需重启软件或编辑 JSON 才恢复。复制默认生成新节点 ID，引用复用是另一项明确操作。 |
| US-02、08 日程与热键 | [#1678 定时启动一条龙](https://github.com/babalae/better-genshin-impact/issues/1678)、[#1207 一条龙快捷键和次数](https://github.com/babalae/better-genshin-impact/issues/1207)（开放）。 | 我能给计划或子树设置时间/热键，并决定忙碌时怎么办。到点默认排队；若选择切换到新计划，必须先确认旧任务停止并释放输入，不直接抢占。 |
| US-03、05 可判断的结果 | [#3662 自动钓鱼失败需要返回值](https://github.com/babalae/better-genshin-impact/issues/3662)、[#3735 JS 路线与直接路线统计不一致](https://github.com/babalae/better-genshin-impact/issues/3735)（开放）。 | 作为脚本作者，我希望知道“未进入钓鱼”“路线失败”“完成但未确认”等原因，才能决定重试或记 CD；同一动作不因从 JS 间接调用就绕过额度检查，也不被父子两层重复统计。 |
| US-05 外部控制 | [#1596 开放 Web 接口](https://github.com/babalae/better-genshin-impact/issues/1596)（开放）：希望无需远程桌面就能启动、停止和查看任务。 | 保留外部客户端使用同一任务服务的方向；本次先交付 C# 与本机通信。HTTP/远程面板是后续传输适配，不为它增加第二套运行模型，也不承诺无交互桌面运行游戏。 |
| US-07 恢复与结束 | [#2941 登录网络异常后重试](https://github.com/babalae/better-genshin-impact/issues/2941)、[#3518 日切月卡阻断长 JS](https://github.com/babalae/better-genshin-impact/issues/3518)、[#2485 卡住影响次日任务](https://github.com/babalae/better-genshin-impact/issues/2485)（均开放）。 | 我不需要给每个脚本各写一份月卡处理；公共恢复由宿主协调。计划有总时限，恢复失败后保留进度、结束或待处理；停止未完成时不能强行开始下一次。 |
| US-01、07 前置条件不足 | [#3305 圣遗物背包满导致一条龙卡住](https://github.com/babalae/better-genshin-impact/issues/3305)（开放）。 | 我希望预算耗尽、背包空间不足或内容不可用时给出原因，能继续不依赖这些条件的任务；不能把“无法领取”记作完成。清理背包需选择具体清理任务和参数，不是通用恢复器的默认动作。 |
| US-11 账号配置 | [#1933 多账号一条龙配置区分](https://github.com/babalae/better-genshin-impact/issues/1933)、[#2924 同一条龙处理大小号](https://github.com/babalae/better-genshin-impact/issues/2924)（开放）。 | 我的账号身份、目标与预设应显式绑定。吸收按 UID 选择配置的需求；“UID 识别失败就继续用当前配置”不用于无人值守多账号流程，避免串号。 |

## JS 脚本已经表达出的需求

本体仓库的 `Core/Script/ScriptRepoUpdater.cs` 指向官方内容仓库 [bettergi-scripts-list](https://github.com/babalae/bettergi-scripts-list)。本地编译输出中也存在该仓库的缓存；为便于复核，以下统一使用远端提交 [`6be613e7dbb3debf7b242146fbee43ddd8a1b4a1`](https://github.com/babalae/bettergi-scripts-list/commit/6be613e7dbb3debf7b242146fbee43ddd8a1b4a1) 的 README、manifest 和相关执行代码。这里只做静态阅读，没有运行这些脚本或调用它们的外部服务。

“观察”描述脚本当前提供或文档要求的行为；“设计启示”是本概要的归纳。脚本里的刷新常量、成功推断和配置技巧不能直接视为游戏事实或可靠执行合同。

| 脚本与可核查来源 | 观察到的需求和现有做法 | 对新系统的设计启示 |
| --- | --- | --- |
| `AutoPlan` 自动体力计划：[说明][js-plan-doc]、[入口代码][js-plan-code] | 按队伍、目标、星期、次数和优先级安排多个内置任务；支持 UID 配置。入口还检查圣遗物空间和活动可用性。 | US-01、09、11：将目标和计划做成可编辑表单，预算与账号统一管理；无需让新手填写分隔符表达式或部署额外配置站点。 |
| 角色养成一条龙：[说明][js-development-doc]、[代码][js-development-code] | 用户先从养成计算器抄材料总量，脚本检查数量并刷取；说明要求未刷完不要使用材料，且秘境配置仍需在独立任务页调整。 | US-01、09：区分角色培养目标与简单库存目标，允许刷新已变化的练度/库存并重算缺口；在当前计划直接显示所用参数。不能把“用户期间不玩游戏”当作系统成立前提。 |
| `CD-Aware-AutoGather`：[说明][js-gather-doc]、[执行判定][js-gather-code] | 订阅后扫描材料，按地区/类别批选，按目标库存和世界身份管理刷新；代码在部分场景用位移或耗时推断路线执行情况。 | US-03、10、11：吸收材料目录、目标数量和世界归属；扫描资源应是资源索引工作，不要求启动游戏任务生成菜单。位移/耗时只算估计，不能证明采集成功。 |
| 采集 CD 管理：[说明][js-cd-doc]、[代码][js-cd-code] | 自行组织路径组、每日采集数量、队伍、优先级、禁用/结束时段、各账号 CD，并根据拾取和耗时调整路线；初始化还需复制路线、运行一次刷新自定义配置。 | US-01、02、03、04、10：明确“今天新采多少”与“背包补到多少”的区别；支持子组配置和共享 CD，把文件搬运与刷新菜单的步骤收回任务库。路线效率优化属于规划策略，不增加执行服务层。 |
| 背包材料统计：[说明][js-inventory-doc]、[代码][js-inventory-code] | 结合库存、目标数量、CD 选择路线；维护黑名单、弹窗处理和路径检测码，试图保留改名路线的记录；使用前仍需订阅并复制路线。 | US-01、03、07、10：复用带观测时间的库存快照、资源标识和公共弹窗处理。未知数量不能默认按 0 消费预算；路径指纹可辅助迁移，不能取代真实资源 ID。 |
| `AutoHoeingOneDragon` 锄地一条龙：[说明][js-hoeing-doc]、[执行结果处理][js-hoeing-code] | 不同路径组共享账号目标、各用队伍/时间安排；路线记录要结合宿主返回值、坐标和回主界面检查，README 明确说明宿主不返回结果时无法可靠确认成功。 | US-03、04、05、09：让脚本拿到统一结果、共享额度和子步骤记录；组级目标不能重复分配为每组一份。成功走完和实际掉落数量继续分开。 |
| `AccountSwitchStateMachine`：[说明][js-account-doc]、[切换后核验][js-account-code] | 以状态机切换账号，支持 UID 校验；UID 不符时可通过配置的停止热键尝试终止配置组。 | US-07、11：切号结果必须直接约束后续执行，不能让脚本模拟“停止快捷键”才能防止串号；停止、失败和待人工登录使用统一状态。 |
| `waitUntilSpecifiedTime`：[说明][js-wait-doc]、[代码][js-wait-code] | 在任务序列中等待到某个钟点，并区分今日已过时立即跳过或等到次日；实际通过长时间 `sleep` 等待。 | US-02：日程、过期和再排队应是调度职责；等待未来时刻时不占游戏输入，也不阻塞其他已经到期的请求。长等待脚本不作为新定时功能的内部实现。 |
| 每月自动兑换抽卡资源：[manifest][js-month-manifest]、[代码][js-month-code] | 按 UID 保存每月兑换记录，兑换资源不足时通知，未刷新时跳过。 | US-03、11：月周期也是同一套账号账本规则；通用执行器记录结构化结果，具体兑换能力负责确认商品、数量和余额。能力进入模板需显式选择，不因“托管”就自动纳入所有购买行为。 |
| `AutoPathingLoader-MultiUser`：[说明][js-coop-doc]、[代码][js-coop-code] | 多人联机同步路线，需要一致的路线文件并校验内容，区分队长/队员和所在世界。 | US-03、11：资源归属不能只看当前登录账号，运行应固定路线版本。多人同步是独立扩展需求，本次多账号先做单会话顺序切号，不扩成多机调度系统。 |

## 现有代码中的依据

| 现状 | 代码位置 | 对设计的影响 |
| --- | --- | --- |
| 配置组在执行具体项目之前统一启动游戏任务环境 | `Service/ScriptService.cs:128`、`:154` | 新执行器不能调用整个 `RunMulti` 作为核心实现 |
| 通用 TaskRunner 初始化要求截图器已启动，并管理全局取消与运行上下文 | `GameTask/TaskRunner.cs:46`、`:147` | 将任务生命周期与游戏生命周期分开 |
| 一条龙由 ViewModel 编排；任务项按名字选择实现、读取其他 ViewModel | `ViewModel/Pages/OneDragonFlowViewModel.cs:573`、`Model/OneDragonTaskItem.cs:57` | 执行业务从页面抽出，用稳定能力 Key 注册 |
| JS 内部任务调用会获取 `TaskSettingsPageViewModel` | `Core/Script/Dependence/Dispatcher.cs:132` | 开发者 API 必须依赖参数与服务，不能依赖页面初始化 |
| 路线成功使用 `SuccessEnd`，其他类型正常返回后统一设为成功 | `Core/Script/Group/ScriptGroupProject.cs:195`、`:248`、`:317` | 引入统一结果，并区分执行结束与目标确认 |
| 执行记录保存为每日 JSON；包含组名、文件名，没有账号字段 | `GameTask/LogParse/ExecutionRecord.cs`、`ExecutionRecordStorage.cs` | 新账本按账号、游戏资源和周期组织，与当前进度在同一运行状态文件中保存 |
| 添加路线会递归建立完整 UI 树，逐项调度到 UI 线程 | `ViewModel/Pages/ScriptControlViewModel.cs:911`、`:985` | 建立资源索引、按需加载、虚拟化；这是代码层面的瓶颈判断，尚未做性能测量 |
| 已有运行环境服务及 Provider | `GameTask/Runtime/GameRuntimeService.cs:25` | 在其上增加资源租约与会话准备，保留已有 Win32/WebPage 分支 |
| 已有角色练度、背包物品计数、树脂及 UID 识别 | `GameTask/CharacterDevelopment/CharacterDevelopmentTask.cs:88`、`GameTask/Common/Job/CountInventoryItem.cs:50`、`GameTask/Common/ResinRecognition.cs:31`、`Core/Script/Dependence/Genshin.cs` | 可以逐步接入托管，但不代表已有完整账号资产扫描 |
| 已有可处理界面状态、重试和超时的状态机 | `GameTask/Common/StateMachine/README.md` | 复用任务内部状态机，不再另建一套通用状态机框架 |

上述路径均相对 `BetterGenshinImpact/`。这是架构梳理，不是对现有代码进行完整缺陷审查。

### d-v3 中直接吸收的设计

以下位置均指 `d-v3@b7aa7fc66`，以代码实际行为为准；分支中的 `GearTaskExecutor_Usage.md` 与现有实现有差异，不作为实现依据。

| 已有实现 | 采用方式 | 分支代码位置（相对 BetterGenshinImpact/） |
| --- | --- | --- |
| 计划元信息 + `root_task`，节点递归保存 `children` | 保留 JSON 的整体形状，补充稳定 ID、版本和明确的参数类型 | `Model/Gear/GearTaskData.cs:11`、`:38` |
| 左侧计划列表，右侧任务树表 | 保留“名称、类型、启用、最近结果、操作”的同一行展示；参数按需打开 | `View/Pages/GearTaskListPage.xaml:462`、`:653` |
| 拖入分组、节点前后插入、展开状态 | 保留交互；补上禁止拖入自身/后代、移动后保存一次 | `View/Pages/Component/GearTaskDragDropHandler.cs` |
| 地图路线搜索、目录 README/文件 JSON 预览、目录任务数量 | 合并到新任务选择器；代码/JSON 预览作为高级页签 | `View/Windows/GearTask/PathingTaskSelectionWindow.xaml` |
| 按目录引用、逐个添加、保留目录结构 | 保留能力，收敛成“引用目录 / 展开为任务”两种模式及一个结构选项 | `ViewModel/Windows/GearTask/PathingTaskSelectionViewModel.cs:390`、`:459` |
| JS 目录摘要、搜索、README/源码按页签加载 | 保留按需读详情；异步结果绑定当前选项，避免快速切换后显示旧内容 | `ViewModel/Windows/GearTask/JsScriptSelectionViewModel.cs:158` |
| 树上最近结果、历史详情、断点、继续执行/从头执行 | 保留；补充结果证据和任务版本变化提示 | `View/Pages/GearTaskListPage.xaml:695`、`:744` |
| 定时与热键分开展示，显示关联任务和启用状态 | 保留；关联对象改为稳定 ID，默认展示可读日程而非 Cron | `View/Pages/GearTriggerPage.xaml` |
| 每次执行一个 JSON 历史文件，临时文件后替换 | 沿用文件存储思路，补上单写入者及关键状态一致性 | `Service/GearTask/Execution/GearTaskHistoryStore.cs:28` |
| 路线任务可报告内部恢复位置 | 保留可选检查点；首版先确保节点续跑可靠 | `Model/Gear/Tasks/PathingGearTask.cs:12`、`Service/GearTask/Execution/GearTaskExecutionModels.cs:173` |
| C# 方法调用接入同一任务树 | 保留扩展入口，采用显式注册的强类型方法或执行器 | `Model/Gear/Tasks/CSharpReflectionGearTask.cs:19` |

这些是从 XAML、ViewModel 和执行代码确认的交互设计，不代表已经验证实际渲染效果或大目录性能。路线选择器的 `LoadDirectChildrenFromRepo` 目前仍递归加载目录，不能据此认定它已经实现懒加载。

### 保留树结构，收敛实现层次

分支的实际执行链包含存储模型、ViewModel、`GearTaskData`、`GearTaskConverter`、`GearTaskFactory`、`BaseGearTask` 树、`GearTaskExecutor`、`GearTaskExecutionRunner`；记录又经过事件总线与后台 Recorder。职责有价值，但这些内容不必逐层对应一个可扩展框架。

新方案保留一个持久化任务模型、薄 ViewModel、统一任务服务和类型执行器。执行前允许集中构建只读执行树，用于展开引用、解析配置和参数、校验及固定版本；具体形式在模型阶段验收。简化重点是合并仅做转发的转换与调度层，不因存在可执行树就否定 d-v3。保存服务不返回 ViewModel；关键结果直接等待保存后推进，界面进度使用普通事件或 `IProgress<T>`。

另外保留以下改进边界：节点标识不再采用 `0/1/2` 这类随排序变化的位置；配置不再强制复用旧 `ScriptGroupConfig`；不在入口统一 `StartGameTask()`；参数错误不能默默退回默认值；节点创建或参数转换失败不能变成一个被禁用后跳过的错误占位任务。

[js-plan-doc]: https://github.com/babalae/bettergi-scripts-list/blob/6be613e7dbb3debf7b242146fbee43ddd8a1b4a1/repo/js/AutoPlan/README.md
[js-plan-code]: https://github.com/babalae/bettergi-scripts-list/blob/6be613e7dbb3debf7b242146fbee43ddd8a1b4a1/repo/js/AutoPlan/main.js
[js-development-doc]: https://github.com/babalae/bettergi-scripts-list/blob/6be613e7dbb3debf7b242146fbee43ddd8a1b4a1/repo/js/角色养成一条龙/README.md
[js-development-code]: https://github.com/babalae/bettergi-scripts-list/blob/6be613e7dbb3debf7b242146fbee43ddd8a1b4a1/repo/js/角色养成一条龙/main.js
[js-gather-doc]: https://github.com/babalae/bettergi-scripts-list/blob/6be613e7dbb3debf7b242146fbee43ddd8a1b4a1/repo/js/CD-Aware-AutoGather/README.md
[js-gather-code]: https://github.com/babalae/bettergi-scripts-list/blob/6be613e7dbb3debf7b242146fbee43ddd8a1b4a1/repo/js/CD-Aware-AutoGather/main.js#L891-L1008
[js-cd-doc]: https://github.com/babalae/bettergi-scripts-list/blob/6be613e7dbb3debf7b242146fbee43ddd8a1b4a1/repo/js/采集cd管理/README.md
[js-cd-code]: https://github.com/babalae/bettergi-scripts-list/blob/6be613e7dbb3debf7b242146fbee43ddd8a1b4a1/repo/js/采集cd管理/main.js
[js-inventory-doc]: https://github.com/babalae/bettergi-scripts-list/blob/6be613e7dbb3debf7b242146fbee43ddd8a1b4a1/repo/js/背包材料统计/README.md
[js-inventory-code]: https://github.com/babalae/bettergi-scripts-list/blob/6be613e7dbb3debf7b242146fbee43ddd8a1b4a1/repo/js/背包材料统计/main.js
[js-hoeing-doc]: https://github.com/babalae/bettergi-scripts-list/blob/6be613e7dbb3debf7b242146fbee43ddd8a1b4a1/repo/js/AutoHoeingOneDragon/README.md#L476-L510
[js-hoeing-code]: https://github.com/babalae/bettergi-scripts-list/blob/6be613e7dbb3debf7b242146fbee43ddd8a1b4a1/repo/js/AutoHoeingOneDragon/main.js#L1980-L2088
[js-account-doc]: https://github.com/babalae/bettergi-scripts-list/blob/6be613e7dbb3debf7b242146fbee43ddd8a1b4a1/repo/js/AccountSwitchStateMachine/README.md
[js-account-code]: https://github.com/babalae/bettergi-scripts-list/blob/6be613e7dbb3debf7b242146fbee43ddd8a1b4a1/repo/js/AccountSwitchStateMachine/main.js#L359-L382
[js-wait-doc]: https://github.com/babalae/bettergi-scripts-list/blob/6be613e7dbb3debf7b242146fbee43ddd8a1b4a1/repo/js/waitUntilSpecifiedTime/README.md
[js-wait-code]: https://github.com/babalae/bettergi-scripts-list/blob/6be613e7dbb3debf7b242146fbee43ddd8a1b4a1/repo/js/waitUntilSpecifiedTime/main.js
[js-month-manifest]: https://github.com/babalae/bettergi-scripts-list/blob/6be613e7dbb3debf7b242146fbee43ddd8a1b4a1/repo/js/每月自动兑换抽卡资源/manifest.json
[js-month-code]: https://github.com/babalae/bettergi-scripts-list/blob/6be613e7dbb3debf7b242146fbee43ddd8a1b4a1/repo/js/每月自动兑换抽卡资源/main.js
[js-coop-doc]: https://github.com/babalae/bettergi-scripts-list/blob/6be613e7dbb3debf7b242146fbee43ddd8a1b4a1/repo/js/AutoPathingLoader-MultiUser/README.md
[js-coop-code]: https://github.com/babalae/bettergi-scripts-list/blob/6be613e7dbb3debf7b242146fbee43ddd8a1b4a1/repo/js/AutoPathingLoader-MultiUser/main.js
