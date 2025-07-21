# Implementation Plan

- [x] 1. Create core localization infrastructure





  - Implement ILocalizationService interface and LocalizationService class
  - Create LanguageInfo model class for language metadata
  - Set up dependency injection registration in App.xaml.cs
  - _Requirements: 5.1, 5.2_

- [x] 2. Implement language file management system





  - Create ILanguageManager interface and LanguageManager class
  - Implement JSON language file discovery in Languages directory
  - Add language file loading and parsing functionality
  - Create error handling for malformed or missing files
  - _Requirements: 2.1, 2.2, 3.1, 3.2_

- [x] 3. Create XAML markup extension for localization





  - Implement LocalizeExtension class inheriting from MarkupExtension
  - Add INotifyPropertyChanged support for reactive binding
  - Implement ProvideValue method for XAML binding integration
  - Add parameter support for formatted strings
  - _Requirements: 5.2, 5.3_

- [x] 4. Set up language file structure and default translations





  - Create Languages directory in project root
  - Create en-US.json with base English translations
  - Create zh-CN.json with Chinese translations
  - Define JSON schema with metadata and strings sections
  - _Requirements: 2.1, 2.3_

- [x] 5. Add language selection UI components





  - Create language selection ComboBox in settings page
  - Implement LocalizationViewModel for language management
  - Add language switching functionality with immediate UI updates
  - Integrate with existing settings configuration system
  - _Requirements: 1.1, 1.2, 1.3_

- [x] 6. Update configuration system for language persistence





  - Add Language property to CommonConfig class
  - Implement language preference saving and loading
  - Add application startup language restoration
  - Ensure language setting persists across application restarts
  - _Requirements: 1.3, 1.4_

- [x] 7. Extract and localize hardcoded strings in main pages









  - Identify all hardcoded Chinese text in XAML files
  - Replace Text properties with LocalizeExtension bindings
  - Update CommonSettingsPage, MacroSettingsPage, and TaskSettingsPage
  - Add corresponding translation keys to language files
  - _Requirements: 4.1, 4.2, 4.3_

- [x] 8. Localize dialog boxes and popup messages





  - Extract hardcoded strings from dialog windows
  - Update message boxes and notification texts
  - Replace hardcoded strings in error messages
  - Add dialog-specific translation keys to language files
  - _Requirements: 4.4_

- [x] 9. Implement automatic language file discovery





  - Add file system watching for Languages directory
  - Implement dynamic language list updates
  - Add validation for language file naming convention
  - Create language metadata extraction from JSON files
  - _Requirements: 3.1, 3.2, 3.3_

- [x] 10. Add comprehensive error handling and fallback mechanisms











  - Implement missing translation key handling with fallback to English
  - Add graceful degradation for missing language files
  - Create logging for translation issues and missing keys
  - Implement error recovery for corrupted language files
  - _Requirements: 2.4, 3.4_

- [x] 11. Create unit tests for localization components
  - Write tests for LocalizationService language switching
  - Test LanguageManager file discovery and loading
  - Create tests for LocalizeExtension XAML binding
  - Add tests for error handling scenarios
  - _Requirements: 2.2, 2.3, 2.4_



- [x] 12. Integrate localization system with application lifecycle







  - Wire up localization service initialization in App.xaml.cs
  - Implement proper service disposal and cleanup
  - Add localization service to dependency injection container
  - Ensure proper initialization order with existing services
  - _Requirements: 5.1, 5.4_

