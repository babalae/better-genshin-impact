# Pulonia 共享设置与设置方案

状态：步骤 1—3 与步骤 4 的一条龙入口已实现（2026-10-27），独立任务绑定未做；与本文的差异见[第 14 节](#14-实施记录)。已取代共享参数预设（`PuloniaTaskPreset`）和分组公共参数（`parameter_overrides`）。需求来源 US-04、US-09、US-12，社区 [#3168](https://github.com/babalae/better-genshin-impact/issues/3168)、[#3235](https://github.com/babalae/better-genshin-impact/issues/3235)、[#3323](https://github.com/babalae/better-genshin-impact/issues/3323)。  
[返回总览](../README.md) · [模型与配置](../model.md) · [界面](../user-interface.md) · [用户故事](../requirements.md)

## 1. 背景

### 1.1 旧调度器的配置组设置

旧配置组设置（`ScriptGroupConfig` → `PathingPartyConfig` + `ShellConfig`）覆盖地图追踪和 JS 的队伍、行走、战斗、拾取、食物与 Shell。用户反馈难用，原因如下：

- **整份二选一。**
  - 行走总开关 `Enabled` 关闭时，整份改用全局地图追踪条件配置，组内已填的值静默失效。
  - 战斗开关 `AutoFightEnabled` 关闭时，改用全局 `AutoFightConfig`；打开它又会强制打开行走总开关。
  - 没有逐项继承，组内项目也不能再覆盖组设置。
- **默认值陷阱。** 新组默认两个开关都打开，战斗配置是出厂值，所以修改全局自动战斗页对调度器不生效。这很可能是 #3168 的来源（推断）。
- **看不清、找不到。** 单页约 60 项、最深三层嵌套；多处文本框没有校验；看不到当前值来自哪里（#3323）。

### 1.2 Pulonia 现状

- **分组公共参数按任务类型分项。** 同一分组下的地图追踪和 JS 要各设一遍相同的队伍和战斗。独立任务的覆盖和按资源的覆盖只能写 JSON。
- **顶层字段整体替换。** `auto_fight_config` 是一个字段：
  - 改其中一项，就会把整份写成本层显式值；
  - 子节点改一项，就会遮住祖先的整份配置。
- **原样沿用旧界面。** 公共设置窗口复用 `ScriptGroupConfigView`，旧总开关（`pathing_enabled`、`auto_fight_enabled`、`shell_config_enabled`）保留。无人读取的 `js_script_use_enabled`、`solo_task_use_fight_enabled` 仍是参数。
- **恢复与来源。** 恢复继承只能整张表单或整个节点清空。来源只在高级区的 ToolTip 里以文本出现。
- **预设与公共参数是两套概念。** 预设有这些限制：
  - 只能按单一任务类型复用，只能挂在叶子上；
  - 没有新建界面，唯一来源是旧一条龙导入；
  - 账号替换 `preset_selections` 没有界面。

## 2. 目标

1. 任意节点（计划根、分组、引用节点、叶子）都能覆盖设置，覆盖逐项向下传播，越近越优先。
2. 覆盖粒度是单个设置项，包括战斗配置的子项。
3. 地图追踪和 JS 共用的设置项只设一次。
4. 每一项都能看到有效值和来源，并能单独恢复继承。
5. 命名方案可以跨计划复用，改一处，所有使用处生效。
6. 执行器读取的参数形状不变，快照不可变原则不变。

| | 旧调度器 | 本提案 |
| --- | --- | --- |
| 覆盖位置 | 配置组 | 任意节点 |
| 粒度 | 整份，靠总开关切换 | 单项 |
| 未设置时 | 总开关关闭回落全局，打开用出厂值 | 逐层继承，最外层为目录默认值 |
| 来源 | 不可见 | 每项可见，可查看整条链 |
| 复用 | 复制配置组 | 设置方案 |
| 恢复 | 无 | 单项或全部恢复继承，可撤销 |

**不做：**

- 不把全局 `AllConfig` 当作隐式默认值。唯一例外是条件选队，见[第 8 节](#8-执行器适配)。
- 不按账号替换方案，随 D5 决定。
- 方案不能引用方案；不支持条件表达式和按星期覆盖。
- 首批不让独立任务参与，见[第 11 节](#11-实施步骤)步骤 4。
- 任务自身参数不向下传播，例如秘境名、JS 脚本 `settings`、Shell 命令。

## 3. 概念与命名

| 名称 | 代码 | 含义 |
| --- | --- | --- |
| 共享设置 | `PuloniaTask.Settings` | 节点上的稀疏覆盖（设置项 ID → 值），可写在任意节点，向下传播 |
| 设置项 | `PuloniaSettingDefinition` | 代码注册的一项设置：ID、类别、名称、值类型、默认值、编辑提示 |
| 设置目录 | `PuloniaSettingCatalog` | 全部设置项，不持久化 |
| 绑定 | `PuloniaTaskDefinition.SettingBindings` | 能力声明自己使用哪些设置项、写入参数的哪个路径 |
| 设置方案 | `PuloniaSettingsProfile`（`prf_`，`profiles/<id>.json`） | 命名的共享设置集合，可挂在任意节点，跨计划复用 |
| 任务参数 | `PuloniaTask.Parameters` | 只属于这个任务、不传播的参数 |

面向用户的说明只有一句：**共享设置会传给下面所有任务，任务参数只属于这一个任务。**

| 现有 | 本提案 |
| --- | --- |
| 共享参数预设 `PuloniaTaskPreset`、`preset_id`、`presets/` | 设置方案 `PuloniaSettingsProfile`、`profile_id`、`profiles/` |
| 分组公共参数 `parameter_overrides`（按类型、资源） | 共享设置 `settings`（按设置项） |
| 计划引用公共参数（第 5 层）、`PublicParameters` | 引用节点上的共享设置，见 [6.2](#62-计划引用) |
| 账号替换预设 `preset_selections` | 删除 |
| `PuloniaTaskCommonSettings` 的表单职责、复用 `ScriptGroupConfigView` | 设置目录加新编辑器；`PuloniaTaskCommonSettings` 只保留参数与旧配置对象的互转 |

## 4. 设置目录

### 4.1 设置项

| 字段 | 说明 |
| --- | --- |
| `key` | 稳定 ID，格式 `<类别>.<名称>`，例如 `fight.strategy_name`；参数改名不改 ID |
| `category`、`order` | 编辑器分类与排序 |
| `display_name`、`description` | 中文名称与说明；这些字段不再登记到 `PuloniaTaskFieldNames` |
| `value_schema` | 值类型，沿用现有 Schema 子集 |
| `default_value` | 整条链都没有设置时使用的值 |
| `is_advanced` | 是否默认隐藏 |
| `editor` | 编辑提示，例如开关、数字（单位、范围）、选项、战斗策略、队伍名、角色位、采集 CD。服务层只给提示名，控件由视图层决定 |
| `visible_when` | 可选；另一设置项取指定值时才显示，例如玛薇卡相关项 |

设置项的值是原子单位。对象值（如采集 CD）整体替换。

### 4.2 绑定

`SettingBindings` 是“设置项 ID → 参数路径”的映射。路径用点分隔，各段与参数 JSON 的属性名一致，例如 `auto_fight_config.FinishDetectConfig.FastCheckEnabled`。绑定只做路径映射，不做值转换。

以下规则由单元测试保证：

- 路径在能力 Schema 中存在，类型与设置项一致。
- 能力默认值在该路径上的值等于设置项的默认值。不一致就不绑定。
- 被绑定覆盖的顶层参数（如 `auto_fight_config`）不再接受节点直接写入，也不接受调用覆盖。

能力没有绑定的设置项对它不生效，也不报错，这就是向下传播的含义。例如分组上设的 Shell 超时，不影响组内的地图追踪。

### 4.3 首批目录

首批覆盖地图追踪、JS 和 Shell，字段来自 `PathingPartyConfig`、`AutoFightConfig`、`AutoEatConfig` 和 `ShellConfig`。

| 类别 | 设置项（括号内为参数名） |
| --- | --- |
| 队伍与角色 `party` | 新增 `source`（见第 8 节）；`party_name`、`skip_party_switch`、`visit_statue_before_switch`（`is_visit_statue_before_switch_party`）、`main_avatar_index`、`guardian_avatar_index`、`guardian_skill_interval`（`guardian_elemental_skill_second_interval`）、`guardian_skill_long_press` |
| 战斗 `fight` | 常用：`strategy_name`、`timeout`、`action_scheduler_by_cd`、`swimming_enabled`<br>高级：`team_names`；盾奶位优先释放 `guardian_avatar`、`guardian_combat_skip`、`guardian_avatar_hold`、`burst_enabled` |
| 战斗结束检测 `fight_end` | `enabled`（`FightFinishDetectEnabled`）和 `FinishDetectConfig` 的 13 项。除总开关、快速检测、旋转寻敌外均为高级 |
| 战斗识别 `fight_vision`（全部高级） | 持续索敌、脱锁等待、识别间隔、伤害数字识别方式、绘制识别结果 |
| 拾取 `pickup` | 自动拾取（`auto_pick_enabled`）、战后光柱扫描及秒数、万叶拾取、万叶队伍、二次拾取、按经验拾取、战斗人次阈值、只拾取精英掉落 |
| 行走 `travel` | `auto_run_enabled`、`auto_skip_enabled`、`use_gadget_interval_ms`、`recover_timing` |
| 赶路 `hurry` | `hurry_on_avatar`、`travel_mode`、`distance`、`approach_stop_distance`、`switch_to_walk_enabled`、`hurry_on_frame_interval`（高级）；玛薇卡相关五项按赶路角色显示 |
| 食物 `eat` | `auto_eat_enabled`，以及三项默认料理（`auto_eat_config.Default*DishName`） |
| Shell `shell` | `disabled`、`timeout_seconds`、`no_window`、`output` |
| 采集 CD `collection` | `cooldown`（`collection_cd`），整体一个值，沿用现有采集 CD 编辑器 |

**各能力的绑定范围：**

- `pathing`：从队伍到食物的全部类别，加上采集 CD。
- `javascript`：从队伍到食物的全部类别。
- `shell`：只有 Shell。
- `keymouse` 的 `with_delay` 仍是任务参数。

**不进入目录的字段：**

- **旧总开关** `pathing_enabled`、`auto_fight_enabled`、`shell_config_enabled`。新模型中“未设置即继承”，总开关没有意义；执行时固定为 true，条件选队改由 `party.source` 表达。
- **没有读取方的** `js_script_use_enabled`、`solo_task_use_fight_enabled`，直接删除。
- **`auto_eat_config` 中旧界面未暴露的字段**（检测间隔等），保持能力默认值。

**Shell 默认值**按目前 `shell_config_enabled=false` 时的实际行为来取：不额外限时、隐藏窗口、不跳过，保证没有设置的 Shell 节点行为不变。实现时要逐项对照 `PuloniaShellTaskExecutor` 核对。

**跨项约束**由编辑器在行内提示，运行时沿用 `PathingPartyConfig` 现有的修正：

- 接近停止距离 ≤ 赶路启用距离；
- 跳飞距离 > 赶路启用距离；
- “旋转寻敌”与“敌人可见时跳过检查”互斥。

## 5. 数据模型

计划 `schema_version` 从 2 升到 3。

| `PuloniaTask` 字段 | 变化 |
| --- | --- |
| `settings` | 新增。JSON 对象，设置项 ID → 值；任意节点可写；空对象表示全部继承 |
| `profile_id` | 新增，可空；任意节点可写 |
| `parameters` | 只放任务参数；分组仍为空对象；不得包含被绑定的顶层参数 |
| `preset_id`、`parameter_overrides` | 删除 |

`PuloniaTaskAccountBinding.preset_selections` 也删除。

```json
{
  "id": "tsk_…",
  "name": "蒙德采集",
  "task_type": "group",
  "profile_id": "prf_…",
  "settings": {
    "fight.strategy_name": "那维莱特",
    "fight_end.fast_check_enabled": true,
    "pickup.kazuha_pickup_enabled": false
  },
  "parameters": {},
  "children": []
}
```

设置方案没有 `task_type`、`resource_id` 和 `schema_version`，因为设置项 ID 全局唯一、含义固定：

```json
{
  "id": "prf_…",
  "name": "深渊队",
  "description": "",
  "revision": 4,
  "settings": { "party.party_name": "深渊队", "fight.strategy_name": "那维莱特" }
}
```

**校验：**

- 节点和方案中的键必须存在于目录中，值按 `value_schema` 校验。未知键按“不允许未声明的参数”处理。
- 叶子的 `settings` 只能包含其能力绑定的设置项；分组、根和引用节点不限。
- `profile_id` 指向的方案缺失时，保存仍允许，预览提示，运行准备报错。

**快照：**

- 新增 `profile_json_by_id`，固定本次使用的方案。
- `preset_json_by_id` 保留为只读，保证旧历史可读；新快照中为空。
- `PuloniaTaskPreparedTask` 新增 `setting_sources`。

## 6. 解析规则

仍由 `PuloniaParameterResolver.Resolve` 一处实现。层级从低到高：

1. **能力默认值。**
2. **共享设置。** 从目录默认值开始，沿设置链由外到内叠加，再按绑定写入参数。
3. **任务参数。** 叶子 `parameters`，顶层字段替换。
4. **本次调用覆盖。** 只改任务参数。

最后按能力 Schema 校验。原先的预设层、分组公共参数层和引用公共参数层合并进第 2 层。

### 6.1 设置链

设置链是从计划根到叶子的节点序列。每个节点贡献两层：先是所挂的方案，再是自身的 `settings`。同一项，后写的生效。

| 节点 | 方案 | `settings` |
| --- | --- | --- |
| 计划根 | — | `fight.strategy_name = A` |
| 分组「蒙德」 | 「深渊队」：`fight.strategy_name = B`、`party.party_name = 深渊队` | — |
| 子组「风车菊」 | — | `party.party_name = 采集队` |
| 叶子「风车菊-01」 | — | — |

叶子得到的结果：

- 战斗策略 B，来源“方案「深渊队」（分组「蒙德」）”；
- 队伍“采集队”，来源“分组「风车菊」”；
- 其余项为默认值。

### 6.2 计划引用

引用节点 R 指向计划 P 时，设置链为：外部祖先 → P 的根 → R → P 内部节点 → 叶子。含义如下：

- **P 的计划级设置优先于外部祖先。** 外部只能补上 P 没有设置的项。
- **R 上的设置（含方案）覆盖 P 的计划级设置。** 也就是在这次引用中替换它的计划级设置。
- **P 内部分组和任务的设置始终保留。**

| 层 | `fight.strategy_name` |
| --- | --- |
| 外部根 | A |
| P 的根 | B |
| 引用节点 R | C |
| P 内的分组 | 未设置 → 叶子得到 C；若设为 D → 叶子得到 D |

现在的做法是“边界处清空，只放行声明为公共的参数”。新规则更符合向下传播的直觉，也不再需要 `PublicParameters`。任务参数属于叶子，本来就不跨越引用。目录引用是普通分组。

### 6.3 写入能力参数

1. 先算出叶子的有效设置：目录中每一项的值和来源。
2. 再按能力的绑定，把每一项写入参数路径的末端。未绑定的项忽略。

因为只写路径末端，`auto_fight_config` 的不同子项可以来自不同层。任务参数仍然是顶层整体替换。执行器拿到的参数形状和现在一样，不需要知道设置链。

### 6.4 来源

- **`setting_sources`**：设置项 ID → `default`、`node/<地址>` 或 `profile/<方案 ID>@<地址>`。
- **`parameter_sources`**：任务参数照旧记录；被绑定覆盖的顶层参数记为 `settings`。
- **界面文本**：默认、计划「每日」、分组「蒙德」、方案「深渊队」（分组「蒙德」）、本任务。

### 6.5 预览与运行

| 情况 | 运行准备 | 界面预览 |
| --- | --- | --- |
| 方案缺失 | 报错，请求不入队 | 跳过该方案，在树和设置页提示 |
| 引用节点 | 按 6.2 | 引用节点的设置页按 6.2 显示传入的值；编辑被引用计划时只看它自己 |
| 调用覆盖 | 应用 | 不应用 |

方案原文固定在快照中。修改方案不影响已排队和正在运行的请求，续跑沿用原快照。

## 7. 界面与交互

设计原则：

- **默认路径短。** 常用项直接可见，高级项收起，搜索可以直达任一项。
- **改一项就是覆盖一项。** 没有总开关，修改的项有明显标记，每项都能单独恢复继承。
- **看得到来历。** 每项旁显示来源，点开可看到整条链上的值。
- **即时生效。** 修改立即写入并自动保存，可以撤销，与一条龙右侧设置区一致。

### 7.1 入口

| 位置 | 入口 | 编辑对象 |
| --- | --- | --- |
| 任务计划页 · 计划卡片右键 | 计划共享设置… | 计划根节点 |
| 任务树空白处右键 | 计划共享设置… | 计划根节点 |
| 节点右键 | 共享设置… | 该节点 |
| 节点设置弹窗 | “共享设置”页 | 该节点 |
| 任务计划页头部菜单 | 设置方案… | 方案管理窗口 |
| 新一条龙 ·“更多”菜单 | 配置共享设置… | 当前配置的根节点 |
| 新一条龙 · 右侧设置区 | “共享设置”卡片 | 选中的节点 |

节点设置弹窗改为左侧导航：基本、任务参数、共享设置、执行策略、高级。

- 删除“行走、战斗与执行”的三个按钮和参数预设下拉框。
- 采集 CD 卡片并入共享设置的“采集 CD”类别。
- “高级”页保留原始 JSON（参数、共享设置、策略、来源）和“最终生效的参数”。
- 能力没有任何绑定的叶子（录制回放、首批中的独立任务）不显示“共享设置”页。

### 7.2 共享设置编辑器

同一个控件用于节点弹窗、计划共享设置窗口、一条龙卡片（紧凑模式）和方案编辑（方案模式）。

```
┌ 共享设置 · 分组「蒙德采集」 ───────────────────────────────────────────┐
│ 设置方案 [ 深渊队 ▾ ]  编辑方案  另存为方案…                            │
│ [🔍 搜索设置              ]   ☐ 只看本节点修改   ☐ 显示高级设置         │
├────────────────┬───────────────────────────────────────────────────────┤
│ 队伍与角色   ● │ 战斗                              影响本组 12 个任务   │
│ 战斗         ● │ ▌战斗策略        [ 那维莱特        ▾ ]   本节点      ↺ │
│ 战斗结束检测   │  战斗超时        [ 120 ] 秒             方案「深渊队」 │
│ 拾取           │  游泳检测        [ 开 ]                 计划「每日」   │
│ 行走           │  按 CD 跳过出招  [            ]          默认          │
│ 赶路           │                                                        │
│ 食物           │                                                        │
│ 采集 CD        │                                                        │
│ Shell（无任务）│                                                        │
├────────────────┴───────────────────────────────────────────────────────┤
│ 本节点覆盖 3 项              清除与继承值相同的覆盖    全部恢复继承     │
└────────────────────────────────────────────────────────────────────────┘
```

**行：**

- **结构。** 每行依次是名称与说明、编辑控件、来源、恢复继承按钮。
- **覆盖标记。** 本节点覆盖的项左侧显示强调色竖条，来源为“本节点”，并出现 ↺。
- **值的来历。** 来源文字可点击，弹出“值的来历”：从默认值到本节点逐层列出每层的值，并高亮生效的那一层。
- **修改即覆盖。**
  - 修改继承来的值，就在本节点创建覆盖。
  - 改回与继承值相同时不自动删除覆盖，避免输入过程中来回跳动；行内提示“与继承值相同”，底部按钮可批量清理。
- **输入与撤销。** 逐字输入中的无效内容只跳过并提示，规则同一条龙右侧设置区；同一项的连续输入合并为一次撤销。

**类别：**

- 导航上的圆点表示该类别有本节点覆盖。
- 分组页的类别标题显示受影响的任务数，并按类型拆分；目录引用和计划引用各算一项。
- 组内没有任务会用到的类别置灰，注明“本组下没有使用这些设置的任务”。它仍可设置，以后加入的任务会生效。
- 叶子只显示其能力绑定的类别。

**联动：**

- 按 `visible_when` 显示或隐藏，例如赶路角色不是玛薇卡或“自动”时，隐藏玛薇卡相关项。
- `party.source` 为“按地图追踪条件配置”时，队伍名和角色位置灰，并说明由条件配置决定。
- 违反跨项约束时，行内警告，并说明运行时会怎样修正。

**筛选：**

- 搜索匹配名称、说明和 ID，导航显示各类别的命中数。
- “只看本节点修改”用来快速回顾这个节点到底改了什么。

**方案模式：**

- 不显示继承，各行只显示“已设置”或“未设置”。
- ↺ 改为“移除”。
- 默认打开“只看已设置”。

### 7.3 设置方案

**选择与使用：**

- 顶部下拉框包含：不使用方案、各方案（附引用次数）、新建方案…、管理方案…。换方案即时生效，可以撤销。
- 在节点上修改永远只改本节点。要改方案本身，点“编辑方案”。来自方案的项，来源显示为“方案「名称」”。

**另存为方案：**

- 把本节点自己的覆盖（不含继承来的）存成新方案。
- 默认勾选“保存后本节点改用该方案，并移除这些覆盖”。

**方案管理窗口：**

- 左侧是方案列表（名称、引用数），右侧是方案模式的编辑器。
- 支持新建、复制、重命名、删除。
- “从全局配置创建”会显式读取当前全局自动战斗配置和地图追踪条件配置中能对应到目录的项，只把与默认值不同的项写成新方案。旧用户可以借此把原来的全局设置一次性带过来。

**保存与删除：**

- 方案编辑是草稿，显式保存。保存前显示影响范围，例如“将影响 3 个计划中的 7 个节点；已排队和正在运行的任务不受影响”。
  - 原因：方案影响多个计划，也不在单个计划的撤销历史里。
- 保存走修订检查，冲突时报错并保留草稿。
- 删除仍被引用的方案时，列出引用位置，要求先改掉引用；不做级联删除。

### 7.4 任务树提示

- 节点行尾显示小标记：
  - 有覆盖时，显示齿轮和覆盖数量；
  - 挂了方案时，显示方案名标签；
  - 悬停时列出本节点覆盖的设置名和值。
- 方案缺失时，标记使用警示色。

### 7.5 移动、粘贴与撤销

- **撤销。** 改设置、换方案都是计划文档中的一次可撤销编辑；方案文件的修改不进入撤销历史。
- **复制。** 复制子树时保留每个节点的 `settings` 和 `profile_id`。
- **粘贴。** 沿用现有的“粘贴前显示有效值差异”，范围扩大到共享设置。选择“保留当前有效设置”时，把不一致的项写到粘贴的子树根节点上，而不是逐个叶子写入。
- **拖动。**
  - 移动后若有叶子的有效设置发生变化，显示非模态提示“移动后 N 个任务的 M 项设置已改变”，提供“查看”和“保留原值”。
  - “保留原值”同样写在被移动的子树根上。
  - 忽略提示即接受新位置的继承。

### 7.6 新一条龙页

- 一条龙配置的根节点就是“配置级共享设置”，从“更多 → 配置共享设置…”进入。
- 选中地图追踪、JS、Shell 或引用行时，右侧设置区增加“共享设置”卡片。卡片内嵌编辑器的紧凑模式：没有类别导航，按类别分组排列卡片。
- 独立任务在步骤 4 之前没有共享设置。
- 左侧清单行显示与任务树相同的覆盖和方案标记。

## 8. 执行器适配

**地图追踪与 JS：**

- 参数形状不变，继续用 `PuloniaTaskCommonSettings.FromParameters`。
- 构造出 `PathingPartyConfig` 后，固定 `Enabled = true`、`AutoFightEnabled = true`。战斗配置一律来自参数，不再回落全局 `AutoFightConfig`。

**条件选队：**

- `party.source` 取 `fixed`（默认）或 `condition`，绑定到新增参数 `party_source`。
- 取 `condition` 时，执行器设置 `PathingPartyConfig` 上新增的运行时属性（`JsonIgnore`，与 `SkipPartySwitch` 同类）。`PathExecutor` 据此处理：
  - 队伍名按地图追踪条件配置选择；
  - 行走位、生存位及其 E 设置，用 `BuildPartyConfigByCondition` 的结果覆盖；
  - 其余字段保持参数值，包括战斗、赶路、拾取、食物和回复时机。
- 旧调度器 `Enabled=false` 时的行为不变（整份回落）。JS 内调用地图追踪时，同一属性随配置对象传递。
- 这是唯一仍读取全局配置的地方，因为条件规则本身就存在全局地图追踪条件配置中，列为待定 S2。

**Shell：** `EnableShellConfig` 固定为 true，各项取参数值；默认值见 4.3。

**能力说明：**

- `PuloniaTaskDefinition` 新增 `SettingBindings`，删除 `PublicParameters`。
- `PuloniaTaskCommonSettings` 的参数 Schema 不再包含旧总开关和死字段。

## 9. 导入与旧数据

Pulonia 尚未发布。按[模型与配置](../model.md)的开发期约定，schema 3 不读取 schema 2：

- 旧计划文件按现有“坏文件”规则跳过并提示，文件保留在磁盘上。
- 旧调度器、d-v3 和旧一条龙的数据通过导入器重新导入。
- `presets/` 下的文件不再读取，也不删除。

导入器改动如下：

| 来源 | 转换 |
| --- | --- |
| 旧配置组 `ScriptGroupConfig`，`Enabled=true` | 只把与目录默认值不同的项写入该分组的 `settings`；避免现在全量写入、遮住上层设置的问题 |
| 同上，`AutoFightEnabled=false` | 战斗项改为冻结导入时的全局 `AutoFightConfig`（与旧行为等价） |
| 同上，`Enabled=false` | 写 `party.source=condition`；冻结全局 `AutoFightConfig`；冻结 `BuildDefault` 借用的回复时机、小道具间隔、自动吃药；不写组内行走与赶路项（旧行为中它们被忽略） |
| 同上，`EnableShellConfig=false` | 不写 Shell 项 |
| d-v3 的 `pathing_party_config` | 按同样规则写入节点 `settings`；目录引用写在引用分组上 |
| 旧一条龙借用的全局配置 | 直接写入各独立任务节点的任务参数，不再生成预设 |

冻结全局配置的位置会写进差异报告：“已按导入时的全局配置固定”。

## 10. 分层与代码落点

| 层 | 新增或修改 |
| --- | --- |
| 模型 `Pulonia/Models` | `PuloniaTask.Settings`、`ProfileId`；`PuloniaSettingsProfile`；`PuloniaTaskDefinition.SettingBindings`；快照字段 |
| 服务 `Pulonia/Services` | `PuloniaSettingCatalog`（纯数据，不引用 WPF）<br>`PuloniaParameterResolver` 改为设置链加绑定写入（仍是唯一实现）<br>`PuloniaTaskBuilder` 组装设置链、固定方案<br>`PuloniaTaskStore` 增加方案的增删改查和引用统计<br>`PuloniaTaskValidator`、`PuloniaTaskLegacyImporter` |
| 执行 | 地图追踪、JS、Shell 执行器；`PathExecutor` 的条件选队运行时属性 |
| ViewModel | `PuloniaSharedSettingsEditorViewModel`（节点模式、方案模式）与行 VM<br>文档 VM 的 `SetSetting`、`ResetSetting`、`ResetAllSettings`、`RemoveRedundantSettings`、`SelectProfile`<br>方案管理 VM<br>选项沿用 `PuloniaOneDragonSettingChoices` 与 `CombatStrategyPicker` |
| View | 共享设置编辑器控件（含紧凑模式）、方案管理窗口；窗口经 `IPuloniaTaskSettingsDialogs` 创建，VM 不直接创建窗口 |
| 删除 | 预设的模型、存储与下拉框；`parameter_overrides` 及其界面；公共设置窗口（`EditCommonSettings`、`PuloniaOneDragonCommonSettingsDialog`）；Pulonia 对 `ScriptGroupConfigView` 的复用；`PublicParameters` |

删除 `PuloniaOneDragonCommonSettingsDialog` 后，[已知偏差](../README.md#已知偏差)中的第一条随之消失。新 XAML 遵守项目约定：只读绑定显式 `Mode=OneWay`，局部样式 `BasedOn` WPF-UI 默认样式，类别模板要验证实际实例化与布局。

## 11. 实施步骤

1. **目录与解析。**
   - 内容：设置目录、绑定、模型字段、解析器与 Builder、来源与快照、校验、导入器、执行器适配。
   - 单元测试：绑定一致性（路径、类型、默认值）、链顺序、深写入、引用顺序、导入等价。
2. **编辑器。**
   - 内容：共享设置编辑器、节点弹窗的“共享设置”页、计划共享设置入口、树标记、粘贴与拖动的差异提示。
   - 同时删除公共设置窗口和 `parameter_overrides` 的界面。
3. **设置方案。** 模型、存储、选择、另存为、管理窗口、从全局配置创建；删除预设。
4. **一条龙与独立任务。**
   - 一条龙入口和紧凑编辑器。
   - 独立任务按“语义和默认值都完全一致”逐个评估是否绑定。建议只绑定调优类（战斗结束检测、战斗识别、拾取），不绑定选择类（队伍、战斗策略），避免采集分组上设的采集队传到秘境。

实施时同步更新 [model.md](../model.md) 的解析规则、[user-interface.md](../user-interface.md) 的节点配置，以及 [requirements.md](../requirements.md) 的“复杂规则 → 用户故事”表。

## 12. 验收场景

1. 计划根设战斗策略 A，分组 G 设 B。G 下的地图追踪和 JS 叶子都显示 B，来源为“分组「G」”。G 恢复继承后显示 A，来源为“计划”。
2. 分组设 `fight_end.fast_check_enabled`，叶子设 `fight_end.rotary_factor`。叶子快照的 `auto_fight_config.FinishDetectConfig` 中两项都生效，其余子项为默认值。
3. 分组设 Shell 超时，不影响组内的地图追踪；分组设战斗策略，不影响组内的 Shell。
4. 两个计划的分组挂同一方案。修改方案后，两处预览立即更新；修改前已排队的运行仍用旧值。
5. 引用顺序：外部根 A、被引用计划根 B、引用节点 C，叶子得到 C；被引用计划内的分组设 D，叶子得到 D。
6. 方案文件缺失时，树和设置页给出提示；提交运行被拒，并说明缺失的是哪个方案。
7. 把子树拖到另一分组且有效设置变化时，出现提示。选择“保留原值”后，叶子有效值与移动前一致；撤销一步回到移动前。
8. 导入 `Enabled=false` 的旧配置组：`party.source=condition`，战斗设置等于导入时的全局自动战斗配置。同一路线在旧调度器和新计划中的有效 `PathingPartyConfig` 一致，已删除的总开关和死字段除外。
9. `party.source=condition` 时，赶路和拾取设置仍按共享设置生效（旧调度器会回落默认值）。
10. 搜索“万叶”能找到万叶拾取和万叶队伍；“只看本节点修改”只列出覆盖项。

## 13. 待定

| ID | 问题 | 建议 |
| --- | --- | --- |
| S1 | 是否需要跨计划的“全局默认方案”：新建计划时自动挂在根上，或作为整条链的最外层 | 首版不做，用计划根挂方案 |
| S2 | 条件选队仍读取全局地图追踪条件配置 | 保留；条件规则是否迁入目录另议 |
| S3 | schema 2 的计划是否需要一次性转换 | 未发布，不转换。若需要：先做结构转换，再用旧解析结果逐叶子比对，不一致处在叶子上写显式值 |
| S4 | 节点弹窗的其余部分（基本、任务参数）也改为即时生效，去掉保存和取消 | 建议做，与一条龙一致，另行排期 |
| S5 | 独立任务的绑定范围 | 见第 11 节步骤 4 |
| S6 | 删除 `preset_selections` 后，普通计划触发器的“账号参数资料”失去用途 | 并入 D5 |
| S7 | C# 或外部请求的调用覆盖能否改共享设置 | 首版不允许，调用覆盖只改任务参数 |

## 14. 实施记录

2026-10-27 实现，与上文的差异：

- **一条龙没有内嵌紧凑编辑器。** 右侧设置区的“共享设置”卡片显示摘要，点击打开与任务计划页相同的共享设置窗口（`PuloniaSharedSettingsWindow`，即时生效、可撤销）。编辑器控件因此只有节点模式和方案模式。
- **新增 `keymouse` 类别。** 只含“按录制延时回放”（`with_delay`），使目录引用的录制回放也能经共享设置控制；录制回放叶子因此显示“共享设置”。
- **新增任务弹窗只保留五个常用项。** 共享设置绑定的参数在新增弹窗中隐藏，只保留 `party_name`、`skip_party_switch`、`auto_pick_enabled`、`auto_skip_enabled`、`with_delay`；创建服务把它们写进叶子的 `settings`，其余字段写进 `parameters`。目录引用若还剩任务参数，创建时报错。
- **拖动提示的“查看”是展开明细。** 提示条列出“任务：设置项 旧值 → 新值”（最多 30 行），不打开设置窗口。“保留原值”与移动合为一步撤销；移动后若已有其他编辑或撤销，不再写入。
- **计划引用的估算。** 编辑器看不到被引用计划的内部，设置页按全部设置项计入引用，拖动比较也按全部设置项比较。被引用计划根自身设置了某项时，提示可能多报；此时“保留原值”写在引用节点上，会覆盖被引用计划根的值（暂不处理）。
- **独立任务没有绑定。** 第 11 节步骤 4 的独立任务评估尚未开始。旧数据中独立任务组的覆盖和 JS 按资源的组设置不再导入为共享设置。
- **快照兼容。** `preset_json_by_id` 仍然写出（新快照为空对象）；`profile_json_by_id` 与 `setting_sources` 为空时不写，旧快照往返不变。
