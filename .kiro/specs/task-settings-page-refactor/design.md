# 任务设置页面重构设计文档

## 概述

本设计文档描述了如何将现有的TaskSettingsPage.xaml（2541行）重构为模块化的结构，参考OneDragon组的组织方式。通过将每个任务模块拆分为独立的UserControl，提高代码的可维护性和可扩展性。

## 架构

### 整体架构设计

```
TaskSettingsPage (主页面)
├── TaskSettings/ (子模块目录)
│   ├── AutoGeniusInvocationTaskControl.xaml
│   ├── AutoWoodTaskControl.xaml
│   ├── AutoFightTaskControl.xaml
│   ├── AutoDomainTaskControl.xaml
│   ├── AutoStygianOnslaughtTaskControl.xaml
│   ├── AutoMusicGameTaskControl.xaml
│   ├── AutoAlbumTaskControl.xaml
│   ├── AutoFishingTaskControl.xaml
│   ├── AutoRedeemCodeTaskControl.xaml
│   ├── AutoArtifactSalvageTaskControl.xaml
│   ├── GetGridIconsTaskControl.xaml
│   ├── AutoTrackTaskControl.xaml (预留，调试模式)
│   ├── AutoMoveTaskControl.xaml (预留，注释状态)
│   └── StopTaskControl.xaml (预留，注释状态)
└── TaskSettingsPageViewModel (保持现有ViewModel)
```

### 模块识别

基于对现有代码的分析，识别出以下主要任务模块：

**活跃模块（当前可用）：**
1. **自动七圣召唤** (AutoGeniusInvocation) - 卡牌游戏自动化
2. **自动伐木** (AutoWood) - 自动收集木材
3. **自动战斗** (AutoFight) - 自动战斗系统
4. **自动秘境** (AutoDomain) - 自动刷取秘境
5. **自动幽境危战** (AutoStygianOnslaught) - 特殊战斗模式
6. **自动千音雅集** (AutoMusicGame) - 音游自动化
7. **自动专辑** (AutoAlbum) - 音游专辑完成
8. **自动钓鱼** (AutoFishing) - 钓鱼自动化
9. **自动兑换码** (AutoRedeemCode) - 兑换码使用
10. **自动分解圣遗物** (AutoArtifactSalvage) - 圣遗物管理
11. **截取物品图标** (GetGridIcons) - 开发者工具

**预留模块（注释状态，未来功能）：**
12. **自动跟踪** (AutoTrack) - 自动跟踪剧情任务（调试模式可见）
13. **自动前进** (AutoMove) - 自动前进、飞行、游泳、爬山（注释状态）
14. **停止任务** (StopTask) - 停止独立任务运行（注释状态）

**说明：** 预留模块当前以注释形式存在，需要在重构时保留这些注释代码，以便未来功能开发时快速启用。

## 组件和接口

### 主页面组件 (TaskSettingsPage)

**职责：**
- 作为所有任务模块的容器
- 提供统一的页面标题和布局
- 管理子模块的显示

**主要元素：**
```xml
<Page>
    <StackPanel Margin="42,16,42,12">
        <ui:TextBlock Text="{local:Localize Key=task.title}" />
        
        <!-- 各个任务模块 -->
        <taskControls:AutoGeniusInvocationTaskControl DataContext="{Binding}" />
        <taskControls:AutoWoodTaskControl DataContext="{Binding}" />
        <taskControls:AutoFightTaskControl DataContext="{Binding}" />
        <!-- ... 其他模块 -->
    </StackPanel>
</Page>
```

### 任务模块基础结构

每个任务模块都遵循统一的结构模式：

```xml
<UserControl x:Class="BetterGenshinImpact.View.Pages.TaskSettings.{ModuleName}TaskControl">
    <ui:CardExpander Margin="0,0,0,12" ContentPadding="0">
        <ui:CardExpander.Icon>
            <!-- 模块特定图标 -->
        </ui:CardExpander.Icon>
        <ui:CardExpander.Header>
            <!-- 模块标题和控制按钮 -->
        </ui:CardExpander.Header>
        <!-- 模块特定设置内容 -->
    </ui:CardExpander>
</UserControl>
```

### 数据绑定策略

**方案1：共享DataContext（推荐）**
- 所有子模块共享主页面的TaskSettingsPageViewModel
- 通过`DataContext="{Binding}"`传递
- 保持现有的数据绑定路径不变

**方案2：独立ViewModel**
- 为复杂模块创建独立的ViewModel
- 通过依赖注入或工厂模式创建
- 适用于未来可能独立使用的模块

### 命名空间和引用

