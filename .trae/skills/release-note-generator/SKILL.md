---
name: "release-note-generator"
description: "Generates release notes from git commits/diffs and outputs one Chinese MD file. Invoke when user asks for release notes bounded by previous non-alpha version."
---

# Release Note Generator

用于基于当前仓库的 git 历史自动生成发布说明，确保不遗漏提交，并为每条变更记录追加提交人的 GitHub ID（`@id`）。默认输出一个 Markdown 文件到 `.trae/documents` 目录：变更内容。

## 触发时机

- 用户要求生成 Release Note / 更新日志
- 用户要求基于最近提交和实际代码变更做汇总
- 用户要求以“上一个非 alpha 版本”为边界统计变更

## 输入约定

- 默认分支当前 `HEAD`
- 版本边界规则：从最新提交向后遍历，遇到“提交信息包含版本号且不含 alpha”的最近一个提交作为边界
- 若用户指定分支、Tag 或版本正则，优先使用用户指定
- 未指定时，默认输出文档路径为：`.trae/documents/release_notes-<version>.md`

## 实战经验（本项目）

- 不直接依赖终端长输出：大量 `git log` 在终端中容易被截断，导致误判“提交缺失”
- 不用 `|` 等普通分隔符解析：提交标题可能包含分隔符，必须使用 NUL 分隔（`\x00`）保证可解析
- 统一 UTF-8：PowerShell 下中文提交信息可能乱码，调用 git 时需显式设置输出编码
- 避免在 `RunCommand` 的内联脚本中直接硬编码中文常量（标题、分类名、文件名）：在 Windows/PowerShell 链路中可能被替换为 `?`
- 发布文件名使用 ASCII：不要在文件名中使用中文（如 `ReleaseNotes-变更内容-<version>.md`），否则编码异常时会变成非法字符 `?` 导致写文件失败
- 中文文案仅放在 Markdown 内容里，并在写入后做一次乱码校验：若出现 `???`、`### ??` 等占位符，必须重新生成
- 明确分支范围：优先使用 `HEAD` 可达历史，必要时用 `--first-parent` 辅助核对发布主线
- 先计数再明细：先取 `rev-list --count`，再逐条采集，最后做数量一致性校验
- 版本边界要可回退：目标版本不存在时，自动回退到最近可识别的“上一非 alpha 版本”并在结果中声明

## 日志采集方式（强制）

- 必须使用 Python 脚本拉取 commit 与 diff，不直接用终端拼接命令产出最终结果
- Python 内通过 `subprocess.run([...], check=True, capture_output=True, text=True, encoding="utf-8", errors="replace")` 调 git
- git 命令需附加编码参数：`-c i18n.logOutputEncoding=utf8 -c core.quotepath=false`
- 提交元数据推荐格式：`--pretty=format:%H%x00%h%x00%an%x00%ae%x00%ad%x00%s%x00`，按 `\x00` 解析
- diff 证据至少包含：
  - 区间总览：`git diff --shortstat <base>..<head>`
  - 文件变更：`git diff --name-status <base>..<head>`
  - 行级统计：`git diff --numstat <base>..<head>`
  - 必要时单提交详情：`git show --name-status --numstat --format= <sha>`
- Python 脚本需输出结构化数据（JSON/对象）后再生成 release note 文本
- 若通过终端执行 Python，优先将中文文案写成 `\\uXXXX` 形式或由程序运行期拼接，避免命令传输阶段损坏字符

## 执行流程

1. 获取提交序列（防遗漏）
   - 从 `HEAD` 向后扫描提交，定位边界提交 `boundary_commit`
   - 使用可识别 SemVer 的正则提取版本号（例如 `v1.2.3`、`1.2.3`）
   - 判断“非 alpha”时使用大小写不敏感匹配：提交信息不包含 `alpha`
   - 生成统计区间：`(boundary_commit, HEAD]`，边界提交本身不计入变更列表
   - 使用 Python 执行 git 并解析 NUL 分隔结果，禁止依赖终端显示内容做解析

2. 完整性校验（必须）
   - 先取区间提交总数 `N`
   - 再逐条收集区间内每个 commit 的详情
   - 输出前校验“已收集条数 == N”，不一致则重新拉取，直到一致
   - 若存在 merge，额外给出 `--first-parent` 视角的校验结果，避免发布主线理解偏差

3. 采集每条提交的证据
   - 提交元数据：hash、标题、作者、时间
   - 实际改动：由 Python 采集 `--name-status`、`--numstat`、`--shortstat` 与必要 diff 片段
   - 将提交按功能模块归类（如 UI、任务、配置、脚本、基础设施等）

