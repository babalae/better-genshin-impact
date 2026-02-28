# GearTaskExecutor 使用指南

## 概述

`GearTaskExecutor` 是一个强大的任务执行控制器，能够从 JSON 文件中解析并执行继承自 `BaseGearTask` 的不同类型任务。这些任务统一使用 `GearTaskData` 对象进行序列化。

## 核心组件

### 1. GearTaskExecutor
主要的任务执行控制器，提供以下功能：
- 从 JSON 文件加载任务定义
- 执行任务并跟踪进度
- 异常处理和日志记录
- 任务状态管理

### 2. GearTaskFactory
任务工厂，根据任务类型创建对应的任务实例：
- JavaScript 任务
- Pathing 任务
- C# 反射任务
- KeyMouse 任务
- Shell 任务

### 3. GearTaskConverter
任务转换器，将 `GearTaskData` 转换为可执行的 `BaseGearTask`：
- 递归处理子任务
- 参数验证
- 错误处理

### 4. GearTaskExecutionManager
任务执行管理器，提供详细的执行状态跟踪：
- 实时进度更新
- 任务状态监控
- 执行统计信息
- 事件通知

## 服务注册

在 `App.xaml.cs` 中注册服务：

```csharp
// 方式1：使用扩展方法注册所有服务
services.AddGearTaskServices();

// 方式2：带配置选项注册
services.AddGearTaskServices(options =>
{
    options.EnableVerboseLogging = true;
    options.ContinueOnTaskFailure = false;
    options.MaxConcurrentTasks = 1;
});

// 方式3：手动注册（如果需要自定义）
services.AddSingleton<GearTaskStorageService>();
services.AddSingleton<GearTaskFactory>();
services.AddSingleton<GearTaskConverter>();
services.AddTransient<GearTaskExecutionManager>();
services.AddTransient<GearTaskExecutor>();
```

## 基本使用

### 1. 执行任务定义

```csharp
public class TaskExecutionViewModel : ObservableObject
{
    private readonly GearTaskExecutor _taskExecutor;
    
    public TaskExecutionViewModel(GearTaskExecutor taskExecutor)
    {
        _taskExecutor = taskExecutor;
    }
    
    [RelayCommand]
    private async Task ExecuteTaskDefinitionAsync(string taskDefinitionName)
    {
        try
        {
            await _taskExecutor.ExecuteTaskDefinitionAsync(taskDefinitionName);
        }
        catch (Exception ex)
        {
            // 处理异常
            Debug.WriteLine($"任务执行失败: {ex.Message}");
        }
    }
}
```

### 2. 直接执行任务数据

```csharp
[RelayCommand]
private async Task ExecuteTaskDataAsync()
{
    var taskData = new GearTaskData
    {
        Name = "测试任务",
        Type = "javascript",
        IsEnabled = true,
        Parameters = new JavascriptGearTaskParams
        {
            FolderName = "test-script",
            Context = new { message = "Hello World" }
        }
    };
    
    try
    {
        await _taskExecutor.ExecuteTaskDataAsync(taskData);
    }
    catch (Exception ex)
    {
        // 处理异常
    }
}
```

### 3. 监控执行进度

```csharp
public class TaskMonitorViewModel : ObservableObject
{
    private readonly GearTaskExecutor _taskExecutor;
    
    [ObservableProperty]
    private double _progress;
    
    [ObservableProperty]
    private string _currentTaskName = string.Empty;
    
    [ObservableProperty]
    private string _statusMessage = string.Empty;
    
    public TaskMonitorViewModel(GearTaskExecutor taskExecutor)
    {
        _taskExecutor = taskExecutor;
        
        // 绑定属性
        _taskExecutor.PropertyChanged += OnTaskExecutorPropertyChanged;
    }
    
    private void OnTaskExecutorPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(GearTaskExecutor.Progress):
                Progress = _taskExecutor.Progress;
                break;
            case nameof(GearTaskExecutor.CurrentTaskName):
                CurrentTaskName = _taskExecutor.CurrentTaskName;
                break;
            case nameof(GearTaskExecutor.StatusMessage):
                StatusMessage = _taskExecutor.StatusMessage;
                break;
        }
    }
    
    [RelayCommand]
    private void StopExecution()
    {
        _taskExecutor.StopExecution();
    }
}
```