```xml
<!-- 在TaskSettingsPage.xaml中添加命名空间 -->
xmlns:taskControls="clr-namespace:BetterGenshinImpact.View.Pages.TaskSettings"

<!-- 在每个子模块中 -->
xmlns:controls="clr-namespace:BetterGenshinImpact.View.Controls"
xmlns:local="clr-namespace:BetterGenshinImpact.Markup"
xmlns:ui="http://schemas.lepo.co/wpfui/2022/xaml"
```

## 数据模型

### 现有数据模型保持不变

重构过程中不修改现有的数据模型和配置结构：
- `TaskSettingsPageViewModel` 保持现有接口
- 所有配置对象（如`Config.AutoGeniusInvokationConfig`）保持不变
- 命令和属性绑定路径保持一致

### 模块间通信

通过共享的ViewModel实现模块间通信：
- 共同的停止命令：`StopSoloTaskCommand`
- 共享的配置访问：`Config.*`
- 统一的状态管理

## 错误处理

### 编译时错误处理
- 确保所有XAML文件的命名空间正确
- 验证数据绑定路径的有效性
- 检查资源引用的完整性

### 运行时错误处理
- 保持现有的错误处理机制
- 确保模块加载失败不影响整个页面
- 提供降级显示方案

## 测试策略

### 功能测试
1. **模块独立性测试**
   - 验证每个模块可以独立显示和操作
   - 确保模块间不存在意外的依赖关系

2. **数据绑定测试**
   - 验证所有控件的数据绑定正常工作
   - 确保命令执行正确

3. **UI一致性测试**
   - 对比重构前后的界面显示
   - 验证所有功能按钮和设置项正常工作

### 性能测试
- 测量页面加载时间
- 验证内存使用情况
- 确保重构不影响响应性能

## 实现细节

### 注释代码处理策略

**保留原则：**
- 所有注释掉的功能模块都需要保留
- 注释状态的模块创建对应的UserControl，但保持注释状态
- 调试模式可见的模块需要保留其可见性条件

**实现方式：**
```xml
<!-- 注释状态的模块示例 -->
<!--
<taskControls:AutoMoveTaskControl DataContext="{Binding}" />
-->

<!-- 调试模式可见的模块示例 -->
<taskControls:AutoTrackTaskControl 
    DataContext="{Binding}"
    Visibility="{markup:Converter Value={x:Static helpers:RuntimeHelper.IsDebuggerAttached},
                                  Converter={StaticResource BooleanToVisibilityConverter}}" />
```

### 文件组织结构

```
BetterGenshinImpact/View/Pages/
├── TaskSettingsPage.xaml (重构后的主页面)
├── TaskSettingsPage.xaml.cs
└── TaskSettings/ (新建目录)
    ├── AutoGeniusInvocationTaskControl.xaml
    ├── AutoGeniusInvocationTaskControl.xaml.cs
    ├── AutoWoodTaskControl.xaml
    ├── AutoWoodTaskControl.xaml.cs
    ├── AutoFightTaskControl.xaml
    ├── AutoFightTaskControl.xaml.cs
    ├── AutoTrackTaskControl.xaml (预留模块)
    ├── AutoTrackTaskControl.xaml.cs
    ├── AutoMoveTaskControl.xaml (预留模块)
    ├── AutoMoveTaskControl.xaml.cs
    ├── StopTaskControl.xaml (预留模块)
    ├── StopTaskControl.xaml.cs
    └── ... (其他模块文件)
```

### 样式和资源共享

- 继续使用现有的样式资源
- 保持图标和主题的一致性
- 确保本地化支持正常工作

### 向后兼容性

- 保持所有公共接口不变
- 确保现有的快捷键和命令继续工作
- 维护配置文件的兼容性

## 迁移策略

### 分阶段实施

1. **第一阶段：基础设施**
   - 创建TaskSettings目录
   - 建立第一个示例模块
   - 验证基本架构

2. **第二阶段：核心模块**
   - 迁移主要的任务模块（前5个）
   - 测试功能完整性

3. **第三阶段：完整迁移**
   - 迁移剩余模块
   - 清理原始文件
   - 最终测试和优化
   - 本目录下留下readme.md方便后人维护

### 风险缓解

- 保留原始文件作为备份
- 分模块进行测试验证
- 提供回滚方案

## 未来扩展性

### 新模块添加

添加新任务模块的标准流程：
1. 在TaskSettings目录创建新的UserControl
2. 在主页面添加模块引用
3. 在ViewModel中添加相关属性和命令
4. 更新测试用例

### 模块复用

设计支持模块在其他页面中复用：
- 独立的UserControl设计
- 最小化外部依赖
- 清晰的接口定义

这种设计确保了代码的可维护性、可扩展性和可测试性，同时保持了与现有系统的完全兼容性。