4. 提炼发布内容
   - 先按用户指定分类规则归类；若未指定，使用更接近 GitHub Releases 页面的默认分类顺序：
     1) 新功能
     2) 地图遮罩
     3) 独立任务
     4) JS相关
     5) 其他
   - 分类名尽量贴近业务语义：例如“地图追踪”“自动剧情”“七圣召唤”等可作为二级小节并入对应一级分类
   - 无法稳定归类的小变更统一放入“其他”
   - 每条摘要都要可追溯到至少一个 commit
   - 识别“变更最大点”并在首屏摘要中突出展示
   - 主体正文只展示可读的变更描述，不在分类正文中堆叠 hash
   - 完整提交清单放在文末，避免正文可读性下降
   - 输出文案需去掉 Conventional Commits 前缀：至少移除 `feat:`、`fix:`（区分大小写不敏感），再写入分类正文与完整提交清单

5. 生成提交人 GitHub ID（逐条记录追加 `@id`）
   - 必须以“当前方式”解析：对区间内每个 commit 逐条调用 GitHub 提交接口，禁止仅依赖本地 `author name`：
     - 接口：`https://api.github.com/repos/<owner>/<repo>/commits/<sha>`
     - 请求头至少包含：`Accept: application/vnd.github+json`、`User-Agent: <custom>`
     - 解析顺序固定：优先 `author.login`，为空时取 `committer.login`
   - 必须保证覆盖全量区间提交：
     - 对每个 SHA 单独请求并记录结果
     - 输出前校验“已解析 login 数 == 区间提交数”，不一致则进入回退链
   - 回退链（仅当 API 某条未命中时）：
     - 先用“同区间内已解析结果”做映射回填（同 author email / 同 author name）
     - 再解析 GitHub noreply 邮箱：
       - `12345+login@users.noreply.github.com` -> `@login`
       - `login@users.noreply.github.com` -> `@login`
     - 再回退 `.atom`：`https://github.com/<owner>/<repo>/commits/<branch>.atom` 按 SHA 匹配 `<entry>` 获取 `<author><name>`
     - 最后才允许作者名标准化兜底，并明确标记为推断
   - 为每条变更记录绑定对应作者的 `@id`
   - 若一条汇总涉及多个 commit，按去重后多作者格式追加：`@id1 @id2`

## 输出格式（强制，需贴近 GitHub Releases 风格）

必须产出 1 个文件：

1. 变更内容文件（全中文）
   - 文件路径：`.trae/documents/ReleaseNotes-<version>.md`（文件名必须 ASCII）
   - 内容结构（推荐模板）：
     - 一级标题：`## <version> <版本主题>`（示例：`## 0.58 月之五适配`）
     - 开场一句话：版本背景或问候语（可选，1~2 句）
     - 分类正文：按以下顺序输出存在内容的分类：
       - `新功能`
       - `地图遮罩`
       - `独立任务`
       - `JS相关`
       - `其他`（必须保留，即使只有 1 条）
     - 分类内每条为单行 bullet，格式：
       - `<变更描述> (#PR号可选) @githubId`
     - 若某条由多人提交，使用：`@id1 @id2`
     - 文末追加“完整提交清单”（倒序，不能缺项，每条带 `@id`）
     - 在标题下方补充统计信息：`边界提交 / 区间 / 提交数 / shortstat`

补充要求：
- 变更内容文件必须全中文
- 仅内容全中文；文件名必须为 ASCII，避免 Windows 下出现 `?` 非法路径
- 风格贴近 GitHub Releases：简洁分段、先讲用户可感知变化，再给完整清单
- 正文优先使用自然语言，不直接复制 commit 原文
- 分类不允许缺失“其他”
- 内容中不保留 `feat:`、`fix:` 前缀
- 生成后必须执行乱码检查：若命中 `\\?{2,}` 或 `### \\?+`，视为失败并重生成

## 质量门槛

- 不允许遗漏区间内任意 commit
- 不允许仅基于 commit 标题，必须结合实际代码改动
- 每条变更记录必须带作者 `@id`，不允许只在末尾集中列出
- 必须通过 Python 脚本获取日志与 diff，禁止直接依赖终端长输出结果
- 解析 `@id` 时必须逐条调用 GitHub Commit API（`/repos/<owner>/<repo>/commits/<sha>`），不得以本地昵称直接替代
- 未找到边界版本时，必须明确说明并给出当前采用的兜底范围
- 分类默认遵循“新功能 / 地图遮罩 / 独立任务 / JS相关 / 其他”，其中“其他”必须出现
- Markdown 文件必须实际落盘到仓库中的 `.trae/documents` 目录
- 不允许输出出现乱码占位符（如 `???`、`??` 分类名）；发现即判定为不合格
