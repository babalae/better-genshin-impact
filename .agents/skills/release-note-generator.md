---
name: "release-note-generator"
description: "基于 BetterGI 本地 git 历史和实际 diff，按 GitHub 已发布 Releases 的中文更新日志风格生成发布说明。用于生成 Release Note、版本更新日志、从上一个非 alpha 正式版本到 HEAD 的变更汇总；不得使用 git tag 作为版本依据。"
---

# Release Note Generator

基于本地 git 历史和实际代码 diff 生成 BetterGI 发布说明。发布说明的结构、分类和措辞应贴近 GitHub 已发布 Releases 的历史风格；生成具体内容时不依赖网络，所有变更信息从本地仓库获取。

## 触发时机

- 用户要求生成 Release Note / 更新日志
- 用户要求基于最近提交和实际代码变更做汇总

## 历史 Release 风格

已发布日志的稳定特征：

- 标题通常为 `## <version> <简短主题>`，例如 `0.61.0 原神6.6`、`0.61.2 优化`、`0.60.1 修复老问题`、`0.59 自动烹饪与BUG修复`。
- 大版本或内容较多时，标题下可有一句总览；补丁版本或内容很少时可以直接列 bullet。
- 分类标题使用项目语义，而不是 Conventional Commits 类型。常见分类包括：`版本适配` / `<月之X适配>`、`自动战斗`、`地图追踪`、`地图遮罩`、`独立任务`、`JS脚本` / `JS相关`、`实时任务`、`自动剧情`、`其他`。
- 版本适配类通常放在最前面，包含新角色识别、新地图、传送点/锚点、七圣召唤元数据等。
- `其他` 分类保留，用于 UI、通知、配置、稳定性、OCR、启动器、仓库、文档等无法归入核心模块的内容。
- 条目语言偏简洁直白，可以用“新增/修复/优化/支持/更新/回滚/适配”，不需要营销化。
- PR 号与作者使用纯文本，例如 `(#3132) @haokaiyang`；多个 PR 可以写在同一括号组里，例如 `(#3101 #3108)`。
- 不输出 GitHub 自动生成的 `Contributors`、`Assets`、reaction 等区块。

## 版本边界

禁止执行或参考 `git tag`、`git describe --tags`。版本边界和输出版本号都不能使用 tag 作为依据。

从 HEAD 向后遍历提交历史，匹配**明确以版本号发布为主题的提交**：

- 提交消息必须匹配以下模式之一（大小写不敏感）：
  - `Update version to X.Y.Z`
  - `bump version to X.Y.Z`
  - `release X.Y.Z`
  - `vX.Y.Z`
- 版本号正则：`\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?`
- 版本号**必须是提交消息的核心主题**，不能只是顺带提及（如 "fix: update package version to 1.0.19" 这种子包更新不算）
- 排除包含 `alpha` 的提交
- 排除仅更新子包/依赖版本的提交

### 通用规则

- 统计区间：`(boundary_commit, HEAD]`，边界提交本身不计入
- 如果 HEAD 附近已经存在当前目标版本的正式版本提交，不要把它作为边界；继续向父提交方向寻找上一个正式版本提交
- 若未找到边界版本，明确说明并使用兜底范围

## 信息采集

### 提交列表

- 使用 `git -c i18n.logOutputEncoding=utf8 -c core.quotepath=false log` 获取提交序列
- 格式：`--pretty=format:"%H%x00%h%x00%an%x00%ae%x00%s%x00%b%x1e"`，字段用 `\x00` 分隔，commit 用 `\x1e` 分隔
- 先用 `git rev-list --count` 取总数，采集后校验一致性
- 读取 commit body，用于提取 `Co-authored-by`、正文中的 PR/Issue 号和补充说明

### Diff 查阅（必须）

- 对每个 commit 必须查阅实际 diff：`git diff <sha>~1..<sha>`
- **不允许仅基于 commit 消息生成说明**，必须结合实际代码改动理解变更内容
- 同一功能的新增 + 后续优化/修复应合并为一条，只记"新增 xxx"

### 作者信息

- 所有作者名前统一加 `@` 前缀
- 从 commit 的 `author email` 解析标识：
  - `数字+login@users.noreply.github.com` → `@login`
  - `login@users.noreply.github.com` → `@login`
  - `login@gmail.com` 等非 noreply 邮箱 → `@author name`（即提交中的 `an` 字段值前加 `@`）
- 从 commit body 的 `Co-authored-by: name <email>` 提取共同作者，并按同样规则解析
- 当作者为 `huiyadanli`（含邮箱 `huiyadanli@gmail.com`）时省略 `@author`（项目维护者，无需标注）
- 不需要调用 GitHub API，纯本地解析

## 内容提炼规则

### 合并策略

- 同一功能在同一版本周期内先新增后优化/修复的，只保留"新增 xxx"，不单独列优化
- 同一 issue/PR 的多个 commit 合并为一条记录
- 同一文件或模块的多次小改动合并为一条有意义的描述
- 可省略纯内部重构、CI、调试日志清理、无用户影响的格式化，但这些 commit 仍必须被检查并纳入内部覆盖记录

### 分类

优先使用以下历史 Release 常见分类，按顺序输出。只有对应分类下有变更时才输出该分类标题，无变更则跳过不输出。

分类来源于项目已发布 GitHub Releases，与项目实际使用习惯对齐：

