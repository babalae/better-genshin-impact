# TriggerSettings 模块

本目录包含了触发器设置页面的各个子模块，用于将原本的大型 TriggerSettingsPage.xaml 文件拆分为更易维护的独立模块。这种模块化架构参考了 TaskSettingsPage 的重构方式，通过关注点分离提高了代码的可维护性和可扩展性。

## 模块化架构

### 架构概述

```
TriggerSettingsPage (主页面)
├── TriggerSettings/ (子模块目录)
│   ├── AutoPickTriggerControl.xaml (自动拾取)
│   ├── AutoSkipTriggerControl.xaml (自动剧情)
│   ├── AutoHangoutTriggerControl.xaml (自动邀约)
│   ├── AutoFishingTriggerControl.xaml (自动钓鱼)
│   ├── QuickTeleportTriggerControl.xaml (快速传送)
│   └── README.md (本文档)
└── TriggerSettingsPageViewModel (共享ViewModel)
```

### 设计原则

1. **模块独立性**: 每个触发器功能作为独立的 UserControl 实现
2. **数据共享**: 所有模块共享主页面的 TriggerSettingsPageViewModel
3. **接口一致性**: 统一的模块结构和命名约定
4. **向后兼容**: 保持与原有功能完全一致的用户体验
5. **注释保留**: 保留所有注释代码以备将来功能启用

## 当前模块结构

### AutoPickTriggerControl
- **文件**: `AutoPickTriggerControl.xaml` 和 `AutoPickTriggerControl.xaml.cs`
- **功能**: 自动拾取物品相关设置
- **配置绑定**: `Config.AutoPickConfig.*`
- **主要设置**: 启用状态、拾取范围、物品过滤等

### AutoSkipTriggerControl
- **文件**: `AutoSkipTriggerControl.xaml` 和 `AutoSkipTriggerControl.xaml.cs`
- **功能**: 自动剧情跳过相关设置
- **配置绑定**: `Config.AutoSkipConfig.*`
- **包含设置**:
  - 启用/禁用自动剧情
  - 后台模式
  - 快速跳过对话
  - 选项优先级
  - 选项延迟
  - 自动提交物品
  - 关闭弹窗页面
  - 每日委托奖励
  - 重新探索

### AutoHangoutTriggerControl
- **文件**: `AutoHangoutTriggerControl.xaml` 和 `AutoHangoutTriggerControl.xaml.cs`
- **功能**: 自动邀约事件处理
- **配置绑定**: `Config.AutoHangoutConfig.*`
- **主要设置**: 邀约自动接受、对话处理等

### AutoFishingTriggerControl
- **文件**: `AutoFishingTriggerControl.xaml` 和 `AutoFishingTriggerControl.xaml.cs`
- **功能**: 自动钓鱼相关设置
- **配置绑定**: `Config.AutoFishingConfig.*`
- **主要设置**: 钓鱼自动化、鱼类识别等

### QuickTeleportTriggerControl
- **文件**: `QuickTeleportTriggerControl.xaml` 和 `QuickTeleportTriggerControl.xaml.cs`
- **功能**: 快速传送功能设置
- **配置绑定**: `Config.QuickTeleportConfig.*`
- **主要设置**: 传送点管理、快捷键配置等

## 数据绑定策略

### 共享 DataContext
- 所有子模块通过 `DataContext="{Binding}"` 共享主页面的 TriggerSettingsPageViewModel
- 保持现有的数据绑定路径不变，确保向后兼容性
- 配置访问模式：`Config.{ModuleName}Config.*`

### 命令绑定
- 模块内的命令通过 ViewModel 统一管理
- 支持跨模块的命令调用和状态同步

## 扩展方法

### 添加新触发器模块

要添加新的触发器模块，请按以下标准流程操作：

#### 1. 创建模块文件
在本目录创建新的 UserControl：
```
YourNewTriggerControl.xaml
YourNewTriggerControl.xaml.cs
```

#### 2. 模块基础结构
使用统一的模块模板：
```xml
<UserControl x:Class="BetterGenshinImpact.View.Pages.TriggerSettings.YourNewTriggerControl"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:ui="http://schemas.lepo.co/wpfui/2022/xaml"
             xmlns:markup="clr-namespace:BetterGenshinImpact.Markup">
    
    <ui:CardExpander Margin="0,0,0,12" ContentPadding="0">
        <ui:CardExpander.Icon>
            <!-- 模块特定图标 -->
        </ui:CardExpander.Icon>
        <ui:CardExpander.Header>
            <!-- 模块标题和主要控制按钮 -->
        </ui:CardExpander.Header>
        <!-- 模块具体设置内容 -->
    </ui:CardExpander>
</UserControl>
```

#### 3. 集成到主页面
在主 TriggerSettingsPage.xaml 中添加模块引用：
```xml
<!-- 确保命名空间已包含 -->
xmlns:triggerControls="clr-namespace:BetterGenshinImpact.View.Pages.TriggerSettings"

<!-- 在适当位置添加模块 -->
<triggerControls:YourNewTriggerControl DataContext="{Binding}" />
```

