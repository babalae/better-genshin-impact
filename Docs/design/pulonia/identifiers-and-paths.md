# Pulonia：标识与磁盘路径整治

状态：已实施，待用户验收。2026-10-24 基于 `refactor/20260930`（HEAD `092d78c2a`）静态阅读代码整理方案，同日按第 9 节全部采用建议项实施，实施记录见第 11 节。Pulonia 仍在开发中，按用户要求不考虑旧数据兼容。第 2 节保留整治前的现状，供对照。

[返回总览](../automation-system.md) · [远程仓库资源读取](repository-resources.md) · [结果、CD 与文件存储](results-and-storage.md) · [文件级采集 CD](collection-cooldown.md)

文中长度均不含安装目录 B（`AppContext.BaseDirectory`，含末尾 `\`），行号为本次阅读时的位置。

## 1. 结论

- 超长路径集中在 `User\Pulonia\ScriptResources`。`Cache`、`Previews` 的目录名依次是 32 位仓库 GUID、40/64 位来源修订、64 位路径哈希，临时目录再追加 `.<32 位 GUID>.tmp`。
- 发布缓存清单时的临时文件长度为 B + 261（Git 来源）或 B + 285（文件式来源），与安装位置和资源名都无关，每次提取资源都会产生。文件式来源的正式缓存文件为 B + 206 + 文件名，安装目录 40 字符左右就贴近 260。
- Pulonia 自身存储（计划、历史、账号）没有超长，但仍有 GUID 文件名、哈希文件名和多套 GUID 身份。
- 整治方向：
  1. 取消 `Cache`。路线和键鼠直接从已保留归档读取正文；只有 JS 需要真实目录，直接从归档准备工作目录。
  2. 目录名和文件名只使用短稳定 ID 与可读名称。哈希只做内容校验，留在 JSON 字段里。
  3. 持久化身份统一为现有 `PuloniaId` 形态 `前缀_16 位 Base32`；临时文件统一为与目标名无关的 `~<8 位>.tmp`。
- 整治后，程序生成的路径段最长 84 字符；JS 工作目录为 B + 61 + 仓库内相对路径。

关于报错来源：.NET 托管文件 API 在进程内通常会自动处理长路径（依据 .NET 设计，未在本仓库实测）。超过 260 的路径主要在资源管理器删除或复制、压缩备份、外部工具和部分原生组件中失败。因此本方案不依赖进程内开关，从源头缩短路径。

## 2. 现状清单

### 2.1 进入路径的 GUID 和哈希

| # | 路径 | 组成 | 最长 | 代码位置 |
| --- | --- | --- | --- | --- |
| P1 | `ScriptResources\Cache\<仓库>\<修订>\<键>\content\…` | 仓库 GUID + 修订 + SHA-256(相对路径) | 正式 182～206 + 文件名；临时清单 261～285 | `ScriptRepositoryStore.Resources.cs` `MaterializeAsync` 41-42、55，清单写入 95 |
| P2 | `ScriptResources\Previews\<仓库>\<修订>\<键>\` | 同上 | 正式 190～214；临时 227～251 | `ScriptRepositoryStore.Archives.cs` `PreparePreviewAsync` 107-115 |
| P3 | `ScriptResources\Workspaces\<仓库>\<键>\` | 仓库 GUID + SHA-256 | 138 + 项目内路径；`.tmp`/`.old` 175 + 项目内路径 | `Resources.cs` `AcquireWorkspaceAsync` 164-168，`PrepareWorkspace` 178-179 |
| P4 | `ScriptResources\Snapshots\<仓库>\<修订>\scopes.json` | 仓库 GUID + 修订 | 124～148；临时 161～185 | `Archives.cs` 19、59-62 |
| P5 | `ScriptResources\Snapshots\<仓库>\Archives\<内容身份>.zip` | 仓库 GUID + SHA-256 | 149；临时 186 | `Archives.cs` `WriteResourceArchive` 69-74 |
| P6 | `ScriptResources\Locks\<SHA-256(锁键)>.lock` | SHA-256 | 104 | `ScriptRepositoryStore.cs` `AcquireLockAsync` 91，`IsOwnedLockFile` 77-81 |
| P7 | `ScriptResources\SourceVersions\<SHA-256(来源路径)>.json` | SHA-256，内容是 GUID 代次 | 113；临时 150 | `ScriptRepositoryStore.cs` 325-331、390 |
| P8 | `Repos\Pulonia\<仓库>\`，更新时同级 `.update.<GUID>`、`.backup.<GUID>` | 仓库 GUID | 46；更新临时 86 | `ScriptRepositoryStore.Management.cs` 24-29；`ScriptRepoUpdater.Repositories.cs` 108-109 |
| P9 | `User\Pulonia\history\<计划>\<RunId:N>.json` | GUID | 79；临时 117 | `PuloniaTaskStore.cs` 362、405 |
| P10 | `account-images\<GUID>.png`、`account-images\versions\<SHA-256>.png` | GUID / SHA-256 | 105 | `PuloniaGenshinAccountSwitcher.cs` 426；`PuloniaGenshinAccountsViewModel.cs` 146；`PuloniaTaskStore.Accounts.cs` `FreezeAccount` 96-97 |
| P11 | `imports\<时间>-<GUID>.txt` | GUID | 73 | `PuloniaOneDragonViewModel.cs` 570 |
| P12 | 临时文件 `<目标>.<GUID>.tmp`、`.<目标>.<GUID>.tmp` | 目标名 + GUID | 每处 +37～38 | `ScriptRepositoryStore.WriteJson` 104 等 5 处；`PuloniaTaskStore.WriteAtomicallyAsync` 552 |

“修订”在 Git 来源为 40 位提交 SHA，在文件式来源为 64 位 SHA-256，表中范围的两端分别对应这两种来源。

### 2.2 不进路径但需要整治的身份

| # | 对象 | 现状 | 问题 | 代码位置 |
| --- | --- | --- | --- | --- |
| I1 | 仓库注册 ID | `Guid` N 格式 32 位；官方为 `000…001` | 进入 P1-P5、P8；多处校验写死 32 位十六进制 | `ScriptRepositoryStore.cs` 151；`Management.cs` 18、26、146；`ScriptResourceReference.Validate` 42；`ScriptRepoUpdater.Repositories.cs` 61-71 |
| I2 | 远端 ID | `Guid` N 格式 | 与其他 ID 形态不一致 | `Management.cs` 147 |
| I3 | 账号 ID | `account_` + GUID，40 位 | 进入计划绑定、快照和作用域键 | `PuloniaGenshinAccountProfile.cs` 12 |
| I4 | RequestId、RunId | 两个 `Guid`，入队和续跑时同时生成，一一对应 | 运行详情显示两段 36 位 GUID；RunId 进入历史文件名和账本键 | `PuloniaTaskService.cs` 225、382；`PuloniaTaskRunItemViewModel.cs` 99-100 |
| I5 | 目录展开节点 ID | `resource-` + SHA-256，73 位 | 撑长 TaskAddress，迫使事件键对地址再做一次哈希 | `PuloniaTaskBuilder.cs` 181、460 |
| I6 | 采集 CD 的 EffectKey | `pathing.file:` + SHA-256(文件身份) | 历史中的“已确认副作用”只显示哈希，看不出是哪个文件 | `PuloniaCollectionCooldown.cs` 110-111；`PuloniaTaskNodeResultViewModel.cs` 130 |
| I7 | 采集 CD 的 EventKey | `pathing.file:<RunId:N>:<SHA-256(TaskAddress)>:<尝试>` | 为绕开 256 字符上限对地址做哈希 | `PuloniaTaskService.CollectionCooldown.cs` 38；默认键上限见 `PuloniaTaskService.cs` 1102-1107 |
| I8 | 登录凭据目标名 | `…Pulonia.Account.` + 每次保存新生成的 GUID | 旧凭据条目不删除，持续累积 | `PuloniaGenshinAccountsViewModel.cs` 190 |
| I9 | 账号图片版本 | `ImageVersion` 为 SHA-256，运行前复核 | 图片文件本来每次新建、不覆盖，冻结和复核是重复保证 | `PuloniaTaskStore.Accounts.cs` 90-114；`PuloniaGenshinAccountSwitcher.cs` 230 |
| I10 | 准备快照的 `path` | 仓库资源保存约 200 字符、带哈希段的绝对缓存路径 | 本机缓存路径写进 `state.json` 和历史；路线和 JS 执行时重新提取，键鼠直接读取它 | `PuloniaTaskBuilder.cs` 170、261 |

### 2.3 附带问题：仓库路线识别不到材料

仓库路线的 `PathingTask.FullPath` 指向缓存文件。`GetMaterialName()` 相对 `User\AutoPathing` 逐级比对目录名（`PathingTask.cs` 51 起），而缓存目录名是 GUID、修订和哈希，所以永远识别不到材料。节点未启用队伍配置时，`PathExecutor` 按材料从“地图追踪条件配置”选队伍（`PathExecutor.cs` 397-400、491-500），这条路径对仓库路线始终落空。结论来自静态阅读，未运行验证。

### 2.4 保留的哈希与 GUID

| 对象 | 保留理由 |
| --- | --- |
| 资源指纹 `resource_version`、`source.version`（SHA-256） | 内容校验，只在 JSON 中 |
| `approved_revision`（Git 40 位 / 文件式 64 位） | 来源版本的自然标识；整治后只在 JSON 中，不再进路径 |
| 归档内容身份（Git 对象 ID 或范围清单 SHA-256） | 跨版本复用归档时去重；整治后只在索引 JSON 中 |
| `ExecutionId`（`Guid`） | 只在内存和跨进程消息中关联，不落盘、不展示 |
| 旧配置导入报告中的 SHA-256 | 只展示来源文件指纹 |
| `.git` 内部对象命名 | Git 自身格式 |

Pulonia 以外的 GUID（`ScriptControlViewModel` 动态控件名、`OneDragonFlowViewModel`、`HtmlMaskWindow`）不在本次范围。

## 3. 整治规则

1. 用途分开。身份用短稳定 ID；内容校验用哈希，保存在 JSON 字段；互斥用可读名称的锁文件；暂存用与目标名无关的临时名。
2. 目录名和文件名不得包含哈希、GUID 或来源修订。Git 仓库内部目录除外。
3. 持久化身份统一为 `前缀_16 位小写 Crockford Base32`，即现有 `PuloniaId` 形态，共 20 字符。需要从内容确定性派生时（只有目录展开节点），取 SHA-256 前 10 字节按同一字母表编码，形态不变。
4. 临时文件和临时目录放在目标的同级，命名为 `~<8 位 Base32>.tmp`；替换目录时旧目录改名为 `~<8 位>.old`。不再把目标名拼进临时名。`File.Replace` 的 `.bak` 保持不变。
5. 能从归档读取就不落地。只有 JS 需要真实目录。
6. 路径预算：程序生成的段（不含 B、不含仓库内相对路径）不超过 90 字符。B 按 100 字符规划时，JS 项目在工作目录中还有约 98 字符可用。
7. `PuloniaTaskValidator.ValidateId` 的长度上限从 96 收紧到 32，防止 ID 成为路径中的长段。

## 4. 统一标识

把 `PuloniaId` 中的 Base32 生成逻辑下沉到 `Helpers/ShortId.cs`，提供 `Create(prefix, length)` 和 `FromHash(prefix, hash)`。`PuloniaId` 调用它；`ScriptRepositoryStore` 也直接调用它，避免 `Core` 反向依赖 `Pulonia`。

| 对象 | 新格式 | 示例 | 说明 |
| --- | --- | --- | --- |
| 计划、节点、预设、触发器 | 不变：`pln_`、`tsk_`、`pre_`、`trg_` + 16 位 | `pln_7k2m9q4x8c1v5b3n` | |
| 账号 | `acc_` + 16 位；默认账号仍为 `default` | `acc_4x9c1v5b3n7k2m8q` | |
| 仓库注册 | 官方固定 `official`；其他 `rep_` + 16 位 | `rep_3n8q2k7m4x9c1v5b` | 生成时在注册锁内查重 |
| 远端 | 第三方 `rem_` + 16 位；官方渠道沿用渠道名 | | 只在 `repositories.json` 中 |
| 运行 | `run_<本地时间 yyyyMMdd-HHmmss>-<4 位 Base32>`，24 字符 | `run_20261024-153012-k3m9` | 历史目录按文件名即可辨认先后；列表排序仍以 `SubmittedAt` 为准。单写入者生成时与现有运行比对，重复则重新生成 |
| 请求 | 并入运行 ID（决策 D1） | | 若不合并，则为 `req_` + 16 位 |
| 归档块 | `blk_` + 16 位 | `blk_9q4x8c1v5b3n7k2m` | 见第 5 节 |
| 预览会话 | `pv_` + 8 位 | `pv_k3m9x2ab` | 一次性目录 |
| 目录展开节点 | `res_` + `FromHash(SHA-256(小写相对路径))` | `res_5b3n7k2m9q4x8c1v` | 同一目录内重复时明确报错 |

类型调整：RunId（以及保留时的 RequestId）由 `Guid` 改为 `string`，涉及 `IPuloniaTaskService` 的入队、查询、等待、取消和续跑签名，`PuloniaTaskRunRecord`、`PuloniaTaskRunView`、`PuloniaTaskTriggerState.LastRequestId`、`PuloniaTaskOperationIntent.RunId`、`PuloniaTaskExecutionContext`、`PuloniaTaskRunChangedEventArgs` 和远程消息。`PuloniaTaskJson` 中的 `Guid.Empty` 检查改为 `ValidateId`。

## 5. 资源存储：取消 Cache

### 5.1 目标布局

```text
User\Pulonia\ScriptResources\                           29
├─ repositories.json            注册表；文件式来源增加 source_generation
├─ selection.json
├─ Locks\
│  ├─ sources.lock、registry.lock、selection.lock
│  └─ retain.<rep_…>.lock                                67
├─ Snapshots\<rep_…>\
│  ├─ retained.json             修订 → 范围 → 归档块；归档块 → 内容身份      73
│  └─ <blk_…>.zip                                         84
├─ Workspaces\<rep_…>\<JS 项目相对路径>\                  61 + 相对路径
│     同级 <项目目录名>.lock    工作目录锁
└─ Previews\<pv_…>\             一次性预览，用完即删                        ≤ 63
```

删除：`Cache\`、`SourceVersions\`、`Snapshots\<仓库>\<修订>\`、`Snapshots\<仓库>\Archives\`，以及旧 `<修订>.zip`、`objects.git` 的兼容读取分支（`ScriptRepositorySnapshot.cs` 79-93、`Archives.cs` 31-35）。

| 路径 | 现在 | 整治后 |
| --- | --- | --- |
| 路线、键鼠正文 | 182～206 + 文件名；临时清单 261～285 | 不落地 |
| JS 工作目录 | 138 + 项目内路径；临时 175 + 项目内路径 | 61 + 项目相对路径 + 项目内路径；临时目录比正式目录短 |
| 预览 | 190～214；临时 227～251 | ≤ 63 |
| 归档 | 149；临时 186 | 84；临时 73 |
| 锁 | 104 | ≤ 67 |

`retained.json` 示例：

```json
{
  "blocks": {
    "blk_9q4x8c1v5b3n7k2m": "<内容身份>"
  },
  "revisions": {
    "<approved_revision>": {
      "repo/pathing/地方特产/蒙德": "blk_9q4x8c1v5b3n7k2m",
      "packages": "blk_…"
    }
  }
}
```

保留流程：持有 `retain.<仓库>` 锁，读取索引，计算各范围的内容身份；已有同身份的块直接复用，否则写入同级 `~<8 位>.tmp`，改名为 `<blk_…>.zip` 后登记，最后原子替换 `retained.json`。块已写入、索引尚未更新时崩溃会留下孤儿块，首版不清理，与现有“不自动清理保留版本”一致。

锁顺序固定为：工作目录锁 → `retain.<仓库>` → `sources`。

### 5.2 API 调整

`ScriptRepositoryStore`：

- 新增 `RetainAsync(reference, taskType, ct)`：保留归档并校验内容。路线和键鼠校验 JSON 语法；JS 校验清单、入口和声明的设置文件，逻辑从 `MaterializeAsync` 移入。
- 保留 `OpenApprovedAsync`：读取正文、枚举目录、计算指纹都在固定快照上进行。
- `AcquireWorkspaceAsync`：直接从已保留归档写入同级临时目录，按现有规则合并新旧 `saved_files`，再替换工作目录。
- `PreparePreviewAsync`：返回可释放的预览租约，目录为 `Previews\<pv_…>`，释放时删除。创建弹窗在切换选择或关闭时释放。
- 删除 `MaterializeAsync`、`ValidateCache` 和缓存 `manifest.json`。

调用方：

| 调用方 | 现在 | 改为 |
| --- | --- | --- |
| `PuloniaTaskBuilder` 单资源（261） | 提取到缓存，再对缓存文件算指纹 | `RetainAsync`；用 `ScriptRepositoryStore.ComputeVersion` 在固定快照上算指纹；不写 `path` |
| `PuloniaTaskBuilder` 目录引用（170） | 提取整个目录后枚举磁盘，每个子文件再各提取一份 | 在固定快照上枚举 `repo/<目录>` 下的 JSON；子节点共用目录归档 |
| `PuloniaTaskService.ValidateSnapshotResourcesAsync`（1205） | 提取后复核指纹 | 在固定快照上复核 |
| `PuloniaTaskResourceVersionService`（171） | 提取 | `RetainAsync` |
| `PuloniaTaskCreationDialogViewModel`（900） | 提取后用缓存路径算指纹 | `RetainAsync`，指纹在快照上计算 |
| `PuloniaPathingTaskExecutor`（72-74） | 提取后 `BuildFromFilePath` | 从快照读取正文，调用 5.3 的新工厂 |
| `PuloniaKeyMouseTaskExecutor`（49） | 读取快照里的缓存路径 | 仓库资源从快照读取正文；本地资源不变 |
| `PuloniaTaskResourcePreviewLoader`（26-27） | 预览目录长期保留 | 持有预览租约 |

Builder 在一次构建内按（仓库，修订）复用已打开的快照，避免每个节点重新打开全部归档。三个执行器的“没有固定资源路径”判断改为 `Resource` 与 `Path` 至少有一个。

### 5.3 路线工厂

把 `PathingTask.BuildFromFilePath` 中的 BetterGI 版本检查和 `EnableMonsterLootSplit` 传播抽成共用方法，新增 `BuildFromJson(json, logicalPath)`。仓库路线的逻辑路径为 `MapPathingViewModel.PathJsonPath` 加上去掉 `pathing/` 前缀的相对路径，只用于 `FileName`、日志和 `GetMaterialName()`，不读取该位置。这样同时修复 2.3 的材料识别问题。

### 5.4 JS 工作目录

- 工作目录镜像仓库相对路径，例如 `Workspaces\official\js\自动采集\`。用户可以直接找到脚本的 `saved_files` 数据。
- 工作目录锁放在同级 `js\自动采集.lock`，替换目录时不受影响；启动时不清理，下次复用。
- 暂不处理：仓库中嵌套的 JS 项目，或只差大小写的两个项目路径，会落到同一目录。首版不支持，在代码注释中记录。

## 6. 其他整治项

| 项 | 改为 | 涉及 |
| --- | --- | --- |
| 锁文件（P6） | 可读名称 `sources`、`registry`、`selection`、`retain.<仓库>`；工作目录锁见 5.4。`cache/…`、`preview/…` 锁随功能删除。启动清理处理 `Locks` 下全部 `*.lock` | `ScriptRepositoryStore.cs` 42-99 |
| 文件式来源代次（P7） | 存入 `repositories.json` 对应注册的 `source_generation`；`NotifyUpdated` 按目录找到注册后更新。删除 `SourceVersions`，同时消除按路径哈希区分大小写、代次带 JSON 引号两处隐患 | `ScriptRepositoryStore.cs` 325-331、386-390 |
| 第三方 clone（P8） | `Repos\Pulonia\<rep_…>\`；更新临时目录为同级 `~<8 位>.update`、`~<8 位>.backup` | `Management.cs` 24-29；`ScriptRepoUpdater.Repositories.cs` 61-71、108-109 |
| 运行历史（P9） | `history\<计划>\<运行 ID>.json` | `PuloniaTaskStore.cs` 362、405 |
| 账号图片（P10、I9） | 每次采集或导入新建 `account-images\<账号 ID>-<yyyyMMdd-HHmmss>.png`，同秒重名追加序号，不覆盖。删除 `versions\`、`FreezeAccount`、`ImageVersion` 和运行前复核，运行快照只记 `ImagePath` | `PuloniaGenshinAccountSwitcher.cs` 230、426；`PuloniaGenshinAccountsViewModel.cs` 146；`PuloniaTaskStore.Accounts.cs` 90-114 |
| 导入报告（P11） | `imports\<yyyyMMdd-HHmmss>.txt`，同秒重名追加 `-2`、`-3` | `PuloniaOneDragonViewModel.cs` 570 |
| 临时文件（P12） | 按规则 4 | `ScriptRepositoryStore` 5 处；`PuloniaTaskStore.cs` 552 |
| 账号 ID（I3） | `acc_` + 16 位 | `PuloniaGenshinAccountProfile.cs` 12 |
| 凭据目标名（I8） | `BetterGenshinImpact.Pulonia.Account.<账号 ID>`，保存时覆盖，删除账号时一并删除 | `PuloniaGenshinAccountsViewModel.cs` 190 |
| 目录展开节点（I5） | `res_` + 16 位 | `PuloniaTaskBuilder.cs` 181、460 |
| EffectKey（I6） | `pathing.file:` + 文件身份原文，例如 `pathing.file:repository:official/pathing/地方特产/蒙德/风车菊/风车菊-01.json` | `PuloniaCollectionCooldown.cs` 110-111 |
| EventKey（I7） | `pathing.file:<运行 ID>:<TaskAddress>:<尝试>`，不再哈希；默认键为 `<运行 ID>:<TaskAddress>:<规则>`。上限从 256 放宽到 1024，只防异常数据 | `PuloniaTaskService.CollectionCooldown.cs` 38；`PuloniaTaskService.cs` 1102-1107 |
| 准备快照 `path`（I10） | 仓库资源不写 `path`，执行器只认 `resource`；本地资源保持原路径 | `PuloniaTaskBuilder.cs`；三个执行器 |

## 7. 不做的事

- 不加 `longPathAware` 清单，也不手动加 `\\?\` 前缀。前者还要求系统开启长路径策略，两者都不影响资源管理器等外部工具。
- 不在运行时截断路径，也不按长度拒绝执行。
- 不清理崩溃残留的临时目录、孤儿归档块和旧账号图片，与现状一致，在代码注释中记录。
- 不处理架构评审中与本议题无关的简化，例如合并 `Remotes` 集合、`ResourceVersion` 与 `approved_revision` 的重复。
- 不迁移、不识别旧格式数据。

## 8. 实施步骤

每步完成后执行 AGENTS.md 中的编译命令。

1. 资源存储：`ShortId`、仓库 ID、第 5 节全部内容，以及锁文件、来源代次、clone 目录和临时命名。这一步消除全部超长路径。
2. 运行与账号标识：运行 ID（与 D1 一起）、账号 ID、目录展开节点、ID 长度上限。
3. 其余：账号图片与凭据、导入报告、EffectKey 与 EventKey、快照 `path`。
4. 同步测试与文档（第 10 节）。

旧数据处理：实施前由开发者手动删除 `User\Pulonia\` 和 `Repos\Pulonia\`。旧计划引用的仓库 ID 和账号 ID 无法通过新校验，需要重建。程序不识别旧格式，也不自动删除。

## 9. 待确认决策

| # | 问题 | 建议 | 影响 |
| --- | --- | --- | --- |
| D1 | RequestId 是否并入 RunId | 合并。两者在入队和续跑时同时生成、一一对应（`PuloniaTaskService.cs` 225、382） | `IPuloniaTaskService` 签名、`state.json`、触发器 `LastRequestId`、远程消息、运行详情 |
| D2 | 运行 ID 形态 | 本地时间 + 4 位随机，便于在历史目录中辨认 | 若选纯随机 `run_` + 16 位，长度不变 |
| D3 | JS 工作目录命名 | 镜像仓库相对路径 | 备选 `ws_` 短 ID 加映射文件，多一份需要维护的状态 |
| D4 | 凭据目标名按账号固定 | 固定 | 排队中的运行会使用最新凭据，而不是保存快照时的旧凭据 |
| D5 | 预览目录 | 一次性目录加租约 | 备选：README 以文本传给控件，彻底去掉 `Previews`；需确认现有 Markdown 控件是否支持 |

## 10. 需要同步的测试与文档

测试（`Test\BetterGenshinImpact.UnitTest\Pulonia\`）：

- `PuloniaRepositoryResourceTests.cs`：锁名、`Cache`、`Previews`、`Archives` 相关断言全部改写。
- `PuloniaIdTests.cs`：新增前缀和长度上限。
- `PuloniaTaskHistoryTests.cs`：历史文件名。
- `PuloniaGenshinAccountTests.cs`：账号 ID、账号图片、删除 `FreezeAccount`。
- `PuloniaCollectionCooldownTests.cs`：用 32 位、40 位假 ID 构造的仓库引用，以及 EffectKey。
- `PuloniaTaskResourceUpdateTests.cs`、`PuloniaRemoteExecutionTests.cs`、`PuloniaTaskTriggerTests.cs`：运行和请求 ID 的类型。
- 测试临时根目录 `%TEMP%\bgi-pulonia-*-<Guid:N>` 改为 `%TEMP%\bgi-<8 位>`，避免测试自身制造长路径。

文档：

- `repository-resources.md`：资源引用 JSON 的 `repository_id` 说明、目录树、`Locks` 段、“缓存通过临时目录……”段。
- `results-and-storage.md` 的文件组织：此外现状已与代码不符，文档写的是 `accounts/<accountId>.json`，代码是 `accounts.json` 加 `account-images/`。
- `task-model.md` 的“步骤 1 已验收的具体格式”：“ID 实际生成为 GUID 的小写 N 格式”已过时；目录展开节点 ID 的说明。
- `collection-cooldown.md`：事件键中的“节点地址摘要”。
- `execution.md`：C# 接口中的 `Guid` 参数。

## 11. 实施记录（2026-10-24）

第 9 节 D1～D5 均采用建议项：RequestId 并入 RunId；运行 ID 为本地时间加 4 位随机；JS 工作目录镜像仓库相对路径；凭据目标名按账号固定；预览使用一次性目录加租约。

与方案的差异：

- 内容校验只在首次保留资源时进行。资源已被同修订的其他范围覆盖时（例如 JS 共享的 `repo/pathing` 已包含某条路线），不再单独校验，路线正文由执行器解析时校验。
- 锁顺序为工作目录锁 → retain → sources → registry。文件式来源的导入代次每次读取清单时都在 registry 锁内重新读取，不使用取得 sources 锁之前的注册副本。
- `ValidateId` 上限收紧到 32 后，32 位十六进制 GUID 仍能通过通用格式校验；仓库 ID 另有严格校验（`official` 或 `rep_` 加 16 位），不接受旧 GUID。
- 新增 `ScriptRepositoryStore.ListDirectoryResources`，目录指纹、运行准备和创建任务共用同一份 JSON 清单与排序。

验证：全解决方案 Debug 编译通过。Pulonia 单元测试 299 项中 287 项通过、12 项失败；在未修改的 HEAD（`e88b0c226`）上运行同一批测试，失败的是同样的 12 项，属于既有失败。未启动应用或游戏，实际界面、真实仓库和游戏执行待用户验收。