| 分类名 | 对应项目模块 | 覆盖范围 |
|--------|-------------|---------|
| 版本适配 / `<月之X适配>` | `Assets/` `AutoFight/Assets/` `AutoGeniusInvokation/Assets/` `AutoTrackPath/Assets/` | 新角色识别数据、新地图添加、传送点/锚点数据更新、七圣召唤角色牌等元数据更新 |
| 自动战斗 | `GameTask/AutoFight` `GameTask/Macro` `GameTask/SkillCd` | 战斗策略、角色切换、技能释放与CD、一键宏、经验值拾取、战后拾取、游泳检测、角色数据（非版本适配类） |
| 地图追踪 | `GameTask/AutoPathing` `GameTask/AutoTrackPath` `GameTask/MapMask` | 路径执行与所有路径处理器（含莉奈娅挖矿、拾取、采矿等 Handler）、地图类型、传送逻辑、地图编辑器（MapPathingPage、MapEditorWebBridge） |
| 地图遮罩 | `GameTask/MapMask` `View/MaskWindow*` `ViewModel/MaskWindow*` `Core/Config/MaskWindowConfig*` | 地图遮罩、日志遮罩、遮罩指标栏、遮罩位置和实时展示 |
| 独立任务 | `GameTask/AutoLeyLineOutcrop` `GameTask/AutoDomain` `GameTask/AutoBoss` `GameTask/AutoStygianOnslaught` `GameTask/AutoCook` `GameTask/AutoWood` `GameTask/AutoFishing` `GameTask/FarmingPlan` `GameTask/AutoSkip` `GameTask/QuickSereniteaPot` | 地脉花、秘境、首领讨伐、幽境危战、烹饪、伐木、钓鱼、养成计划、自动剧情、快速尘歌壶等独立运行的任务 |
| JS脚本 | `Core/Script` `Core/BgiVision` | JS 脚本 API、BvLocator、HTML 遮罩、模块导入、v8 引擎、脚本项目 |
| 实时任务 | `GameTask/TaskTriggerDispatcher*` `GameTask/AutoPick` `GameTask/AutoEat` `GameTask/Common` | 自动拾取、自动吃药、自动跳过、实时触发器、进入游戏与界面检测等 |
| 其他 | — | 无法归入以上分类的杂项（必须保留，即使为空也输出） |

- 归类原则：按**变更所属的项目模块（代码所在目录）**划分，而非按变更类型（新增/修复/优化）或用户感知的子功能划分
- 当一个变更涉及多个模块时，归入其主要逻辑所在的模块
- 版本适配类命名：若能从变更或用户要求明确对应原神版本/月之版本，可用 `<月之X适配>`；否则使用 `版本适配`
- 仅当本轮有多个明确的新功能且难以归入既有模块时，才使用 `新功能`
- 若某项变更无法明确归入上述分类，放入"其他"
- 遇到历史分类未覆盖的新模块，允许 AI 自行新建分类（放在"其他"之前），但分类名必须是项目语义，不要使用 `feat/fix/refactor`

每条记录格式：
```
描述内容 (#PR号) @author
```

- PR 号和作者名均使用纯文本，不加超链接
- 当作者为 `huiyadanli` 时省略 `@author`（项目维护者，无需标注）
- 描述用自然语言，不直接复制 commit 原文
- 移除 `feat:`、`fix:` 等 Conventional Commits 前缀
- PR 号从 commit 消息中提取（如 `(#3111)`）
- 多人参与的记录：`@id1 @id2`
- 合并多个 PR 时格式为 `(#3101 #3108)`，不要写成多个分散条目

### 描述质量

生成后必须做后处理检查，确保表达准确：

- 描述要准确反映实际变更的范围和内容，不能缩小也不能夸大
  - 如果 diff 显示新增了一张地图，就写"新增 xxx 地图"，而不是"新增 xxx 传送点"
  - 如果改动只涉及一个配置项，不要写成整体功能变更
- 面向用户的变更用用户视角描述（功能、体验、问题现象）
- 面向开发者的变更可以保留技术术语（接口、脚本引擎、配置项等）
- 去掉不必要的代码标识符（方法名、类名等），除非它是脚本开发者会直接使用的 API
- 纯内部重构、CI 变更等不影响使用的内容可以省略不列
- 主题句要概括本版本最重要的 2~4 个变化；不要把所有分类机械罗列一遍
- 重要新增功能优先写“新增 xxx”，后续修复/优化并入同一条描述

## 输出格式

输出到 `.claude/documents/ReleaseNotes-<version>.md`（文件名 ASCII）。

输出文件的版本号规则：取边界版本号的**次版本号（minor）加 1**，补丁版本归零。即 `major.(minor+1).0`。例如边界为 `0.60.1`，输出版本号为 `0.61.0`。

若用户明确指定版本号或发布主题，以用户指定为准；否则按上述规则推导。

```markdown
## <version> <版本主题>
<一句话版本主题>

### 分类名1
- 变更描述 (#PR号) @author
- 变更描述 @author

### 分类名2
- 变更描述 (#PR号) @author

### 其他
- 变更描述 @author
```

补丁版本或少量修复可以不分分类，直接输出 bullet；但如果使用分类，仍必须保留 `其他` 分类。

## 质量门槛

- 不允许遗漏检查区间内任意 commit；确认无用户影响的 commit 可以不输出
- 不允许仅基于 commit 标题，必须查阅实际 diff
- 同一功能的多次提交必须合并，不允许出现"新增 xxx""优化 xxx（同功能）"的冗余
- 优先使用历史 Release 分类，不允许自行发明无项目语义的分类名
- "其他"分类必须存在
- 不依赖网络，全部信息从本地 git 获取
- 输出前检查是否漏掉用户可见修复，尤其是同一新功能上线后的后续修复 commit
