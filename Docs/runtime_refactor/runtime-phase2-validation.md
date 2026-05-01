# 第二阶段运行时验证清单

本文用于验证“截图器重启不再打断运行器”这条第二阶段目标是否真正成立。它分为三部分：

1. 自动化单元测试
2. 手动运行时回归矩阵

## 自动化测试

测试工程：Test/BetterGenshinImpact.UnitTest

当前已落地的自动化用例：

1. CaptureService 启动与内部重启后会递增 CaptureVersion，并复用已保存的启动上下文
2. CaptureService 在连续失败期间只发出一次 CaptureUnavailable，恢复成功后发出 CaptureRecovered
3. CaptureService 在重试窗口内可以通过内部重启恢复取帧
4. CaptureService 在连续失败超过 5 秒后发出 PermanentFailure
5. CaptureService 停止后会清空启动上下文，后续 Restart 不再继续工作

建议命令：

```powershell
dotnet test .\Test\BetterGenshinImpact.UnitTest\BetterGenshinImpact.UnitTest.csproj --filter CaptureServiceTests
```

## 手动回归矩阵

| 场景 | 类型 | 预期结果 |
| --- | --- | --- |
| 正常启动截图器并进入触发器循环 | 手动 | 触发器、遮罩、截图和画中画都正常工作 |
| 运行中调整游戏窗口大小 | 手动 | 截图器内部重启，运行器不被 Stop，不触发全局 Cancel |
| 截图器短暂返回空帧 | 手动 | 当前帧被跳过，运行器继续存活，恢复后继续取帧 |
| 长时间无法恢复截图 | 手动 | CaptureService 进入最终失败路径，出现日志/通知，但不是直接走 UI Stop/Start |
| 画中画窗口在截图恢复窗口期运行 | 手动 | 可接受地短暂空白，不抛异常，不导致运行器停止 |
| 通知截图在截图恢复窗口期运行 | 手动 | 可接受地缺少截图，但通知链路不抛异常 |
| 游戏进程退出 | 手动 | 仍然允许通过 UiTaskStopTickEvent 结束完整运行时会话 |

## 执行顺序建议

1. 先跑 CaptureService 单元测试，确认第二阶段核心契约没有被回归
2. 再做“窗口缩放”和“空帧恢复”两个高风险手动场景
3. 最后验证画中画、通知截图和游戏退出这些旁路场景

## 通过标准

第二阶段运行时验证通过，需要同时满足以下条件：

1. CaptureService 单元测试全绿
2. 窗口尺寸变化不再触发 UI Stop/Start 重启链路
3. 截图器短时失效不会取消当前运行任务
4. 游戏进程退出仍然会按旧语义结束完整运行时会话
5. 画中画和通知截图在恢复窗口期没有未处理异常