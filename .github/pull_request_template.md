<!--
感谢你的贡献！提交前请先阅读以下说明 / Thanks for contributing! Please read this before submitting:
1. 请按模板逐项填写，请勿删除模板中的标题，否则内容无法被正确识别 / Fill in every section and do not delete the headings.
2. 未填写关键信息（解决的需求、是否在游戏内实际运行、代码是否纯手工编写）的 PR 可能会被直接关闭 / PRs missing key information may be closed directly.
3. 请保持改动聚焦，避免在一次 PR 中混入与需求无关的大规模改动 / Keep the change focused.
-->

## 提交确认 / Checklist

- [ ] 我已经搜索过现有 Issue 与 PR，确认这不是重复内容 / I have searched existing issues and PRs and confirmed this is not a duplicate
- [ ] 我已经阅读过[文档](https://www.bettergi.com/doc.html)，确认改动符合项目的使用方式 / I have read the [documentation](https://www.bettergi.com/doc.html) and confirmed the change matches the project's usage
- [ ] 本次改动可以通过 `dotnet build BetterGenshinImpact.sln -c Debug` 编译 / This change compiles with `dotnet build BetterGenshinImpact.sln -c Debug`
- [ ] 我理解未经过实际验证的改动，可能被要求补充证据后再合并 / I understand unverified changes may require more evidence before merging

## 关联 Issue / Related Issue

<!-- 例：close #1234 / Fixes #1234；没有关联 Issue 可填写“无” / Write "None" if there is no related issue -->

## 解决的需求 / Problem Solved

> 请说明这个 PR 要解决什么问题，而不是只描述改了哪些代码 / Explain the problem this PR solves, not just which code was changed.

**要解决的需求或问题是什么？/ What problem does this PR solve?**

**在什么场景下会触发或需要它？/ In which scenarios is it needed?**

**实现方案简述 / Implementation summary**

## 是否在游戏内实际运行 / Verified In-Game

> BetterGI 的行为高度依赖真实游戏环境（分辨率、帧率、任务状态、网络延迟等），因此请如实填写本项，以下两项请只勾选一项。/ BetterGI's behavior heavily depends on the real game environment, so please answer this honestly. Check only one of the following.

- [ ] 已在游戏内实际运行并验证本次改动 / Yes, I actually ran and verified this change in the game
- [ ] 未在游戏内实际运行 / No, I have not run it in the game

**若已在游戏内运行，请补充验证信息 / If yes, please provide details:**

| 项目 / Item | 内容 / Detail |
| --- | --- |
| 游戏版本 / Game version | |
| 分辨率与窗口模式 / Resolution & window mode | |
| 验证的功能或任务 / Verified feature or task | |
| 验证次数与运行时长 / Runs & duration | |
| 结果 / Result | 符合预期 / 存在问题（请说明） |

**若未在游戏内运行，请说明原因与替代验证方式（例如仅文档、仅 UI 调整、仅重构、单元测试）/ If not, explain why and what alternative verification was done:**

## 代码是否纯手工编写 / Hand-Written Code

> 本项目推荐使用 AI 辅助编写代码，但要求提交者完全理解并验证自己提交的每一行代码，并对最终结果负责。以下三项请只勾选一项。/ AI-assisted coding is recommended, but you must fully understand and verify every line you submit, and you are responsible for the result. Check only one of the following.

- [ ] 本次代码完全由我手工编写，未使用 AI 生成 / Entirely hand-written, no AI generation
- [ ] 部分代码由 AI 辅助生成，我已逐行阅读、理解并验证 / Partly AI-assisted; I have read, understood and verified it line by line
- [ ] 本次代码完全由 AI 生成，我已逐行阅读、理解并验证 / Fully AI-generated; I have read, understood and verified it line by line

**AI 参与的部分及我的验证方式（未使用 AI 请填“无”）/ AI-involved parts and how I verified them (write "None" if no AI was used):**

**我确认 / I confirm:**

- [ ] 我理解本次改动的全部逻辑，能够解释每一处修改的原因 / I understand the entire change and can explain every modification
- [ ] 提交的代码不包含来源不明、我无法解释的代码片段 / No unexplained code from unknown sources is included

## 改动类型 / Type of Change

- [ ] 新功能 / New feature
- [ ] 问题修复 / Bug fix
- [ ] 重构或性能优化 / Refactor or performance
- [ ] 文档或其他杂项 / Docs or chores

## 影响范围 / Impact

**涉及的模块或功能 / Affected modules or features:**

**可能带来的风险、兼容性影响或行为变化 / Potential risks, compatibility impact, or behavior changes:**

## 补充信息 / Additional Information

<!-- 截图、录屏、日志、参考链接等，可选 / Screenshots, recordings, logs, references (optional) -->