#### 4. ViewModel 扩展
在 TriggerSettingsPageViewModel 中添加：
- 相关配置属性的访问器
- 模块特定的命令和方法
- 必要的数据验证逻辑

#### 5. 配置类扩展
如需要，在相应的 Config 类中添加新的配置项

#### 6. 更新文档
更新本 README.md 文件，添加新模块的说明

### 模块复用策略

设计模块时考虑复用性：
- 最小化外部依赖
- 使用清晰的接口定义
- 支持在其他页面中使用

## 维护指南

### 日常维护

#### 修改现有模块
1. **数据绑定检查**: 确保所有数据绑定路径正确，特别是 `Config.*` 路径
2. **功能兼容性**: 保持与原有功能的完全兼容性，避免破坏性更改
3. **测试验证**: 测试所有设置项的正常工作，包括保存和加载
4. **注释保留**: 保留所有注释代码的原有状态，不要随意删除
5. **样式一致性**: 遵循现有的 UI 设计模式和样式规范

#### 代码规范
- **命名约定**: 遵循现有的代码风格和命名约定
- **本地化支持**: 使用 `{markup:Localize Key=...}` 进行文本本地化
- **错误处理**: 实现适当的错误处理和用户反馈
- **性能考虑**: 避免不必要的资源消耗和内存泄漏

### 故障排除

#### 常见问题及解决方案

1. **数据绑定失效**
   - 检查 DataContext 是否正确传递
   - 验证绑定路径是否存在于 ViewModel 中
   - 确认属性实现了 INotifyPropertyChanged

2. **本地化文本不显示**
   - 检查本地化键值是否正确
   - 确认本地化资源文件是否包含对应条目
   - 验证 markup 命名空间是否正确引用

3. **模块加载失败**
   - 检查 UserControl 的命名空间和类名
   - 确认主页面中的模块引用正确
   - 验证编译是否成功

4. **功能异常**
   - 对比重构前后的代码逻辑
   - 检查配置对象的属性访问
   - 验证命令绑定是否正确

### 测试策略

#### 功能测试清单
- [ ] 所有设置项可以正常显示和操作
- [ ] 配置保存和加载功能正常
- [ ] 本地化文本正确显示
- [ ] 模块间不存在冲突
- [ ] 性能表现符合预期

#### 回归测试
- 对比重构前后的功能一致性
- 验证所有快捷键和命令正常工作
- 确认配置文件兼容性

### 注释代码处理策略

#### 保留原则
- **完整保留**: 所有注释掉的功能模块和代码都需要完整保留
- **状态维护**: 在迁移到子模块时，注释代码应保持原有的注释状态
- **条件保留**: 调试模式可见的模块需要保留其可见性条件

#### 实现方式
```xml
<!-- 注释状态的功能示例 -->
<!--
<Grid Margin="16">
    <ui:TextBlock Text="{markup:Localize Key=trigger.someFeature}" />
    <ui:ToggleSwitch IsChecked="{Binding Config.SomeConfig.Enabled, Mode=TwoWay}" />
</Grid>
-->

<!-- 注释状态的模块引用示例 -->
<!--
<triggerControls:SomeFeatureTriggerControl DataContext="{Binding}" />
-->
```

### 性能优化

#### 最佳实践
- **延迟加载**: 对于复杂模块，考虑实现延迟加载
- **资源管理**: 及时释放不需要的资源
- **绑定优化**: 使用合适的绑定模式（OneWay, TwoWay, OneTime）
- **UI虚拟化**: 对于大量数据的显示，考虑使用虚拟化技术

### 版本兼容性

#### 向后兼容
- 保持所有公共接口不变
- 维护配置文件的兼容性
- 确保现有快捷键和命令继续工作

#### 升级路径
- 提供清晰的升级指南
- 保留必要的迁移工具
- 文档化所有重要变更

## 相关文件

### 核心文件
- **主页面**: `../TriggerSettingsPage.xaml` - 触发器设置主页面
- **主页面代码**: `../TriggerSettingsPage.xaml.cs` - 主页面后台代码
- **ViewModel**: `../../../ViewModel/Pages/TriggerSettingsPageViewModel.cs` - 页面视图模型

### 配置文件
- **自动拾取配置**: `Config.AutoPickConfig`
- **自动剧情配置**: `Config.AutoSkipConfig`
- **自动邀约配置**: `Config.AutoHangoutConfig`
- **自动钓鱼配置**: `Config.AutoFishingConfig`
- **快速传送配置**: `Config.QuickTeleportConfig`

### 本地化资源
- 本地化键值定义在相应的资源文件中
- 使用 `trigger.*` 前缀的键值用于触发器相关文本

## 联系信息

如有问题或建议，请参考项目的贡献指南或联系维护团队。

---

**最后更新**: 根据触发器设置页面重构需求更新
**维护者**: BetterGenshinImpact 开发团队