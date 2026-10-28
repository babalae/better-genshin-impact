# Pulonia：结果、CD 与存储

[返回总览](README.md) · [用户故事](requirements.md) · [路线图](roadmap.md)

明确什么时候算完成、什么时候可以重做，以及如何用 JSON 文件保存一致的进度和账本。2026-10-24 并入原 `collection-cooldown.md`（原文与路线返回值改造的影响评估见[归档](archive/collection-cooldown.md)），按当前代码核对；文件级路线 CD 一节于 2026-10-28 按改造后的代码重写。不引入 Quartz、SQLite 或 ORM。

## 三个问题分别回答

| 问题 | 由谁处理 | 示例 |
| --- | --- | --- |
| 什么时候发起？ | 触发器 `PuloniaTaskTrigger` | 每天 04:00 |
| 现在能不能开始？ | 请求准入与节点开关 | 触发器仍启用、未超过截止时间、节点已启用 |
| 这个资源现在能不能再做？ | 文件级路线 CD 与账本 | 这份路线文件距上次成功还没到刷新时间 |

“每周跑一次”是执行偏好，“某奖励每周只能领一次”是游戏事实，两者分开处理。

## 完成合同

执行器返回 `PuloniaTaskOutcome`，完成程度 `Kind` 为 Succeeded、ExecutedUnverified、PartiallySucceeded、Failed、Cancelled、Skipped、NeedsAttention，另带消息、结构化数据、证据和机器可读原因码 `ReasonCode`（如“树脂不足，未执行挑战”）。Succeeded 与 ExecutedUnverified 都让节点记为成功，但只有 Succeeded 能产生路线 CD，续跑时也只有 Succeeded 的节点视为已完成。

| 场景 | 可以确认什么 | 处理 |
| --- | --- | --- |
| Shell 退出码为 0 | 进程按约定成功 | 节点成功，不推出任何游戏资源完成 |
| 地图追踪整份文件执行成功 | 路线执行完毕 | 按文件估算采集完成，写入路线 CD |
| 地图追踪中途放弃、重试耗尽、取消、超时 | 文件未完整执行 | 不写 CD，下次可整份重跑 |
| 旧 JS、键鼠录制正常返回 | 脚本执行结束 | 默认“已执行、未核验”，不写 CD |
| 独立任务返回结构化结果 | 按任务各自的结果合同 | 首领讨伐的累计领奖次数由编排层单独记录 |

通用的完成事件报告、额度规则、执行前操作意图和未决操作已于 2026-10-24 删除：唯一的声明方是示例任务，生产能力从未使用。其他能力需要次数或周期限制时，按各自的完成证据单独实现，不预设统一框架。设计这类规则时仍要区分滚动 CD、按服务器日历的周期额度和开放日；时间统一存 UTC，周期边界按服务器时区计算。

### 防止重复执行与崩溃后的误判

1. 路线 CD 在执行前判定，不预先写入占用；整份文件成功后，节点结果、确认事实和账本在同一次 state.json 替换中提交，成功落盘后才继续。
2. 同一事件键重复结算不会重复记账。
3. 崩溃发生在成功提交前，按未成功处理，可重跑整份文件；提交后保留 CD。
4. 程序崩溃后，遗留的活动运行在启动时转为“已中断”，不当作从未执行；关键状态写盘失败时熔断，停止自动运行并让运行进入待处理，不把采集成功当作普通失败自动重试。

游戏操作与本地文件无法同时提交，因此不承诺任意任务严格只执行一次，只通过“成功后立即落盘”和中断态缩小重复窗口。遇到 CD 默认跳过当前节点并继续后续任务，不原地等待。

## 文件级路线 CD

当前唯一的执行前资源限制（US-03），界面称“路线 CD”（2026-10-28 前称“采集 CD”；代码与存储键 `collection_cd`、`collection.cooldown` 未改名）。适用于 Pulonia 地图追踪节点，包括目录展开、计划引用、定时、手动和续跑入口；旧配置组和 JS 内部的路线调用不接入。2026-10-28 按[路线 CD 提案](proposals/cooldown-visibility.md)改造，本节按改造后的代码核对。

