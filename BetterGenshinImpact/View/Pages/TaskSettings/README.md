# TaskSettings 模块化架构文档

## 概述

TaskSettings 是一个模块化的任务设置页面，采用了 UserControl 组件化的方式，将原本单一的大型 XAML 文件拆分为多个独立的子模块。这种架构提高了代码的可维护性、可读性和可扩展性，使开发者能够更轻松地修改现有功能或添加新功能。

## 目录结构

```
BetterGenshinImpact/View/Pages/
├── TaskSettingsPage.xaml (主页面)
├── TaskSettingsPage.xaml.cs
└── TaskSettings/ (子模块目录)
    ├── AutoGeniusInvocationTaskControl.xaml (自动七圣召唤)
    ├── AutoGeniusInvocationTaskControl.xaml.cs
    ├── AutoWoodTaskControl.xaml (自动伐木)
    ├── AutoWoodTaskControl.xaml.cs
    ├── AutoFightTaskControl.xaml (自动战斗)
    ├── AutoFightTaskControl.xaml.cs
    └── ... (其他模块文件)
```

## 架构设计

### 主页面 (TaskSettingsPage.xaml)

主页面作为容器，负责组织和显示所有任务模块。它具有以下特点：

1. 简洁的结构，主要包含页面标题和子模块引用
2. 通过命名空间引用子模块：`xmlns:taskControls="clr-namespace:BetterGenshinImpact.View.Pages.TaskSettings"`
3. 使用 StackPanel 作为布局容器，按顺序排列各个任务模块
4. 所有子模块共享主页面的 DataContext：`DataContext="{Binding}"`

### 子模块 (TaskControl)

每个子模块都是一个独立的 UserControl，遵循统一的结构模式：

1. 基本结构为 CardExpander 包装的内容面板
2. 包含模块标题、图标、控制按钮和设置选项
3. 通过数据绑定与 TaskSettingsPageViewModel 交互
4. 独立的 XAML 和 CS 文件，便于单独维护

### 数据绑定

所有子模块共享主页面的 TaskSettingsPageViewModel，通过以下方式实现数据绑定：

1. 在主页面中设置子模块的 DataContext：`DataContext="{Binding}"`
2. 子模块直接绑定到 ViewModel 中的属性和命令
3. 配置数据通过 `Config.*` 路径访问
4. 命令通过直接绑定访问，如 `Command="{Binding StopSoloTaskCommand}"`

## 模块类型

当前实现的模块包括：

### 活跃模块（当前可用）

1. **AutoGeniusInvocationTaskControl** - 自动七圣召唤
2. **AutoWoodTaskControl** - 自动伐木
3. **AutoFightTaskControl** - 自动战斗
4. **AutoDomainTaskControl** - 自动秘境
5. **AutoStygianOnslaughtTaskControl** - 自动幽境危战
6. **AutoMusicGameTaskControl** - 自动千音雅集
7. **AutoAlbumTaskControl** - 自动专辑
8. **AutoFishingTaskControl** - 自动钓鱼
9. **AutoRedeemCodeTaskControl** - 自动兑换码
10. **AutoArtifactSalvageTaskControl** - 自动分解圣遗物
11. **GetGridIconsTaskControl** - 截取物品图标（开发者工具）

### 预留模块（调试或注释状态）

12. **AutoTrackTaskControl** - 自动跟踪（调试模式可见）
13. **AutoMoveTaskControl** - 自动前进（注释状态）
14. **StopTaskControl** - 停止任务（注释状态）

## 添加新模块的标准流程

如果需要添加新的任务模块，请按照以下步骤操作：

### 1. 创建模块文件

在 `BetterGenshinImpact/View/Pages/TaskSettings/` 目录下创建新的 XAML 和 CS 文件：

```
AutoNewFeatureTaskControl.xaml
AutoNewFeatureTaskControl.xaml.cs
```

### 2. 实现 UserControl

在 XAML 文件中实现 UserControl，遵循现有模块的结构模式：

