# Pulonia 任务系统架构总览

状态：设计基线，按 2026-10-24 工作区代码核对。进度与验收结论只维护在[路线图](roadmap.md)。

实施状态：分层、核心对象、主流程和关键约束 3、4、6 按[实例、账户与路线 CD 统筹设计](../instance-account-model.md)改写，代码已于 2026-11-01 实施（路线图步骤 14），待用户验收。实现位置与遗留问题见[统筹设计第 15 节](../instance-account-model.md#15-实施记录)。

Pulonia 是 BetterGI 新的任务计划系统，命名空间 `BetterGenshinImpact.Pulonia`。普通玩家用“新一条龙”配置日常；高级玩家在“任务计划”里用任务树组合路线、JS、键鼠录制、独立任务、Shell 和 C#；开发者通过 `IPuloniaTaskService` 从代码提交与查询。两个页面共用同一计划模型、存储和执行语义，最终替代配置组、旧一条龙等旧入口。

## 分层

依赖方向自上而下：UI → 编辑会话 → 应用服务 → 执行 → 存储。下层原则上不引用上层，服务层不产出供逻辑判断的界面文案；现存例外列在[已知偏差](#已知偏差)。

| 层 | 主要类型 | 职责 |
| --- | --- | --- |
| UI | `PuloniaTaskPlanViewModel`（任务计划页）、`PuloniaOneDragonViewModel`（新一条龙页）、`PuloniaPlanTriggersViewModel`（两页共用的触发设置）、`PuloniaTaskHistoryViewModel`（执行记录）、`PuloniaTaskNodeAdvancedSettingsViewModel`；视图层窗口工厂 `PuloniaTaskSettingsDialogs`（实现 `IPuloniaTaskSettingsDialogs`） | 选择、菜单、弹窗输入和展示。页面 VM 不直接读写文件 |
| 编辑会话 | `PuloniaPlanEditingSession`（DI 单例）、`PuloniaTaskPlanDocumentViewModel`（一份计划）、`PuloniaTaskNodeViewModel` | 两个页面共用的文档集合、首次加载、自动保存、计划增删与导入、资源检查和运行提交；文档负责节点树、30 步撤销和有效参数预览 |
| 应用服务 | `IPuloniaTaskService` / `PuloniaTaskService`、`IPuloniaAccountService` / `PuloniaAccountService`、`PuloniaTaskTriggerHost`、`PuloniaTaskBuilder`、`PuloniaParameterResolver`、`PuloniaTaskRunStateStore`、`PuloniaTaskCreationService`、`PuloniaTaskResourceVersionService`、`PuloniaTaskLegacyImporter` | 本实例的提交与串行队列、快照准备、唯一的有效参数解析、本实例运行状态的唯一写入、本实例负责的定时检查、计划热键（只在主实例）、向子实例一步下发（只用于手动“运行于”）、新增任务构建、资源版本检查、旧数据导入；`PuloniaAccountService` 负责账户目录、实例当前账户、账户占用、切换前检查、UID 比对和账户进度读写 |
| 执行 | `PuloniaLocalNodeRunner`、`IPuloniaTaskExecutor` 的 6 个实现、`PuloniaGameTaskCoordinator` / `PuloniaGameTaskLease`、`PuloniaGenshinAccountSwitcher` | 每次节点尝试取游戏租约、核验 UID（指定账户）、调用执行器；账户切换，以及只读、不按键的 UID 读取（`TryReadUidAsync`） |
| 存储 | `PuloniaTaskStore`（账户部分拆为 `PuloniaTaskStore.Accounts`、`PuloniaTaskStore.AccountProgress` 两个 partial）、`ScriptRepositoryStore` | `User/Pulonia` 下的计划、设置方案、账户目录、实例设置与实例运行状态、账户进度（完成记录、首领领奖次数）、历史；脚本仓库注册、保留归档和 JS 工作目录 |

六个执行器及任务类型：`PuloniaPathingTaskExecutor`（`pathing`）、`PuloniaJavaScriptTaskExecutor`（`javascript`）、`PuloniaKeyMouseTaskExecutor`（`keymouse`）、`PuloniaSoloTaskExecutor`（`solo.<TaskId>`，独立任务）、`PuloniaShellTaskExecutor`（`shell`）、`PuloniaCSharpTaskExecutor`（`csharp`，目前只有 `sample.sum` 样例，去留待定 R2）。`group` 由服务编排，不对应执行器。

## 核心对象

| 对象 | 类型 | 说明 | 保存位置 |
| --- | --- | --- | --- |
| 计划 | `PuloniaTaskPlan` | 一棵任务树（`RootTask`）加触发器和运行账户设置（`account_id`，空为跟随当前账户）；`Purpose` 区分普通计划与一条龙 | `plans/<planId>.json` |
| 节点 | `PuloniaTask` | 分组或叶子任务；嵌套 `Source`（计划/目录引用）、`Policy`（超时与重试）；`Settings`（共享设置，向下传播）与 `ProfileId`（所挂方案）、`Parameters`（任务参数，只属于本任务） | 计划内 |
| 设置方案 | `PuloniaSettingsProfile` | 命名的共享设置集合，可挂在任意计划的任意节点上复用；设置项由代码中的 `PuloniaSettingCatalog` 定义 | `profiles/<profileId>.json` |
| 触发器 | `PuloniaTaskTrigger` | 定时或热键，嵌入计划；定时触发器的 `instances` 多选执行实例，由各执行实例自己检查；热键只在主实例注册和执行，`instances` 必须为空 | 计划内 |
| 账户 | `PuloniaGenshinAccountProfile` | 一个原神游戏账号：名称、UID、服务器（`server_time_zone`）和可选的登录资料；初始自带“我的账户”，它是唯一允许不填 UID 的账户 | `accounts.json` |
| 实例当前账户 | 实例设置 `current_account_id` | 用户设置的本实例游戏当前登录的账户；BetterGI 只在自己切换账户时改写 | `instances/<目录名>/settings.json` |
| 请求 | `PuloniaTaskRequest` | 一次提交：计划、范围、触发信息；不含执行实例（运行总在提交它的实例执行，记在运行记录的 `instance`），运行账户不在提交时确定 | 运行记录内 |
| 快照 | `PuloniaTaskSnapshot`（含 `PuloniaTaskPreparedTask` 树） | 提交时固定的展开树、有效参数、策略、资源版本和计划原文 | 运行记录内 |
| 运行记录 | `PuloniaTaskRunRecord` | 一次运行的状态、执行实例、运行账户（`account_id`、`account_source`、`uid_check`、`verified_uid`）、节点结果、本次完成的路线（`route_completions`）；界面通过只读包装 `PuloniaTaskRunView` 读取 | 实例运行状态或 `history/` |
| 节点结果 | `PuloniaTaskNodeResult` | 某个节点地址的一次尝试 | 运行记录内 |
| 实例运行状态 | `PuloniaTaskState` | 本实例的排队请求、活动运行、待归档运行，以及本实例负责的定时触发器的游标，按（计划、触发器）记录；不含完成记录 | `instances/<目录名>/state.json`（格式版本 7） |
| 完成记录 | `PuloniaRouteCompletion` | 某个账户最近一次完整执行成功某份路线的事实，每个（账户，路线）一条，可由用户清除 | `accounts/<账户 ID>/route-completions.json` |

字段与配置解析规则见[模型与配置](model.md)。

## 主流程

每个实例（主实例和子实例）都有自己的 Pulonia 协调器和队列，下面的流程在执行实例内完成。页面、定时、热键、命令行 `--pulonia-run` 或 C# 调用都向执行实例提交请求，本实例内的入口是 `IPuloniaTaskService.EnqueueAsync`。定时触发器由执行实例自己检查，到点在本实例提交；计划热键只在主实例注册，按下后在主实例提交；两者都不经过 IPC。只有主实例在界面上选择子实例手动运行（“运行于”）时，经 `pulonia.run.submit` 一步下发；子实例校验计划修订号后放入本实例队列，之后不再依赖主实例。

服务读取已保存的计划，由 `PuloniaTaskBuilder` 展开分组轮次、计划引用和目录引用，用 `PuloniaParameterResolver` 解析每个叶子的有效参数，固定资源指纹，生成不可变快照；请求与快照经 `PuloniaTaskRunStateStore` 写入本实例的 `state.json` 后才进入本实例的串行队列。出队时复核触发配置和截止时间；含游戏节点的运行由 `PuloniaAccountService` 确定运行账户并取得账户占用：跟随当前账户时取实例当前账户的最新设置并做 UID 比对，指定账户时按需切换并核验 UID，成功后改写实例当前账户。然后深度优先执行快照树：每个叶子先按运行账户的完成记录判定路线 CD，再由 `PuloniaLocalNodeRunner` 取游戏租约、核验 UID（指定账户）、调用执行器；路线完成后先在 `.writer.lock` 内写完成记录，再把节点结果写入本实例的 `state.json`。提交前可用 `PreviewAsync` 按同一份快照规则预测哪些节点会因 CD 跳过。全部节点结束后由 `ResolveFinalStatus` 判定最终状态，运行先进入 `pending_archives`，写入所有实例共用的历史目录后移出，账户占用随运行结束释放。子实例只向主实例回报主实例下发的运行（`pulonia.run.changed`），用于显示。

```mermaid
flowchart TB
    subgraph UI["UI"]
        PlanVM["PuloniaTaskPlanViewModel"]
        OD["PuloniaOneDragonViewModel"]
        Trig["PuloniaPlanTriggersViewModel"]
        Hist["PuloniaTaskHistoryViewModel"]
    end
    subgraph Edit["编辑会话"]
        Session["PuloniaPlanEditingSession<br/>加载 · 自动保存 · 资源检查 · 运行提交"]
        Doc["PuloniaTaskPlanDocumentViewModel<br/>节点树 · 撤销 · 有效参数预览"]
    end
    subgraph App["应用服务"]
        Host["PuloniaTaskTriggerHost<br/>1 秒检查本实例定时 · 计划热键（只在主实例）· 命令行"]
        Svc["IPuloniaTaskService<br/>PuloniaTaskService：本实例串行队列 · 编排 · 续跑 · 一步下发"]
        Acc["IPuloniaAccountService<br/>PuloniaAccountService：运行账户 · 账户占用 · UID 比对 · 账户进度"]
        Builder["PuloniaTaskBuilder"]
        Resolver["PuloniaParameterResolver"]
        RunState["PuloniaTaskRunStateStore"]
        ResVer["PuloniaTaskResourceVersionService"]
    end
    subgraph Exec["执行"]
        Local["PuloniaLocalNodeRunner"]
        Coord["PuloniaGameTaskCoordinator"]
        Ex["6 个 IPuloniaTaskExecutor"]
        Sw["PuloniaGenshinAccountSwitcher"]
    end
    subgraph Child["子实例（结构相同）"]
        CSvc["IPuloniaTaskService<br/>子实例自己的队列 · 编排 · 本实例定时"]
    end
    subgraph Store["存储"]
        PS["PuloniaTaskStore"]
        RS["ScriptRepositoryStore"]
    end
    PlanVM --> Session
    OD --> Session
    Trig --> Session
    Session --> Doc
    Doc --> Resolver
    Session --> ResVer --> RS
    Session --> PS
    Session --> Svc
    Hist --> Svc
    Trig -. 读取状态 .-> Host
    Host --> Svc
    Svc --> Builder --> Resolver
    Builder --> PS
    Builder --> RS
    Svc --> RunState --> PS
    Svc --> Acc --> PS
    Acc --> Sw
    Svc --> Local
    Local --> Coord
    Local --> Sw
    Local --> Ex
    Ex --> RS
    Svc -. "IPC pulonia.run.submit / cancel（只用于手动运行于）" .-> CSvc
    CSvc -. "IPC pulonia.run.changed" .-> Svc
```

## 关键约束

1. **一份 `PuloniaTaskPlan`，两种界面。** 一条龙配置是 `Purpose=OneDragon` 的计划，界面沿用旧 `OneDragonFlowPage` 左右布局；执行器没有用途分支。
2. **快照不可变。** 运行只读提交时固定的快照，编辑、设置方案或资源变化不影响已排队和正在运行的请求；续跑沿用原快照。
3. **每个实例自己协调，实例之间并行。** 每个实例（含子实例）都有自己的队列、快照、运行账户确定、执行、运行状态和历史写入；同一实例内串行，只有一个活动运行；不同实例之间并行，同一账户由账户占用（`accounts/<账户 ID>/run.lock`）保证不同时运行。主实例只做一步下发，断开或退出不取消子实例已接受的运行。
4. **实例运行状态单写者，账户进度在写锁内读改写。** `instances/<目录名>/state.json` 只由本实例的 `PuloniaTaskRunStateStore` 写入；完成记录和首领领奖次数任意实例都可写，在 `.writer.lock` 内读改写，先写完成记录再写节点结果；配置类文件（计划、设置方案、账户目录、偏好、仓库注册）只需原子替换加后写者胜。
5. **唯一参数解析。** 运行准备与界面预览共用 `PuloniaParameterResolver`，规则只写在[模型与配置](model.md#有效参数解析规则)。
6. **唯一 CD 规则。** 路线 CD 是当前唯一的执行前资源限制，由刷新规则（计划设置）和运行账户的完成记录推算，不单独存储；状态只由 `PuloniaRouteCooldown.GetStatus` 判定，日切时区取运行账户的服务器。通用额度框架已于 2026-10-24 删除。
7. **不引入 Quartz、SQLite、ORM 或工作流框架。** 调度用 .NET 定时循环，存储用 JSON 文件。

## 文档地图

| 文档 | 内容 |
| --- | --- |
| [实例、账户与路线 CD 统筹设计](../instance-account-model.md) | 实例、账户、配置与路线 CD 的关系；运行账户、账户占用、一步下发、触发器的执行实例；旧数据处理（不迁移）、实施顺序与实施记录 |
| [用户故事与边界](requirements.md) | 13 条故事、范围约束、边界分级、复杂规则来源追踪 |
| [模型与配置](model.md) | 计划、节点、共享设置与设置方案、触发器、运行账户设置的字段；任务树语义；唯一的有效参数解析规则 |
| [执行、恢复与 API](execution.md) | 本实例串行队列、节点执行顺序、游戏租约、一步下发、取消、最终状态、续跑、C# 接口 |
| [触发与热键](scheduling.md) | 定时、补触发、忙碌策略、热键、命令行入口、触发器的执行实例 |
| [多账户](accounts.md) | 账户目录与“我的账户”、实例当前账户、运行账户设置、切换与 UID 核验、UID 比对、账户占用、凭据与图片 |
| [结果、CD 与存储](results-and-storage.md) | 完成合同、路线 CD（刷新规则、状态、清除、运行预览）、完成记录、实例运行状态、历史与文件布局 |
| [脚本仓库与任务资源](repository-resources.md) | 仓库注册、同步与更新任务资源、保留归档、锁、资源指纹 |
| [标识与磁盘路径](identifiers-and-paths.md) | 短 ID 规则、资源目录布局、路径长度预算 |
| [界面](user-interface.md) | 任务计划页、新一条龙页、触发设置、新增任务与目录添加 |
| [重构与迁移](migration.md) | 旧代码解耦、旧配置导入 |
| [调研依据](research.md) | 社区 issues、JS 脚本、本体与 d-v3 代码依据 |
| [路线图](roadmap.md) | 步骤状态、验收规则、待决定事项、2026-10-24 改造摘要 |

后续能力（提案，未实现）：[体力计划](proposals/resin-plan.md)、[目标托管](proposals/managed-hosting.md)。已实现的专题设计：[共享设置与设置方案](proposals/shared-settings.md)（独立任务绑定未做）、[路线 CD：识别、可见性与简化](proposals/cooldown-visibility.md)（一条龙清单不改）。过程记录与被替换的旧文原文见[归档](archive/README.md)。

## 已知偏差

以下与上述分层不一致，代码中仍存在，尚未排期：

- `PuloniaOneDragonViewModel.ImportLegacyAsync` 仍读取全局 `ConfigService.Config`。
- `PuloniaTaskPlanViewModel.EditNodeConfiguration` 直接创建 `PuloniaTaskNodeSettingsDialog`。
- `PuloniaGameTaskCoordinator` 与 `TaskRunner` 的会话代码仍各有一份，合并为 `GameTaskLease` 见 [task-contract.md](../task-contract.md) 步骤 D。
- 服务与执行层的宿主依赖：`PuloniaTaskTriggerHost` 用 WPF `Dispatcher` 在 UI 线程注册热键，`PuloniaTaskHotkeyValidator` 引用 `System.Windows.Input`；`PuloniaGameTaskCoordinator` 引用 `BetterGenshinImpact.View`（任务模式的遮罩）；`PuloniaPathingTaskExecutor` 读取 `MapPathingViewModel.PathJsonPath` 作为路线逻辑路径的根。

## 维护方式

新增需求先更新[用户故事](requirements.md)并在“复杂规则 → 用户故事”表登记来源，再修改所属专题；涉及步骤范围变化时同步[路线图](roadmap.md)。基线文档只写当前事实与规则，过程记录放进 `archive/`，未实现的设计放进 `proposals/`。按[统筹设计](../instance-account-model.md)改写的部分已于 2026-11-01 实施，各文档的“实施状态”行只说明实施日期和验收状态。调研依据的代码版本见[调研依据](research.md)。
