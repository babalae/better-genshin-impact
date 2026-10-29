# 设计文档 / ADR

本目录存放设计文档与架构决策记录（Architecture Decision Record）。

每份 ADR 建议记录：背景、备选方案、决策结果，以及取舍原因。

## 文档列表

- [Pulonia 任务系统架构总览](pulonia/README.md)（路线图与验收状态见 [pulonia/roadmap.md](pulonia/roadmap.md)；旧入口 [automation-system.md](automation-system.md) 保留为跳转页）
- [三种实例的能力边界与 IPC 规划](instance-capability-boundaries.md)（现状核对与后续规划）
- [BetterGI 多实例命名管道协议](multi-instance-ipc.md)
- [BetterGI 输入层 InputHub 设计](input-hub.md)
- [遮罩窗口（MaskWindow）统一设计](mask-window.md)
- [配置持久化链路（AllConfig 保存）优化](config-persistence.md)
- [实时触发器生命周期与启停设计](realtime-trigger.md)
- [TaskTriggerDispatcher 依赖注入改造设计](task-trigger-dispatcher-di.md)
- [移除 TaskContext：按生命周期显式传递 GameRuntime](task-context-removal.md)
- [BetterGI 外部访问服务设计](external-access.md)
- [独立任务合同重构：ISoloTask 泛型化](task-contract.md)（核心迁移已完成，统一会话租约待实施；2026-10-24 核对）
- [自动战斗策略统一：策略模型、键鼠录制实现与策略选择控件](auto-fight-strategy.md)（第 3 版，已确认）
- [树脂使用设置统一：弹窗界面、参数合同与任务适配](resin-usage-settings.md)（第 3 版，已实施，待界面与游戏内验收）
- [基础设施统一：WebView2、网络与下载、缓存、临时文件](infrastructure-foundation.md)（第 2 版，已确认，待实施）
- [JS 脚本自定义 HTML 设置页](js-script-html-settings.md)（第 3 版，提案，待确认）
- [悬浮小窗（攻略小窗）](guide-window.md)（第 2 版，已确认，实施中；首版代码已完成，待游戏内验证）
