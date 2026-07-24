本项目使用了 WPF-UI、 CommunityToolkit.Mvvm、Microsoft.Xaml.Behaviors.Wpf 来实现 MVVM 架构。在编写代码的时候请注意：

### 主要依赖框架
#### UI 框架
- **WPF-UI (4.0.2)** - 现代化 WPF UI 框架
- **gong-wpf-dragdrop(3.2.1)** - 拖拽框架

#### MVVM 框架
- **CommunityToolkit.Mvvm (8.2.2)** - 微软官方 MVVM 工具包
  - 所有 ViewModel 必须继承自 `ObservableObject`
  - 使用 `[ObservableProperty]` 特性自动生成属性
  - 使用 `[RelayCommand]` 特性自动生成命令
- **Microsoft.Xaml.Behaviors.Wpf(1.1.122)** - WPF 行为扩展库
  - 请尽量使用 Behaviors 库来实现交互，避免不符合 MVVM 规范的交互事件触发方式。

### 其他框架使用要求
1. 请优先使用 Newtonsoft.Json 作为json序列化工具，但是如果这个模型已经被System.Text.Json序列化过了，那么就直接使用System.Text.Json反序列化。
2. 所有简单的对话框弹出需求优先使用 ThemedMessageBox 弹出。而不是 WPF 自带的 MessageBox。

## MVVM 架构规则

### 基础架构

### ViewModel 编写规范

1. **继承规则**
   ```csharp
   public partial class ExampleViewModel : ViewModel
   {
       [ObservableProperty]
       private string _title = "";
       
       [RelayCommand]
       private void DoSomething()
       {
           // 实现逻辑
       }
   }
   ```

2. **属性命名**
   - 私有字段使用下划线前缀: `_fieldName`
   - 公共属性使用 PascalCase: `PropertyName`
   - 使用 `[ObservableProperty]` 自动生成属性

3. **命令实现**
   - 使用 `[RelayCommand]` 特性
   - 异步命令使用 `[RelayCommand]` + `async Task`

### View 编写规范

1. **代码后置**
   ```csharp
   public partial class ExamplePage : UserControl
   {
       public ExampleViewModel ViewModel { get; }
       
       public ExamplePage(ExampleViewModel viewModel)
       {
           ViewModel = viewModel;
           DataContext = this;
           InitializeComponent();
       }
   }
   ```

2. **XAML 绑定**
   - 使用 `{Binding}` 语法绑定 ViewModel 属性
   - 命令绑定: `Command="{Binding ExampleCommand}"`
   - 避免在 XAML 中编写复杂逻辑

### 依赖注入规范

1. **服务注册**
   ```csharp
   // 在 App.xaml.cs 中注册
   services.AddView<ExamplePage, ExampleViewModel>();
   services.AddSingleton<IExampleService, ExampleService>();
   ```

最后，程序能够编译就认为成功，无需实际运行程序。

编译指令参考，如果出现程序占用场景，直接放弃编译验证即可
```
dotnet build BetterGenshinImpact.sln -c Debug
```

## Code Review Rules

### Output language

- 必须使用简体中文输出代码审查结果。
- 问题标题、问题描述、影响分析和修复建议必须使用中文。
- 类名、方法名、变量名、文件路径、代码片段和错误消息保持原文。
- 专业术语第一次出现时可以采用“中文（English）”格式。

### Review priorities

- 优先检查可能造成崩溃、死锁、资源泄漏和数据竞争的问题。
- 重点检查异步任务、CancellationToken、线程切换和 STA UI 线程使用。
- 重点检查 Mat、Bitmap、Stream、CancellationTokenSource 等 IDisposable 对象是否正确释放。
- 检查游戏输入、截图和多实例场景是否存在共享状态冲突。
- 不要只提出风格问题，除非它会影响正确性、可维护性或性能。

### Finding format

每个问题按以下结构输出：

1. 严重程度
2. 问题位置
3. 问题原因
4. 可能造成的影响
5. 推荐修复方案
