# 尘歌壶修复离线回归

在 Windows、.NET 8 SDK 和 Windows PowerShell 环境运行，无需启动 BGI 或原神：

```powershell
dotnet test Test/SereniteaPot.Regression/SereniteaPot.Regression.csproj
```

项目链接生产辅助类，并在构建时提取当前秘境、尘歌壶入口和快捷键方法体。窗口、输入、OCR 和奖励操作由测试桩替代，不需要向主程序添加测试入口、界面或日志接收器。

覆盖局部战斗正常结束与父任务取消、两种进壶入口的名称检查、暂停计时与状态归还、持键清理、前台门禁、背包滚动边界和诊断截图保留限制。离线验证不能替代真实图像识别、传送和领奖验收。

在仓库根目录检查共享代码和界面范围：

```powershell
powershell -File Test/SereniteaPot.Regression/Verify-Scope.ps1 -BaseRef f29966868c6e2d5b8798bb6a4f3df201ec4a5f95
```

基线取本分支已合入的上游提交。公共等待、秘境、任务执行器、滚动、一条龙界面、应用日志配置及依赖版本必须保持该基线内容，仅允许普通 `TpTask` 增加 `partial` 声明。合入更新的上游后，应改用对应提交运行检查，避免把上游自己的改动误判为本修复引入的变化。