```xml
<UserControl x:Class="BetterGenshinImpact.View.Pages.TaskSettings.AutoNewFeatureTaskControl"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:b="http://schemas.microsoft.com/xaml/behaviors"
             xmlns:controls="clr-namespace:BetterGenshinImpact.View.Controls"
             xmlns:local="clr-namespace:BetterGenshinImpact.Markup"
             xmlns:ui="http://schemas.lepo.co/wpfui/2022/xaml"
             xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
             xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
             mc:Ignorable="d">

    <ui:CardExpander Margin="0,0,0,12" ContentPadding="0">
        <ui:CardExpander.Icon>
            <!-- 模块图标 -->
            <ui:FontIcon Glyph="&#xf6d2;" Style="{StaticResource FaFontIconStyle}" />
        </ui:CardExpander.Icon>
        <ui:CardExpander.Header>
            <Grid>
                <!-- 标题和控制按钮 -->
                <ui:TextBlock Text="{local:Localize Key=task.newFeature}" />
                <controls:TwoStateButton EnableCommand="{Binding SwitchNewFeatureCommand}"
                                         DisableCommand="{Binding StopSoloTaskCommand}" />
            </Grid>
        </ui:CardExpander.Header>
        <!-- 模块内容 -->
        <StackPanel>
            <!-- 设置选项 -->
        </StackPanel>
    </ui:CardExpander>
</UserControl>
```

### 3. 实现代码后端

在 CS 文件中实现简单的代码后端：

```csharp
using System.Windows.Controls;

namespace BetterGenshinImpact.View.Pages.TaskSettings
{
    public partial class AutoNewFeatureTaskControl : UserControl
    {
        public AutoNewFeatureTaskControl()
        {
            InitializeComponent();
        }
    }
}
```

### 4. 更新 ViewModel

在 `TaskSettingsPageViewModel.cs` 中添加必要的属性和命令：

```csharp
[ObservableProperty]
private bool _switchNewFeatureEnabled;

[ObservableProperty]
private string _switchNewFeatureButtonText = App.GetService<ILocalizationService>().GetString("common.start");

[RelayCommand]
private async Task OnSwitchNewFeature()
{
    SwitchNewFeatureEnabled = true;
    await new TaskRunner()
        .RunSoloTaskAsync(new NewFeatureTask(new NewFeatureParam()));
    SwitchNewFeatureEnabled = false;
}
```

### 5. 添加配置（如需要）

在 `AllConfig` 中添加新功能的配置类：

```csharp
public class NewFeatureConfig
{
    public bool Enabled { get; set; } = false;
    public int SomeParameter { get; set; } = 100;
    // 其他配置项...
}
```

### 6. 在主页面中引用新模块

在 `TaskSettingsPage.xaml` 中添加新模块的引用：

```xml
<!-- NewFeature Task Module -->
<taskControls:AutoNewFeatureTaskControl DataContext="{Binding}" />
```

### 7. 添加本地化支持

在语言资源文件中添加必要的本地化键值：

```
"task.newFeature": "新功能名称",
"task.newFeatureDescription": "新功能的描述文本"
```

## 最佳实践

### 模块设计

1. **保持一致性**：遵循现有模块的结构和样式，确保用户体验的一致性
2. **关注点分离**：每个模块只关注自己的功能，避免跨模块依赖
3. **合理命名**：使用清晰、描述性的命名，如 `Auto{Feature}TaskControl`
4. **复用组件**：利用共享的控件和样式，如 `TwoStateButton`、`CardExpander` 等

### 数据绑定

1. **共享 DataContext**：所有子模块共享主页面的 ViewModel，简化数据访问
2. **直接绑定**：直接绑定到 ViewModel 中的属性和命令，避免中间层
3. **配置访问**：通过 `Config.*` 路径访问配置数据
4. **命令绑定**：使用 `{Binding CommandName}` 绑定命令

### 本地化支持

1. **使用键值**：使用 `{local:Localize Key=key.name}` 而非硬编码文本
2. **检查键值**：确保所有本地化键在语言文件中存在
3. **避免硬编码**：避免在 XAML 或代码中使用硬编码的中文字符串

### 性能优化

1. **延迟加载**：考虑对不常用模块使用延迟加载
2. **资源共享**：共享样式和资源，减少内存占用
3. **事件处理**：合理使用事件处理，避免不必要的更新

## 注意事项

1. **保持向后兼容**：确保修改不会破坏现有功能
2. **测试验证**：修改后进行充分测试，确保功能正常
3. **注释代码**：对复杂逻辑添加注释，提高可读性
4. **预留功能**：对于预留功能，保持注释状态，不要删除

## 维护指南

1. **定期审查**：定期审查代码，检查是否有可优化的部分
2. **更新文档**：修改架构时更新本文档
3. **版本控制**：使用版本控制系统跟踪变更
4. **代码风格**：遵循项目的代码风格和命名规范

通过遵循这些指南，可以确保 TaskSettings 模块化架构的长期可维护性和可扩展性。