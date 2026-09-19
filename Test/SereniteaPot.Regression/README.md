# 尘歌壶修复离线回归

在 Windows、.NET 8 SDK 和 Windows PowerShell 环境运行，无需启动 BGI 或原神：

```powershell
dotnet test Test/SereniteaPot.Regression/SereniteaPot.Regression.csproj
```

项目链接生产辅助类，并在构建时提取当前秘境、尘歌壶入口和快捷键方法体。窗口、输入、OCR 和奖励操作由测试桩替代，不需要向主程序添加测试入口、界面或日志接收器。

覆盖局部战斗正常结束与父任务取消、两种进壶入口的名称检查、暂停计时与状态归还、持键清理、前台门禁、背包滚动边界和诊断截图保留限制。离线验证不能替代真实图像识别、传送和领奖验收。

在仓库根目录检查共享代码和界面范围：

```powershell
powershell -File Test/SereniteaPot.Regression/Verify-Scope.ps1 -BaseRef 42e1c0e745670eb4443c1e0357fba963eb24dfcd
powershell -File Test/SereniteaPot.Regression/Verify-Scope.ps1 -BaseRef f29b0828ab4f8f91d9087152dcb487719e80fa16
```

公共等待、秘境、任务执行器、滚动、一条龙界面及应用日志配置必须保持基线内容。仅允许普通 `TpTask` 增加 `partial` 声明，以及 `BetterGI.Assets.Other` 从共同祖先的 `1.0.25` 对齐当前上游的 `1.0.27`；其他依赖变更仍会失败。
