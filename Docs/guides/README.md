# 使用指南

本目录存放面向**使用者**（而非贡献者）的操作指南：怎么把环境搭起来、怎么按步骤完成某项配置。

## 文档列表

- [单机双用户：用 RDM 登录第二个用户并运行跨用户 Worker](multi-user-setup.md)
  —— 从创建 Windows 用户、配置本地 RDP 多会话（RDPWrap + RDM/FreeRDP），
  到在第二个用户下运行 `--headless` Worker 并由控制端接管。

相关的设计文档与开发者文档：

- [多实例命名管道协议](../design/multi-instance-ipc.md)：管道命名、DACL、SID 校验、任务类型
- [开发者文档](../development/README.md)：编译、调试、发行包专属资源