### 4. 获取执行统计信息

```csharp
[RelayCommand]
private void ShowStatistics()
{
    var stats = _taskExecutor.GetExecutionStatistics();
    
    var message = $@"执行统计信息：
总任务数: {stats.TotalTasks}
已完成: {stats.CompletedTasks}
失败: {stats.FailedTasks}
跳过: {stats.SkippedTasks}
成功率: {stats.SuccessRate:F1}%
整体进度: {stats.OverallProgress:F1}%";
    
    MessageBox.Show(message);
}
```

## 支持的任务类型

### 1. JavaScript 任务
```json
{
    "name": "JavaScript 脚本任务",
    "type": "javascript",
    "isEnabled": true,
    "parameters": {
        "folderName": "my-script",
        "context": {
            "param1": "value1",
            "param2": 123
        }
    }
}
```

### 2. Pathing 任务
```json
{
    "name": "路径任务",
    "type": "pathing",
    "isEnabled": true,
    "parameters": {
        "path": "path/to/route.json",
        "pathingPartyConfig": {
            "team": ["character1", "character2"]
        }
    }
}
```

### 3. C# 反射任务
```json
{
    "name": "C# 反射任务",
    "type": "csharp",
    "isEnabled": true,
    "parameters": {
        "methodPath": "MyNamespace.MyClass.MyMethod",
        "parametersJson": "[\"param1\", 123, {\"key\": \"value\"}]"
    }
}
```

### 4. KeyMouse 任务
```json
{
    "name": "键鼠录制任务",
    "type": "keymouse",
    "isEnabled": true,
    "parameters": "path/to/macro.json"
}
```

### 5. Shell 任务
```json
{
    "name": "Shell 命令任务",
    "type": "shell",
    "isEnabled": true,
    "parameters": {
        "command": "echo Hello World",
        "workingDirectory": "C:\\temp",
        "timeout": 30000
    }
}
```

## 错误处理

### 1. 任务验证
```csharp
public async Task<bool> ValidateTaskAsync(GearTaskData taskData)
{
    var converter = serviceProvider.GetRequiredService<GearTaskConverter>();
    var result = converter.ValidateTaskData(taskData);
    
    if (!result.IsValid)
    {
        foreach (var error in result.Errors)
        {
            Debug.WriteLine($"验证错误: {error}");
        }
        return false;
    }
    
    foreach (var warning in result.Warnings)
    {
        Debug.WriteLine($"验证警告: {warning}");
    }
    
    return true;
}
```

### 2. 异常处理
```csharp
try
{
    await _taskExecutor.ExecuteTaskDefinitionAsync(taskName);
}
catch (ArgumentException ex)
{
    // 参数错误
    MessageBox.Show($"参数错误: {ex.Message}");
}
catch (FileNotFoundException ex)
{
    // 文件未找到
    MessageBox.Show($"文件未找到: {ex.Message}");
}
catch (OperationCanceledException)
{
    // 用户取消
    MessageBox.Show("任务执行已取消");
}
catch (Exception ex)
{
    // 其他异常
    MessageBox.Show($"执行失败: {ex.Message}");
}
```

## 最佳实践

1. **使用依赖注入**：通过构造函数注入 `GearTaskExecutor`
2. **异步执行**：所有任务执行都应该使用 `async/await`
3. **异常处理**：始终包装任务执行在 try-catch 块中
4. **进度监控**：绑定执行器的属性来显示进度
5. **资源清理**：在适当的时候取消任务执行
6. **日志记录**：启用详细日志记录来调试问题
7. **参数验证**：在执行前验证任务数据的完整性

## 注意事项

- 任务执行器是线程安全的，但同时只能执行一个任务
- 子任务会按顺序执行，不支持并行执行
- 禁用的任务会被跳过，但仍会处理其子任务
- 任务失败会停止整个执行流程，除非配置为继续执行
- 所有任务都支持取消操作