### 成功的定义

以一份地图追踪 JSON 为最小单位：全部路径段完整执行成功，才以文件结束时间估算“该文件的采集已完成”。不核验实际拾取，也不保存文件内的部分成功。

`Pathing()` 返回每次调用独立的不可变对象 `PathingResult`：

| 字段 | 内容 |
| --- | --- |
| `IsSuccess` | 整份文件成功执行为 true；任一路径段提前退出或重试耗尽为 false |
| `SuccessFightCount` | 本次执行已确认的成功战斗次数，不是击杀数；每次重新统计 |
| `ErrorMessage` | 空路线、准备失败、提前结束或重试耗尽等已知原因；成功时为空 |
| `ReviveCount` | 预留，当前为 null；只有已统计且无复活时才为 0 |
| `CollectedItems` | 预留的拾取明细，当前为 null；已统计但无拾取时才是空集合 |

未统计的字段显示为“未统计”，不冒充 0。结果对象随节点结果序列化进运行记录；宿主只根据明确成功的完成状态提交 CD。旧历史中的 `success_end`、`fully_executed` 只保留展示，不参与判定。

### 身份与规则

- **文件身份**：仓库文件为 `repository:<仓库 ID>/<小写相对路径>`；本地文件为 `local:<小写规范化绝对路径>`。身份不含计划、节点、版本或内容哈希。
- 同一文件跨计划共享 CD，内容更新后保留 CD；移动、重命名或复制到新位置视为新文件。不同文件的点位重叠不去重。
- **材料规则**：从文件所在目录向上，精确匹配最近的材料名、别名或类别目录名，最近一级命中即停止。规则表集中在 `PuloniaCollectionCooldownRules`（来源为[采集物刷新文档](../../technical/genshin-collectable-respawn.md)，不在运行时解析 Markdown）：

| 类别 | 规则 |
| --- | --- |
| 特产（含石珀、夜泊石、清水玉）及明确的植物、食材、生物 | 滚动 12、24 或 46 小时，按文档分档 |
| 钓鱼（含官方仓库目录 `提瓦特钓鱼指南`） | 滚动 72 小时 |
| 晶核、晶蝶（参考规则）；魔物、怪物、普通怪、普通怪物、精英怪、精英怪物、地方传奇、地脉花、盗宝团 | 下一次服务器 04:00 |
| 类别目录 `敌人与魔物`、`锄地专区` | 下一次服务器 04:00；位于材料目录之上，材料规则优先 |
| 矿石（含官方仓库目录 `魔晶矿`） | 成功当天的服务器日期加 1、2 或 3 天的 00:00，显示“午夜保守估算” |
| 宝箱、岩之印调查 | 一次性，成功后不再自动执行 |
| 圣遗物、木材（含 `木材伐木点`）、摩拉盒等混合类别 | 识别为“混合类别”，不检查 CD |
| 未匹配任何目录 | “未识别”，不检查 CD，界面单独提示 |

