# Pulonia 设计文档归档

本目录保存过程记录和被替换的旧文原文，供追溯，不代表当前实现。当前基线从 [Pulonia 架构总览](../README.md) 开始。归档时只修正了相对链接的路径，正文未改。

| 文件 | 来源 | 归档原因（2026-10-24） |
| --- | --- | --- |
| [automation-system.md](automation-system.md) | `Docs/design/automation-system.md` 正文 | 总览改由 `pulonia/README.md` 承担，原文件保留为跳转页 |
| [development-plan.md](development-plan.md) | `pulonia/development-plan.md` | 逐步交付、验收操作与编译记录；精简为 [roadmap.md](../roadmap.md) |
| [review-step-9.md](review-step-9.md) | `pulonia/review-step-9.md` | 步骤 9 清理与审查的过程记录 |
| [task-model.md](task-model.md) | `pulonia/task-model.md` | 含步骤 1 类型清单、过时类名与多处配置优先级描述；改写为 [model.md](../model.md) |
| [scheduling-and-managed.md](scheduling-and-managed.md) | `pulonia/scheduling-and-managed.md` | 拆为 [scheduling.md](../scheduling.md)、[accounts.md](../accounts.md)，托管部分移入 [proposals/managed-hosting.md](../proposals/managed-hosting.md) |
| [collection-cooldown.md](collection-cooldown.md) | `pulonia/collection-cooldown.md` | 现行规则并入 [results-and-storage.md](../results-and-storage.md)；原文含路线返回值改造的影响评估与开发验证 |
| [script-repositories.md](script-repositories.md) | `pulonia/script-repositories.md` | 早期多仓库草案；仍有效的所有权与身份原则并入 [repository-resources.md](../repository-resources.md) |
| [repository-resources-details.md](repository-resources-details.md) | `pulonia/repository-resources.md` 的部分段落 | 仓库选择器像素细节与测试记录 |
| [identifiers-and-paths-remediation.md](identifiers-and-paths-remediation.md) | `pulonia/identifiers-and-paths.md` 全文 | 整治前现状、API 调整、实施步骤、决策过程与实施记录；现行规则见 [identifiers-and-paths.md](../identifiers-and-paths.md) |
| [user-interface-step-notes.md](user-interface-step-notes.md) | `pulonia/user-interface.md` 的部分段落 | 步骤 2、步骤 5 实现记录与 10.1 账号准备段落 |
| [CONTEXT-2026-10-24.md](CONTEXT-2026-10-24.md) | 根目录 `CONTEXT.md` | 精简前的术语表，含体力计划术语与已不存在的“开发工作目录” |

`proposals/` 下的[体力计划](../proposals/resin-plan.md)和[目标托管](../proposals/managed-hosting.md)不是归档，而是尚未实现的提案。
