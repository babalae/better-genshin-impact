# 项目约定

- 使用 Windows 环境，使用简体中文沟通。
- C# 文件必须使用 UTF-8 无 BOM 编码，避免乱码。
- 保留已有注释，可修改或补充，不要随意删除。
- 在无冲突情况下请不要对类使用全路径

## 设计原则

- 设计时先明确各层职责、边界和依赖方向，保持分层清晰，避免跨层耦合。
- 核心代码保持简洁易懂，直接表达业务意图，避免不必要的抽象和复杂度。
- 首次实现聚焦主流程和已明确的需求，不为细微边界问题过度设计；暂不处理的场景可用注释记录。
- UI 层可为交互体验承载必要的复杂度，同时遵守 MVVM 和分层边界；整体 UI/UX 应直观易用，符合用户操作直觉。

## 代码规范

- 新增类、属性、字段和方法必须有注释；核心逻辑、执行步骤及逻辑分块也需说明。
- 重要 C# 文件原则上只放一个类；枚举或用途单一的小类可合并存放。
- 可为 null 的类型使用 `?` 标注。
- 私有字段使用 `_camelCase`，公共属性使用 `PascalCase`。
- 禁止新增 `BetterGenshinImpact/Assets` 目录。资源由 `BetterGI.Assets.*` 外部包提供，新增资源需人工修改对应 NuGet 包。

## 框架与 MVVM

主要依赖：WPF-UI 4.0.2、gong-wpf-dragdrop 3.2.1、CommunityToolkit.Mvvm 8.2.2、Microsoft.Xaml.Behaviors.Wpf 1.1.122。

- ViewModel 必须直接或间接继承 `ObservableObject`；项目的 `ViewModel` 基类已满足此要求。
- 使用 `[ObservableProperty]` 生成属性，使用 `[RelayCommand]` 生成命令；异步命令返回 `Task`。
- View 通过构造函数注入 ViewModel，沿用所在页面的 `DataContext` 约定；XAML 绑定路径须与其一致，避免复杂逻辑。
- XAML 绑定只读属性、计算属性或纯展示文本时，必须显式指定 `Mode=OneWay`，不要依赖目标依赖属性的默认绑定模式。尤其 `Run.Text` 默认双向绑定，绑定只读属性会在加载 XAML 时抛出异常。示例：`<Run Text="{Binding TriggerDirectory, Mode=OneWay}" />`。
- 只有需要将用户输入回写到可写属性的绑定才使用 `Mode=TwoWay`；新增或修改含 `Run.Text` 的视图时，必须检查所有展示用绑定的方向。
- 子视图构造时尚未加入父控件资源树，`StaticResource` 必须能在本视图或明确合并的字典中解析；禁止依赖稍后才挂接的父视图资源。新增类别模板须验证实际实例化及布局，不能只以编译通过作为 XAML 可用的依据。
- 控件局部样式应通过 `BasedOn` 继承对应 WPF-UI 默认样式；承载类别视图的 `ContentControl` 须明确内容拉伸，避免覆盖主题状态或出现内容挤缩。
- 交互优先使用 Behaviors，避免在代码后置中编写不符合 MVVM 的交互逻辑。
- 在 `App.xaml.cs` 中注册依赖：页面使用 `services.AddView<ExamplePage, ExampleViewModel>()`，单例服务使用 `services.AddSingleton<IExampleService, ExampleService>()`。
- 新增窗口必须启用主题背景；简单对话框优先使用 `ThemedMessageBox`，不要使用 WPF 自带的 `MessageBox`。
- JSON 序列化优先使用 Newtonsoft.Json；模型若已由 System.Text.Json 序列化，则使用 System.Text.Json 反序列化。

## 识别与模拟操作

- 键鼠操作后再识图，必须适当延时：游戏响应通常有 30～60 ms 延迟，截图来源也有延迟。
- 键鼠操作之间也需合理间隔，避免在 30 fps 游戏中因操作过快而丢失输入。

## 编译验证

禁止直接运行主程序进行测试或截图（包括启动 exe、`dotnet run` 或通过 IDE 启动）。主程序要求管理员权限，harness 无法通过启动 exe 完成模拟测试和截图感知，因此不要将启动主程序作为验证步骤。

在仓库根目录执行以下命令，仅输出错误；退出码 0 表示成功，非 0 表示失败。编译通过即可，无需运行程序；若程序占用导致失败，停止验证并说明即可，无需继续尝试。

```powershell
dotnet msbuild BetterGenshinImpact.sln -restore -p:Configuration=Debug -nologo -tl:off -clp:ErrorsOnly
```

## 代码审查

仅在执行代码审查时，读取并遵循 [.agents/code-review.md](.agents/code-review.md)。
