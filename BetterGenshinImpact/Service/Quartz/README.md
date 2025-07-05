# Quartz.NET 集成使用指南

## 概述

本项目已集成 Quartz.NET 调度框架，支持动态添加、删除和管理定时任务。现有的手动执行功能保持不变，新增的调度功能为脚本组提供自动化执行能力。

## 主要特性

1. **非破坏性集成**：现有功能完全保留
2. **动态任务管理**：运行时添加/删除定时任务
3. **Cron表达式支持**：完整的时间调度配置
4. **可视化管理**：通过UI界面管理定时任务
5. **任务监控**：查看任务状态和执行报告

## 核心组件

### 1. ScriptExecutionJob
负责实际执行脚本组的任务类，实现了 `IJob` 接口。

### 2. SchedulerManager
提供完整的调度管理功能：
- 动态添加任务
- 删除任务
- 更新任务
- 暂停/恢复任务
- 查询任务状态

### 3. DynamicTaskExampleService
演示如何使用调度功能的示例服务，包含各种实用方法。

### 4. QuartzHostedService
管理 Quartz.NET 调度器的生命周期。

## 使用示例

### 基本用法

```csharp
// 获取服务实例
var schedulerManager = App.GetService<SchedulerManager>();
var dynamicTaskService = App.GetService<DynamicTaskExampleService>();

// 添加每日执行的定时任务
var scriptGroup = GetYourScriptGroup();
bool success = await dynamicTaskService.CreateDailyTaskAsync(scriptGroup, 8, 30); // 每天8:30执行

// 添加每周执行的定时任务
bool success = await dynamicTaskService.CreateWeeklyTaskAsync(scriptGroup, 1, 9, 0); // 每周一9:00执行

// 添加间隔执行的定时任务
bool success = await dynamicTaskService.CreateIntervalTaskAsync(scriptGroup, 30); // 每30分钟执行
```

### 自定义 Cron 表达式

```csharp
// 直接使用 Cron 表达式
var cronExpression = "0 0 8,12,18 * * ? *"; // 每天8点、12点、18点执行
bool success = await schedulerManager.AddScheduledTaskAsync(scriptGroup, cronExpression);

// 使用预定义的调度配置转换
var schedule = "Daily"; // 或其他预定义值
var cronExpression = SchedulerManager.ConvertScheduleToCron(schedule);
bool success = await schedulerManager.AddScheduledTaskAsync(scriptGroup, cronExpression);
```

### 任务管理

```csharp
// 查看所有定时任务
var tasks = await schedulerManager.GetAllScheduledTasksAsync();

// 删除特定任务
bool success = await schedulerManager.RemoveScheduledTaskAsync("TaskName");

// 暂停任务
bool success = await schedulerManager.PauseScheduledTaskAsync("TaskName");

// 恢复任务
bool success = await schedulerManager.ResumeScheduledTaskAsync("TaskName");

// 更新任务的Cron表达式
bool success = await schedulerManager.UpdateScheduledTaskAsync("TaskName", "0 0 10 * * ? *");
```

### 批量操作

```csharp
// 批量添加多个脚本组的定时任务
var scriptGroups = GetAllScriptGroups();
int successCount = await dynamicTaskService.AddMultipleScriptGroupSchedulesAsync(scriptGroups);

// 删除指定脚本组的所有任务
int removedCount = await dynamicTaskService.RemoveScriptGroupSchedulesAsync("ScriptGroupName");

// 清理所有定时任务
int clearedCount = await dynamicTaskService.ClearAllScheduledTasksAsync();
```

### 任务报告

```csharp
// 获取定时任务报告
var report = await dynamicTaskService.GetScheduledTaskReportAsync();
Console.WriteLine($"总任务数: {report.TotalTasks}");
Console.WriteLine($"活跃任务数: {report.ActiveTasks}");

// 查看即将执行的任务
foreach (var execution in report.NextExecutions)
{
    Console.WriteLine($"{execution.ScriptGroupName} 将在 {execution.NextFireTime} 执行");
}
```

## UI 集成

在 `ScriptControlViewModel` 中新增了以下命令，可以在界面中使用：

1. **OnAddScheduledTaskAsync** - 添加定时任务
2. **OnViewScheduledTasksAsync** - 查看所有定时任务
3. **OnRemoveScheduledTasksAsync** - 删除定时任务
4. **OnViewScheduledTaskReportAsync** - 查看任务报告
5. **OnBatchAddScheduledTasksAsync** - 批量添加任务

## Cron 表达式说明

| 表达式 | 含义 |
|--------|------|
| `0 0 0 * * ? *` | 每天午夜执行 |
| `0 0 8 * * ? *` | 每天上午8点执行 |
| `0 30 9 ? * MON-FRI *` | 工作日上午9:30执行 |
| `0 0 0 ? * MON *` | 每周一午夜执行 |
| `0 0 0 1 * ? *` | 每月1号午夜执行 |
| `0 0/30 * * * ? *` | 每30分钟执行 |
| `0 0 8,12,18 * * ? *` | 每天8点、12点、18点执行 |

## 预定义调度配置转换

现有的调度配置会自动转换为相应的 Cron 表达式：

- `Daily` → `0 0 0 * * ? *` (每天午夜)
- `EveryTwoDays` → `0 0 0 1/2 * ? *` (每两天)
- `Monday` → `0 0 0 ? * MON *` (每周一)
- `Tuesday` → `0 0 0 ? * TUE *` (每周二)
- 其他自定义值被视为 Cron 表达式直接使用

## 注意事项

1. **任务执行环境**：定时任务在后台线程中执行，请确保脚本兼容性
2. **错误处理**：任务执行失败不会影响其他任务的调度
3. **性能考虑**：避免添加过于频繁的定时任务
4. **数据持久化**：当前使用内存存储，应用重启后需要重新添加任务
5. **并发控制**：默认最大并发数为10，可在配置中调整

## 扩展开发

### 自定义任务类型

```csharp
// 创建自定义任务
public class CustomScriptJob : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        // 自定义任务逻辑
    }
}

// 注册自定义任务
services.AddQuartz(q =>
{
    q.UseInMemoryStore();
    // 添加自定义任务
    q.AddJob<CustomScriptJob>(opts => opts.WithIdentity("CustomJob"));
});
```

### 数据持久化

如需任务数据持久化，可配置数据库存储：

```csharp
services.AddQuartz(q =>
{
    // 使用数据库存储替代内存存储
    q.UsePersistentStore(s =>
    {
        s.UseProperties = true;
        s.UseSqlServer("ConnectionString");
        s.UseJsonSerializer();
    });
});
```

## 故障排除

### 常见问题

1. **服务未初始化**：确保在 `App.xaml.cs` 中正确注册了 Quartz.NET 服务
2. **任务不执行**：检查 Cron 表达式格式是否正确
3. **脚本组数据错误**：确保脚本组包含有效的项目且状态为启用
4. **并发冲突**：避免同时运行多个相同的脚本组任务

### 日志监控

所有任务执行都会记录详细日志，可通过日志文件查看：
- 任务开始执行
- 任务执行完成
- 任务执行异常
- 调度器状态变化

## 总结

Quartz.NET 集成为 Better Genshin Impact 提供了强大的定时任务功能，通过简洁的 API 和友好的 UI 界面，用户可以轻松实现脚本组的自动化调度执行。该集成保持了原有功能的完整性，同时扩展了应用的自动化能力。