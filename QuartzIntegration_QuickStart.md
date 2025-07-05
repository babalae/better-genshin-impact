# Quartz.NET 集成 - 快速入门指南

## 🚀 快速开始

### 1. 基本设置
Quartz.NET 已经集成到项目中，无需额外配置。应用启动时会自动初始化调度服务。

### 2. 在界面中使用

#### 添加定时任务
1. 选择一个脚本组
2. 使用新增的"添加定时任务"功能
3. 选择预设时间或输入自定义 Cron 表达式

#### 管理定时任务
- **查看任务**: 查看所有已配置的定时任务
- **删除任务**: 删除选中脚本组的所有定时任务
- **任务报告**: 查看任务统计和即将执行的任务
- **批量添加**: 为所有启用的脚本组批量添加定时任务

### 3. 编程方式使用

```csharp
// 获取服务实例
var schedulerManager = App.GetService<SchedulerManager>();
var dynamicTaskService = App.GetService<DynamicTaskExampleService>();

// 添加每日任务（每天上午8:30执行）
await dynamicTaskService.CreateDailyTaskAsync(scriptGroup, 8, 30);

// 添加每周任务（每周一上午9:00执行）
await dynamicTaskService.CreateWeeklyTaskAsync(scriptGroup, 1, 9, 0);

// 添加自定义 Cron 任务（每2小时执行一次）
await schedulerManager.AddScheduledTaskAsync(scriptGroup, "0 0 0/2 * * ? *");

// 查看所有任务
var tasks = await schedulerManager.GetAllScheduledTasksAsync();
```

## 📅 常用 Cron 表达式

| 描述 | Cron 表达式 | 说明 |
|------|-------------|------|
| 每天午夜 | `0 0 0 * * ? *` | 每天 00:00:00 执行 |
| 每天上午8点 | `0 0 8 * * ? *` | 每天 08:00:00 执行 |
| 工作日上午9点 | `0 0 9 ? * MON-FRI *` | 周一到周五 09:00:00 执行 |
| 每小时执行 | `0 0 * * * ? *` | 每小时的整点执行 |
| 每30分钟执行 | `0 0/30 * * * ? *` | 每30分钟执行一次 |
| 每周一执行 | `0 0 0 ? * MON *` | 每周一 00:00:00 执行 |
| 每月1号执行 | `0 0 0 1 * ? *` | 每月1号 00:00:00 执行 |

## 🛠️ 使用 CronExpressionHelper

```csharp
// 生成常用表达式
var dailyExpression = CronExpressionHelper.CreateDaily(8, 30); // 每天8:30
var weeklyExpression = CronExpressionHelper.CreateWeekly(DayOfWeek.Monday, 9, 0); // 每周一9:00
var workdayExpression = CronExpressionHelper.CreateWorkdays(9, 0); // 工作日9:00

// 解析表达式为人类可读描述
var description = CronExpressionHelper.ParseToDescription("0 0 8 * * ? *");
// 输出: "自定义时间: 8点"

// 验证表达式
bool isValid = CronExpressionHelper.IsValidCronExpression("0 0 8 * * ? *");

// 获取下次执行时间
var nextTime = CronExpressionHelper.GetNextExecutionTime("0 0 8 * * ? *");
```

## 🎯 实际应用场景

### 场景1：每日自动任务
```csharp
// 每天早上8点执行日常任务
await dynamicTaskService.CreateDailyTaskAsync(dailyScriptGroup, 8, 0);
```

### 场景2：工作日任务
```csharp
// 工作日上午9点执行
var cronExpression = CronExpressionHelper.CreateWorkdays(9, 0);
await schedulerManager.AddScheduledTaskAsync(scriptGroup, cronExpression);
```

### 场景3：定期清理任务
```csharp
// 每周日凌晨2点执行清理任务
await dynamicTaskService.CreateWeeklyTaskAsync(cleanupScriptGroup, DayOfWeek.Sunday, 2, 0);
```

### 场景4：批量管理
```csharp
// 为所有脚本组添加定时任务
var allScriptGroups = GetAllScriptGroups();
int successCount = await dynamicTaskService.AddMultipleScriptGroupSchedulesAsync(allScriptGroups);
```

## 🔧 高级功能

### 任务状态管理
```csharp
// 暂停任务
await schedulerManager.PauseScheduledTaskAsync("TaskName");

// 恢复任务
await schedulerManager.ResumeScheduledTaskAsync("TaskName");

// 更新任务时间
await schedulerManager.UpdateScheduledTaskAsync("TaskName", "0 0 10 * * ? *");
```

### 任务监控
```csharp
// 获取任务报告
var report = await dynamicTaskService.GetScheduledTaskReportAsync();
Console.WriteLine($"总任务数: {report.TotalTasks}");
Console.WriteLine($"活跃任务数: {report.ActiveTasks}");

// 查看即将执行的任务
foreach (var execution in report.NextExecutions)
{
    Console.WriteLine($"{execution.ScriptGroupName} 将在 {execution.NextFireTime} 执行");
}
```

## ⚠️ 注意事项

1. **任务持久化**: 当前使用内存存储，应用重启后需要重新添加任务
2. **并发控制**: 默认最大并发数为10个任务
3. **错误处理**: 任务执行失败不会影响其他任务
4. **性能考虑**: 避免设置过于频繁的任务（如每秒执行）
5. **脚本兼容性**: 确保脚本组中的项目状态为"启用"

## 🔍 故障排除

### 问题：任务不执行
- 检查 Cron 表达式格式是否正确
- 确认脚本组包含启用的项目
- 查看日志文件确认任务是否正确调度

### 问题：服务未初始化
- 确保在 `App.xaml.cs` 中正确注册了 Quartz.NET 服务
- 检查依赖注入容器是否正常工作

### 问题：界面功能不可用
- 确认 ViewModel 中正确获取了服务实例
- 检查是否选择了有效的脚本组

## 📚 更多信息

- 查看 `/Service/Quartz/README.md` 获取详细文档
- 运行 `/Examples/QuartzExampleProgram.cs` 查看完整示例
- 参考 Quartz.NET 官方文档了解更多高级功能

---

**提示**: 这个集成保持了完全的向后兼容性，现有的手动执行功能不受影响。你可以同时使用手动执行和定时执行功能。