- [x] 13. Complete localization of remaining hardcoded strings by page files







  - [x] HomePage.xaml: Already localized
  - [x] KeyMouseRecordPage.xaml: "键鼠录制回放功能（实验功能）", "建议在游戏内使用快捷键进行录制", "打开脚本目录", "脚本仓库", "开始录制", "停止录制", "名称", "创建时间", "操作", "播放脚本", "修改名称", "删除"

  - [x] OneDragon/MailPage.xaml: "领取所有邮件", "领取附件", "最大领取邮件数量"





  - [x] View/HardwareAccelerationView.xaml: "推理设备配置", "修改后需要重启程序生效", "推理设备类型", "选择推理使用的硬件设备类型", "缓存文件管理", "打开缓存目录", "强制OCR使用CPU", "GPU设备ID", "CUDA配置", "CUDA设备ID", "自动添加CUDA路径", "附加PATH", "TensorRT配置", "启用TensorRT缓存", "嵌入式引擎缓存"






  - [x] View/PathingConfigView.xaml: "地图追踪设置", "队伍切换设置", "角色设置", "使用小道具的间隔时间", "只在传送点时恢复", "则切换到队伍", "则角色执行", "删除", "+ 添加条件"





  - [x] View/ScriptGroupConfigView.xaml: "地图追踪行走配置", "是否开启自动拾取", "切换到队伍的名称", "切换队伍前强制前往安全的神像区域", "【生存位】在队伍内的编号", "【生存位】释放元素战技的间隔", "【生存位】元素战技是否长按", "【行走位】主要行走的角色在队伍内的编号", "战斗配置", "执行周期配置", "分界时间", "周期", "执行序号", "计算当天执行序号", "计算"





  - [x] NotificationSettingsPage.xaml: "通知设置", "全局通知设置", "影响下方所有通知的设置", "通知时包含截图", "总是在通知中包含截图", "是否允许 JS 通知", "开启时允许 JS 脚本发送通知", "需要通知的事件", "英文逗号分割，为空为全部通知", "启用 Webhook", "Webhook 相关设置", "Webhook 端点", "填写 Webhook 端点", "发送对象", "填写发送对象", "测试", "发送测试载荷", "发送", "启用 WebSocket", "WebSocket 相关设置", "WebSocket 端点", "填写 WebSocket 端点", "测试 WebSocket 通知", "发送测试通知", "启用 Windows 通知", "Windows 通知别与游戏界面重叠，否则易误点通知", "测试 Windows 通知", "启用飞书通知", "飞书通知相关设置", "飞书通知地址", "填写飞书通知地址", "飞书AppId", "若填写AppId、AppSecret则发送图片", "飞书AppSecret", "测试飞书通知", "启用 OneBot 通知", "OneBot 通知相关设置"




  - [x] OneDragonFlowPage.xaml: "任务列表", "配置", "合成树脂", "合成树脂合成台", "指定地区合成树脂", "合成后保留", "原粹树脂数量", "自动秘境", "（此处未覆盖的配置可在 独立任务-自动秘境 中配置）", "每日秘境刷取配置", "前往指定秘境消耗树脂，并自动领取奖励。", "进入秘境切换的队伍名称", "注意是游戏内你设置的名称", "填写队伍名称", "选择秘境", "秘境名称", "周日或限时", "选择序号", "每周秘境刷取配置", "启用后，每日刷取配置将会失效。", "± 鼠标右击添加或删除任务"



  - [x] ScriptControlPage.xaml: "配置组", "（实验功能）请在左侧栏选择或新增配置组", "在左侧栏目右键可以新增/修改配置组，拖拽进行配置组排序。配置组内可以添加并配置软件内的 Javascript 脚本、键鼠脚本等，并能够控制执行次数、顺序等，", "点击查看调度器使用教程", "在左侧栏目右键可以新增配置组，或者直接点击下面按钮新增配置组", "新增配置组", "（实验功能）配置组 - ", "在下方列表中右键可以添加配置，拖拽可以调整执行顺序。支持 BetterGI 内的 Javascript 脚本、键鼠录制脚本等，通过调度器可以设置脚本执行次数、顺序等。", "运行", "添加", "JS脚本", "地图追踪任务", "键鼠脚本", "Shell", "更多功能", "清空", "日志分析", "打开脚本仓库", "根据文件夹更新", "任务倒序排列", "导出根据控制文件修改任务", "设置", "#", "名称", "类型", "启用状态", "添加JS脚本", "添加地图追踪任务", "添加键鼠脚本", "添加Shell", "下一次任务从此处执行", "修改通用配置", "修改JS脚本自定义配置", "移除", "根据文件夹移除", "新增组", "删除组", "重命名", "复制组", "连续任务从此开始执行", "连续执行", "继续执行"


  - [x] MainWindow.xaml: (需要检查)






  
  - [x] CaptureTestWindow.xaml: (需要检查)






  
  - [x] MaskWindow.xaml: (需要检查)





  
  - [x] PickerWindow.xaml: (需要检查)






  
  - [x] OneDragon/CraftPage.xaml: (需要检查)






  
  - [x] OneDragon/DailyCommissionPage.xaml: (需要检查)


  
  - Add missing translation keys to language files
  - _Requirements: 4.1, 4.2, 4.3_

- [x] 14. Localize notification and message content





























  - [x] Localize all notification messages sent through various channels (Windows, WebSocket, Webhook, Feishu, OneBot)














-



  - [ ] Localize error messages and system notifications

  - [ ] Localize status update messages
  - [ ] Localize task completion and progress messages
  - [ ] Create a centralized message template system for consistent localization
  - [ ] Add message templates to language files
  - _Requirements: 4.3, 4.4, 4.5_

- [ ] 15. Localize log window content
  - [ ] Identify all hardcoded log messages in the application
  - [ ] Create a centralized logging system that supports localization
  - [ ] Extract log message templates to language files
  - [ ] Implement parameter substitution for dynamic log content
  - [ ] Ensure log timestamps and formatting respect locale settings
  - [ ] Update log viewer UI to display localized log messages
  - _Requirements: 4.3, 4.5_