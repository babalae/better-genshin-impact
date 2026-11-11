# Pulonia：标识与磁盘路径

[返回总览](README.md) · [脚本仓库与任务资源](repository-resources.md) · [结果、路线 CD 与存储](results-and-storage.md)

状态：2026-10-24 整治已实施，待用户验收（路线图“不属于编号步骤的交付”）。本文只保留现行规则；整治前的路径清单、API 调整、实施步骤、决策过程和实施记录见[归档](archive/identifiers-and-paths-remediation.md)。文中长度均不含安装目录 B（`AppContext.BaseDirectory`）。

实施状态：账户、实例和账户进度相关的标识与路径按[实例、账户与路线 CD 统筹设计](../instance-account-model.md)改写，代码已于 2026-11-01 实施，待用户验收，见[统筹设计第 15 节](../instance-account-model.md#15-实施记录)。

## 规则

1. **用途分开。** 身份用短稳定 ID；内容校验用哈希，只保存在 JSON 字段里；互斥用可读名称的锁文件；暂存用与目标名无关的临时名。
2. **目录名和文件名不含哈希、GUID 或来源修订**（Git 仓库内部目录除外）。首领领奖次数改存 `accounts/<账户 ID>/boss-rewards.json` 后没有例外。
3. **持久化身份统一为 `前缀_16 位小写 Crockford Base32`**（共 20 字符），由 `Helpers/ShortId.cs` 生成，`PuloniaId` 与 `ScriptRepositoryStore` 都调用它。需要从内容确定性派生时（目录展开节点），取 SHA-256 前 10 字节按同一字母表编码。
4. **临时文件和目录**放在目标同级，命名 `~<8 位 Base32>.tmp`；替换目录时旧目录改名为 `~<8 位>.old`。`File.Replace` 产生的 `.bak` 保持原名。
5. **能从归档读取就不落地**，只有 JS 需要真实工作目录。
6. **路径预算**：程序生成的路径段（不含 B 和仓库内相对路径）不超过 90 字符；B 按 100 字符规划时，JS 项目在工作目录中还有约 98 字符可用。
7. **ID 校验**：`^[a-z0-9][a-z0-9_-]{0,31}$`，拒绝 Windows 保留名；仓库 ID 另有严格校验（`official` 或 `rep_` 加 16 位）。

.NET 托管文件 API 在进程内通常能处理长路径（依据 .NET 设计，未在本仓库实测），超过 260 的路径主要在资源管理器、压缩备份、外部工具和部分原生组件中失败，因此从源头缩短路径，不依赖进程内开关。

## 标识

| 对象 | 格式 | 示例 |
| --- | --- | --- |
| 计划、节点、设置方案、触发器 | `pln_`、`tsk_`、`prf_`、`trg_` + 16 位 | `pln_7k2m9q4x8c1v5b3n` |
| 账户 | `acc_` + 16 位，包括初始自带的“我的账户”。没有默认账号 `default` | `acc_4x9c1v5b3n7k2m8q` |
| 实例键 | 主实例 `primary`，桌面分身 `child_session`，云原神实例 `webview:<小写实例名>`；重启后不变，写入运行记录、完成记录、触发器的执行实例和 `run.lock` | `webview:小号a` |
| 仓库注册 | 官方 `official`；其他 `rep_` + 16 位，生成时在注册锁内查重 | `rep_3n8q2k7m4x9c1v5b` |
| 远端 | 第三方 `rem_` + 16 位；官方沿用渠道名 | 只在 `repositories.json` 中 |
| 运行（兼作请求 ID） | `run_<本地时间 yyyyMMdd-HHmmss>-<4 位>`，24 字符；与现有运行和已加载历史比对，重复则重新生成 | `run_20261024-153012-k3m9` |
| 归档块 | `blk_` + 16 位 | `blk_9q4x8c1v5b3n7k2m` |
| 预览会话 | `pv_` + 8 位 | `pv_k3m9x2ab` |
| 目录展开节点 | `res_` + 由小写相对路径派生的 16 位；同一目录内重复时报错 | `res_5b3n7k2m9q4x8c1v` |

保留的哈希与 GUID：资源指纹（`resource_version`、`source.version`）、`approved_revision`、归档内容身份、旧配置导入报告中的来源指纹都只在 JSON 中；跨进程消息的 `ExecutionId`（GUID）只在内存和 IPC 中，不落盘、不展示。

## 文件命名

| 对象 | 规则 |
| --- | --- |
| 运行历史 | `User/Pulonia/history/<计划 ID>/<运行 ID>.json`，所有实例共用，每个实例只写自己的运行 |
| 账户图片 | `User/Pulonia/account-images/<账户 ID>-<yyyyMMdd-HHmmss>.png`，同秒重名追加 `-2`、`-3`，从不覆盖 |
| 导入报告 | `User/Pulonia/imports/<yyyyMMdd-HHmmss>.txt`，同秒重名追加 `-2`、`-3` |
| 登录凭据 | Windows 凭据目标名 `BetterGenshinImpact.Pulonia.Account.<账户 ID>`，保存时覆盖，删除账户时删除 |
| 账户进度 | `User/Pulonia/accounts/<账户 ID>/route-completions.json`（完成记录）、`boss-rewards.json`（首领领奖次数） |
| 账户占用 | `User/Pulonia/accounts/<账户 ID>/run.lock`，运行占用账户期间打开 |
| 实例目录 | `User/Pulonia/instances/<实例目录名>/`；目录名为 `primary`、`child_session` 或 `webview-<小写实例名>`，即实例键中的 `:` 改为 `-` |
| 实例文件 | 实例目录下的 `settings.json`（实例当前账户）、`state.json` 与 `state.json.bak`（实例运行状态）、`online.lock`（实例运行期间打开） |
| 第三方 clone | `Repos/Pulonia/<rep_…>/`；更新临时目录为同级 `~<8 位>.update`、`~<8 位>.backup` |
| 完成记录的 `route` | 路线身份原文，不做哈希，例如 `repository:official/pathing/地方特产/蒙德/风车菊/风车菊-01.json` |
| 准备快照的 `path` | 仓库资源不写 `path`，执行器只认 `resource`；本地资源保持原路径 |

删除根目录的 `User/Pulonia/state.json`、`state.json.bak` 和 `boss-rewards/`，旧文件不转换，见[统筹设计第 11 节](../instance-account-model.md#11-迁移)。路线 CD 的资源键 `pathing.file:<文件身份>` 和事件键 `pathing.file:<运行 ID>:<节点地址>:<尝试次数>` 随作用域键一起删除，完成记录按账户存放、按 `run_id`、`task_address`、`attempt` 幂等。

## 账户与实例目录布局

```text
User\Pulonia\accounts\<acc_…>\                       43
├─ route-completions.json       完成记录             65
├─ boss-rewards.json            首领领奖次数         60
└─ run.lock                     账户占用             51
User\Pulonia\instances\<实例目录名>\                 primary 31；child_session 37；云原神 32 + 实例名长度
├─ settings.json                实例当前账户
├─ state.json / state.json.bak  实例运行状态（格式版本 7）
└─ online.lock                  在线标记
```

右侧数字为该级路径的长度（不含 B）。实例目录下最长的文件名是 `state.json.bak`，比目录多 14 字符。云原神实例名由用户填写，这一级的长度随实例名变化。完整的 `User/Pulonia` 布局见[结果、路线 CD 与存储 · 文件组织](results-and-storage.md#文件组织)。

## 资源存储布局

```text
User\Pulonia\ScriptResources\                           29
├─ repositories.json
├─ selection.json
├─ Locks\
│  ├─ sources.<目录键>.lock、registry.lock
│  └─ retain.<rep_…>.lock                                67
├─ Snapshots\<rep_…>\
│  ├─ retained.json             修订 → 范围 → 归档块；归档块 → 内容身份      73
│  └─ <blk_…>.zip                                         84
├─ Workspaces\<rep_…>\<JS 项目相对路径>\                  61 + 相对路径
│     同级 <项目目录名>.lock    工作目录锁
└─ Previews\<pv_…>\             一次性预览，用完即删                        ≤ 63
```

右侧数字为该级路径的长度（不含 B）。没有 `Cache\` 和 `SourceVersions\`：路线和键鼠直接从归档读取正文，文件式来源的导入标记改为仓库根 `repo_updated.json` 的写入时间。

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

保留流程：持有 `retain.<仓库>` 锁，读取索引，计算各范围的内容身份；已有同身份的块直接复用，否则写同级临时文件、改名为 `<blk_…>.zip` 后登记，最后原子替换 `retained.json`。锁顺序见[脚本仓库与任务资源](repository-resources.md#锁与并发)。

### 仓库路线的逻辑路径

`PathingTask.BuildFromJson(json, logicalPath)` 与 `BuildFromFilePath` 共用版本检查和 `EnableMonsterLootSplit` 传播。仓库路线的逻辑路径为 `MapPathingViewModel.PathJsonPath` 加去掉 `pathing/` 前缀的相对路径，只用于 `FileName`、日志和 `GetMaterialName()`，不读取该位置。这样仓库路线也能按材料目录选择队伍和路线 CD 规则。

### JS 工作目录

- 工作目录镜像仓库相对路径，例如 `Workspaces\official\js\自动采集\`，用户可以直接找到脚本的 `saved_files` 数据。
- 工作目录锁放在同级 `js\自动采集.lock`，替换目录时不受影响；启动时不清理，下次复用。
- 暂不支持：仓库中嵌套的 JS 项目，或只差大小写的两个项目路径，会落到同一目录。

## 不做的事

- 不加 `longPathAware` 清单，也不手动加 `\\?\` 前缀。
- 不在运行时截断路径，也不按长度拒绝执行。
- 不清理崩溃残留的临时目录、孤儿归档块，以及被替换后不再引用的账户图片。删除账户时，它不再被引用的图片随账户一并删除（[统筹设计 4.1](../instance-account-model.md#41-账户目录)）。
- 不识别旧格式数据：整治前的 GUID 仓库 ID、账户 ID 和旧缓存目录需由开发者手动删除 `User\Pulonia\`、`Repos\Pulonia\` 后重建。实例与账户统筹改造前的旧格式数据同样不识别，需手动删除后重建，见[统筹设计第 11 节](../instance-account-model.md#11-迁移)。
