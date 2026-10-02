# Pulonia 步骤 3：无游戏运行链路验收

[返回开发计划](../../development-plan.md) · [执行设计](../../execution.md)

步骤 3 只注册 `shell` 与 `csharp` 两种不需要游戏资源的执行器。打开主菜单“任务计划”，在树中新增对应节点，保存后切换到“运行”页签即可提交。运行状态只保留在当前进程；步骤 6 才持久化历史和续跑信息。

## 纯计算 C# 样例

新增“进程内 C#”节点。空参数会使用注册能力的默认值，也可以在节点参数中填写：

```json
{
  "operation": "sample.sum",
  "values": [1, 2, 3.5],
  "delay_milliseconds": 3000
}
```

运行结果应为 `6.5`。在 3 秒内修改节点参数或计划名称，本次运行的“提交时快照”仍保持旧值；下一次提交才使用新值。把延时改为 `30000` 后运行并点击“取消选中运行”，状态应先进入“停止中”，待委托响应取消后变为“已取消”。该样例只使用 `Task.Delay` 和内存计算，不会解析或启动游戏窗口，也不会启动截图器。

## Shell 样例

新增“Shell”节点并填写：

```json
{
  "file_name": "powershell.exe",
  "arguments": [
    "-NoProfile",
    "-Command",
    "Write-Output 'Pulonia shell started'; Start-Sleep -Seconds 30; Write-Output 'Pulonia shell finished'"
  ]
}
```

运行后取消。执行器会结束本次创建的整个子进程树、继续排空标准输出和标准错误，并在确认进程退出后才把请求标为“已取消”及运行队列中的下一请求放行。正常执行时，退出码、有限截取的两路输出及截断标记保存在节点结构化结果中。

## 串行与超时

连续提交两个带延时的请求，第二个请求在第一个请求确认结束前应保持“排队中”。“计划总时限”控制整次运行；节点“执行策略”中的 `timeout_seconds` 控制单次尝试。节点超时、显式取消和总时限取消都使用各自的关联令牌，执行器退出前不会启动下一个请求。

进程内 C# 调用可注入 `IPuloniaTaskService`，调用 `EnqueueAsync`、`GetRunAsync`、`WaitForCompletionAsync` 和 `CancelAsync`；提交方法的 `CancellationToken` 只控制提交/准备等待，运行取消必须使用显式 `CancelAsync`。