不确定的数据在规则说明中标为参考规则。不实现木材循环刷新，也不实现调查、击杀（每日 400 只）、领奖的全局额度。按官方脚本仓库提交 `d775a4b` 的 5315 个路线文件统计，自动识别覆盖 3875 个（73%）；未识别的 1440 个中 1386 个在 `食材与炼金`，需先补刷新数据。统计方法见[提案 1.1 节](proposals/cooldown-visibility.md#11-魔物路线几乎没有-cd)，单元测试用一组真实目录样本防止规则表回退。

### 设置

`pathing` 参数 `collection_cd` 是一个对象，按[有效参数解析规则](model.md#有效参数解析规则)整体参与各层覆盖，不深合并。缺省为 `{ "mode": "auto" }`。

| `mode` | 字段 | 含义 |
| --- | --- | --- |
| `auto` | — | 按目录匹配上表；忽略 `reset_hour`、`reset_weekday`，每日边界为 `PuloniaServerTime.DayResetHour`（4 点） |
| `disabled` | — | 不检查 CD |
| `rolling` | `hours` | 成功时刻加固定小时数 |
| `daily` | `reset_hour`（0—23，默认 4） | 下一个服务器游戏日开始时 |
| `calendar_days` | `days` | 服务器时区当天日期加若干天的 00:00 |
| `weekly` | `reset_hour`、`reset_weekday`（1 为周一，7 为周日，默认周一 4 点） | 下一个游戏日后顺延到指定星期 |
| `once` | — | 有成功记录后不再执行 |

代码中模式为枚举 `PuloniaCollectionCooldownMode`，参数和快照仍写上表的字符串。规则对象 `PuloniaCollectionCooldownPolicy` 的来源为枚举 `PuloniaCollectionCooldownRuleSource`（Directory、Mixed、Unmatched、Manual），快照中写中文取值（“目录自动识别”“混合类别”“未匹配规则”“手动覆盖”），旧快照无需迁移。规则说明文字只由 `Policy.Description` 生成。Schema 只有 `PuloniaCollectionCooldown.CreateSchema` 一份，执行器和设置目录都引用它。

表单只给三种选择：按材料自动识别（推荐）、不检查 CD、自定义周期；选择自定义后再选“每隔 N 小时”“每日”“每周”“日期间隔”“只执行一次”，存储格式不变。共享设置行显示规则摘要（“自动识别”“每日 04:00”“不检查”等）。

日与周边界使用 `PuloniaServerTime.GetGameDate`（按服务器偏移减去日切小时取日期）。服务器偏移在入队时由 `PuloniaServerTime` 从 `ServerTimeHelper` 取得并写入请求的 `ServerUtcOffsetMinutes`，未初始化时按 UTC+8；续跑沿用原请求的偏移。

准备快照固定本节点的规则，排队期间修改设置不影响本次运行。修改规则或关闭限制后，下一次判定从原成功时间按新规则重新计算，不改写历史；不检查及未识别时，完整成功仍记录时间。

### 状态与判定

唯一的判定函数是 `PuloniaCollectionCooldown.GetStatus(policy, latestEntry, now, serverOffset)`，运行判定、运行预览、任务树标记、CD 总览和 CD 表单都调用它：

| 状态 `PuloniaCollectionCooldownState` | 条件 | 本次运行 |
| --- | --- | --- |
| Ready（可执行） | 没有成功记录，或已到下次可执行时间 | 执行 |
| CoolingDown（CD 中） | 有记录，未到按当前规则重算的下次可执行时间 | 跳过，原因码 `pathing.cooldown_active` |
| Completed（已完成） | 一次性规则且有记录 | 跳过，原因码 `pathing.cooldown_completed` |
| Unmatched（未识别） | 自动识别没有命中 | 执行 |
| NotChecked（不检查） | 用户关闭或混合类别 | 执行 |

跳过的节点结果为 Skipped，带上述原因码与 collection_* 数据，消息形如“已跳过：仍在 CD，10-30 04:00 后可执行（落落莓 · 成功后 46 小时）”；同时更新运行的当前节点与消息（只在内存）。运行汇总按原因码把“路线 CD 跳过 N”与其他跳过分开。界面时间一律按本机时间显示；结果数据中的时间仍存 UTC ISO 字符串。成功后的下次可执行时间只计算一次，节点结果与账本共用。

### 作用域与键

| 键 | 格式 |
| --- | --- |
| 作用域 `ScopeKey` | `cn/world/{身份}`：未切换账号（默认账号及所有普通计划）为 `default`，切换到指定账号为核验后的 UID |
| 资源 `EffectKey` | `pathing.file:<文件身份>` |
| 事件 `EventKey` | `pathing.file:<运行 ID>:<节点地址>:<尝试次数>` |
| 周期 `WindowKey` | 固定为 `file_success` |

作用域身份在 2026-10-24 统一：此前一条龙用核验 UID、普通计划用账号资料 ID 或 `default`，同一真实账号在两类计划之间不共享 CD。旧账本中按资料 ID 写下的键因此失效，相关文件可能多跑一次。多账号规则见[多账号](accounts.md)。

### 账本

账本是 state.json 中的 `ledger`，条目 `PuloniaTaskLedgerEntry` 字段为 `event_key`、`scope_key`、`effect_key`、`window_key`（固定 `file_success`）、`units`（固定 1）、`occurred_at`、`next_eligible_at`（按记录时规则）、`task_run_id`（`<运行 ID>:<节点地址>`）和 `evidence`（来源 `pathing.file`，数据含材料、规则、文件身份和当时的规则对象）。`window_key` 与 `units` 是已删除额度框架的残留，不参与判定，下一次 state.json 格式变更时删除（D19）。

- 每个（作用域，文件）只保留**最新一条**：成功时替换旧条目，判定也只看最新一条。
- 每次成功的明细保存在对应运行历史的 `confirmed_effects` 中，账本不承担历史职责。
- 不检查和未识别的文件成功后同样写入账本。账本没有过期清理，只受 10 万条上限约束。
- **清除记录**：`IPuloniaTaskService.ClearCollectionCooldownAsync` 经 `PuloniaTaskRunStateStore.MutateAsync` 删除指定（作用域，文件）的条目，只在主实例执行；运行历史中的确认事件不变；不能撤销。“标记为已采集”不提供（D18）。
- **账本变化通知**：新成功记录落盘或清除记录后触发 `LedgerChanged`，界面据此重新读取账本，不在每次 `RunChanged` 时重建。
- **运行预览**：`IPuloniaTaskService.PreviewAsync` 与提交共用 `PrepareRunAsync` 准备快照（只读打开已保留归档），不写 state.json、不入队；按执行顺序对每个叶子调用 `GetStatus`，并预测“与前面同一文件，前一项成功后跳过”。一条龙绑定指定账号时按账号资料的 UID 预测，运行时以核验 UID 为准。

界面呈现见[界面 · 路线 CD](user-interface.md#路线-cd)。

## 存储

### 文件组织

```text
User/Pulonia/
├─ plans/<planId>.json              计划；保存时留 .bak
├─ profiles/<profileId>.json        设置方案（共享设置）
├─ plan-order.json                  计划列表顺序（编辑器元数据）
├─ plan-view-state.json             节点折叠状态（编辑器元数据）
├─ accounts.json                    账号目录
├─ account-images/<账号ID>-<yyyyMMdd-HHmmss>.png   账号标识图片，每次新建
├─ state.json / state.json.bak      当前运行状态（见下文）
├─ history/<planId>/<runId>.json    已结束运行的不可变记录
├─ boss-rewards/<哈希>.json         首领讨伐累计领奖次数，按（计划、节点、重置批次）分文件
├─ imports/<yyyyMMdd-HHmmss>.txt    旧配置导入报告
├─ .writer.lock                     跨进程写锁
└─ ScriptResources/                 仓库注册、保留归档、预览与 JS 工作目录
```

ID 与文件命名规则见[标识与磁盘路径](identifiers-and-paths.md)；`ScriptResources` 见[脚本仓库与任务资源](repository-resources.md)。`boss-rewards` 的文件名仍是 SHA-256，是“文件名不含哈希”规则的现存例外。

所有写入先写同目录临时文件 `~<8 位>.tmp` 并刷新到底层存储，目标已存在时用 `File.Replace` 替换（同时保留 `.bak`），首次建立时移动；读取以 `FileShare.Read | FileShare.Delete` 打开，替换期间已打开的句柄继续读旧版本。`PuloniaTaskStore` 的写入还持有 `.writer.lock`，防止多个进程交错写入（子实例是否会写 `User/Pulonia` 待定 D15）。

### 配置类数据

计划、预设、账号、计划顺序、折叠状态和仓库注册只需要“原子替换 + 后写者胜”。计划和账号目录另有修订号，保存旧修订时报冲突，两个页面共用同一编辑会话，这项检查成本很低。配置在运行开始前固定进快照，不同配置文件不要求同步提交。

损坏的计划文件不阻断其他计划：`ListPlansWithFailuresAsync` 跳过坏文件并返回失败清单，文件保留在磁盘上。编辑会话加载时弹出提示；调度保留读取失败计划的触发游标；有计划读不出时拒绝移除账号。预设列表遇到损坏文件仍直接报错。

### state.json

`state.json` 是队列、活动运行和 CD 的唯一当前依据，格式版本 4：

| 字段 | 内容 |
| --- | --- |
| `schema_version`、`sequence` | 格式版本；每次保存递增的序列 |
| `pending_requests` | 已固定快照、尚未开始的运行记录 |
| `active_run` | 唯一活动运行，主实例与子实例共用；`request.execution_target` 记录执行实例 |
| `pending_archives` | 已结束但尚未确认写入历史的运行 |
| `ledger` | 路线 CD 账本 |
| `trigger_states` | 定时触发游标，见[触发与热键](scheduling.md#游标) |

**唯一写入入口**是主实例的 `PuloniaTaskRunStateStore.MutateAsync`：在状态门内复制当前状态（只复制集合，写时复制）→ 应用修改 → 序列化一次并按读取规则自检 → 耐久替换 → 提交为新的当前状态。保存失败时内存保持上一版本并熔断，之后拒绝新提交、停止自动运行。只读查询直接读已提交的状态，不加锁，也不为每秒的调度检查克隆整份状态。子实例以非协调器模式构造服务，不读写 state.json。

以下变化各自在一次替换中完成：请求入队与触发游标前移；运行开始；账号核验 UID 与“切换账号”节点结果；节点结果（地图追踪成功时连同确认事实和账本）。当前执行节点的地址只在内存中更新，不单独写盘。

**读取**：只接受格式版本 4，其他版本明确报错并提示删除 `state.json` 与 `state.json.bak`；开发期不升级旧版本文件，也不剔除已停用字段，未知字段一律按严格读取报错。历史文件同样按当前格式严格读取，读不出的文件被跳过并提示。

**备份回退**：正式文件损坏时从 `.bak` 恢复，启动提示“已从备份回退”，下一次保存直接替换正式文件而保留这份有效备份（由加载时记录的标志决定，不再回读旧文件）；两份都无法读取时停止自动运行，保留文件。备份回退不能证明丢失时间段内没有游戏操作。集合上限为 1 万项，账本上限 10 万条。

这是一份状态文件的替换，不是多文件事务；不要把进度写到一处、CD 写到另一处。

### 历史

运行结束时先放进 `pending_archives` 并保存，再按运行 ID 幂等写入 `history/<planId>/<runId>.json`（同一运行已存在且内容不同时拒绝覆盖），成功后从状态移除。中途崩溃时启动后按同一运行 ID 重试归档。历史读取不参与 CD 判定，历史保存失败也不会让任务重新执行。

- 启动时读取全部历史；读不出的历史文件被跳过，文件不删除也不改写，并通过 `RecoveryNotice` 提示（最多列出 3 份）。
- 常驻内存的历史只保留摘要（去掉快照正文），快照在首次查看详情或续跑时从历史文件按需读取。
- 历史文件不自动清理，保留数量策略待定。

收尾保存使用独立且有时间上限的取消控制（15 秒），不复用已经取消的任务令牌。UI 进度可以合并刷新，运行开始与结束、节点结果和采集成功必须等待保存完成。

### 依赖选择

| 选择 | 结论 |
| --- | --- |
| SQLite / ORM | 不引入，使用上述 JSON 存储 |
| Quartz / Hangfire / Elsa / Cronos | 不引入；自写五段 Cron 子集与 .NET 定时循环 |
| `PeriodicTimer`、取消令牌、`SemaphoreSlim`、`File.Replace` | 用于调度、单写者与原子替换 |
| Newtonsoft.Json | 复用项目已有依赖 |
| WPF-UI、MVVM Toolkit、Behaviors、gong-wpf-dragdrop | 沿用现有 UI 技术 |

[File.Replace 文档](https://learn.microsoft.com/en-us/dotnet/api/system.io.file.replace?view=netframework-4.8)、[.NET 文件 I/O 说明](https://devblogs.microsoft.com/dotnet/file-io-improvements-in-dotnet-6/)
