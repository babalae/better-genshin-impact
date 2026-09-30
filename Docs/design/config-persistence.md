# 配置持久化链路（AllConfig 保存）优化

> 状态：已实施（第 1~3 项） · 2026-09-30

## 1. 背景

`AllConfig` 是全局唯一的配置对象，保存在 `User/config.json` 中。读取入口有两个，拿到的是同一个实例：

- 静态入口 `TaskContext.Instance().Config`，转发到 `ConfigService.Config`。有 296 处调用，分布在 120 个文件。
- DI 入口 `IConfigService.Get()`，约 27 个文件使用。

"全局静态、任何地方都能读写"这种用法本身不变。本次只改写入之后的链路：变更感知、触发器刷新和落盘。

## 2. 现状问题

### 2.1 每次属性变更的代价过高

`AllConfig.OnAnyPropertyChanged` 会在"调用方线程"上同步执行下面两步：

1. `GameTaskManager.RefreshTriggerConfigs()`：
   - 重新 `Init()` 7 个触发器。其中 AutoPick 会重读 3~4 个名单文件，AutoSkip 会重读 3 个 JSON。
   - SkillCd 的 `Init()` 会清空所有 CD 计时，所以改任何一个无关配置都会让 CD 提示归零。
   - 最后调用 `DrawContent.ClearAll()`，同步 `Dispatcher.Invoke` 到 UI 线程。
2. `ConfigService.Save()`：全量序列化后执行 `File.WriteAllText`。

XAML 里约有 44 处配置绑定使用了 `UpdateSourceTrigger=PropertyChanged`，每输入一个字符就会把上面两步完整走一遍。

`File.WriteAllText` 采用截断后覆盖写入。如果写到一半进程退出，文件就会损坏。现在的 `BackupConfigFile` 就是为这种情况兜底的。

### 2.2 自动保存的订阅不完整

`InitEvent()` 手写了 32 行 `PropertyChanged +=`，存在以下遗漏：

- 未订阅的一级配置：`OtherConfig`、`RecordConfig`、`AutoComboBuildConfig`、`AutoGeniusInvokationConfig`、`GetGridIconsConfig`。
- 所有嵌套对象，例如 `OtherConfig.AutoRestartConfig`、`AutoFightConfig.FinishDetectConfig`、`PathingConditionConfig` 中的 `Condition`、`SkillCdConfig.CustomCdList` 中的规则。
- 所有 `ObservableCollection` 的增删。

这些改动只有在托盘退出时由 `NotifyIconViewModel.Exit()` 顺带保存。进程被杀、崩溃，或者走 `Environment.Exit` 的重启路径时，改动都会丢失。

### 2.3 线程模型与注释不一致

`ConfigService.Config` 的注释写着"写入只有 UI 线程会调用"，但下面这些地方都会在非 UI 线程写配置：

- 任务线程：`AutoFishingTask`、`AutoWoodTask`、`AutoLeyLineOutcropTask`、`TurnAroundMacro`。
- 截图线程：`SkillCdTrigger` 检测到联机时，会在 `OnCapture` 内部关闭自身。
- 其他：WebView 桥和热键回调。

后果：

- 整份配置会在这些线程上序列化。如果 UI 线程同时在修改 `List`，就可能抛出 "Collection was modified"，异常最终会变成一个错误弹窗。
- `SkillCdTrigger` 这条路径会在持有 `_triggerListLocker` 的情况下同步写盘。

## 3. 目标与非目标

目标：

1. 保存做防抖和原子写，触发器只按变更来源定向刷新。
2. 自动覆盖整个配置对象图，新增配置类时不需要再去登记。
3. 任何线程都可以写配置，保存侧不再产生竞争。

非目标（留给后续）：

- 不改动 296 处调用点，也不引入新的访问入口。
- 不处理运行时状态混在配置中的问题：`NextScriptGroupName`、`AutoLeyLineOutcropTask` 临时替换 `AutoFightConfig`、临时打开 `DisplayRecognitionResultsOnMask`。
- 不处理多个实例（主实例与桌面分身）同时写同一个 `config.json` 时"后写者覆盖"的问题。

## 4. 设计

### 4.1 变更追踪：`ConfigChangeTracker`

新增 `Core/Config/ConfigChangeTracker.cs`。它从 `AllConfig` 根节点开始，递归订阅：

| 节点类型 | 处理方式 |
| --- | --- |
| `INotifyPropertyChanged` | 订阅 `PropertyChanged`，并继续遍历其公开实例属性 |
| `INotifyCollectionChanged` | 订阅 `CollectionChanged` |
| `IEnumerable` / `IDictionary`（不含 `string`） | 遍历元素（字典遍历 Value） |
| 其他 POCO、值类型、委托 | 跳过 |

遍历属性时，跳过以下几类：有索引参数的属性、没有公开 getter 的属性、标注 `[JsonIgnore]`（`Condition = Always`）的属性、值类型、`string` 和委托。每个 getter 都包在 `try/catch` 中。

订阅是增量且幂等的：

- 已访问的对象记录在 `ConditionalWeakTable` 中。同一对象只订阅一次，环也不会导致无限递归。弱引用不会阻止对象被 GC。
- 某个属性变更时，如果新值是可追踪对象（例如子配置被整体替换），就补订阅这个新值。`PropertyName` 为空时，重新遍历该对象的所有子节点。
- 集合发生 `Add`/`Replace` 时，补订阅新元素；发生 `Reset` 时，重新遍历整个集合。
- 被替换掉的旧对象不主动退订。它们以后如果再触发事件，只会多产生一次保存请求，没有副作用。

不做全量重扫，原因是 `AutoLeyLineOutcropTask` 会在战斗期间把 `AllConfig.AutoFightConfig` 临时换成另一个实例。如果此时全量重扫，追踪就会挂到这个临时对象上，而原对象丢失订阅。增量订阅不会去读根节点上没有通知能力的自动属性，所以不受这次临时替换的影响。

每次变更都会回调 `onChanged(sender)`，回调在触发事件的线程上同步执行。

### 4.2 触发器定向刷新

`AllConfig.InitEvent()` 改为创建 tracker，回调中做两件事：

1. `GameTaskManager.RefreshTriggerConfigs(sender)`（新增重载）。
2. `OnAnyChangedAction?.Invoke()`，也就是 `ConfigService.RequestSave`。

`NotificationConfig` 仍然单独订阅，用来调用 `NotificationService.RefreshNotifiers()`。

各触发器的 `Init()` 只依赖自身对应的那份子配置（已逐一核对），映射如下：

| `sender` 类型 | 重新 `Init()` 的触发器 |
| --- | --- |
| `AutoPickConfig` | AutoPick |
| `AutoSkipConfig` | AutoSkip |
| `AutoFishingConfig` | AutoFish |
| `QuickTeleportConfig` | QuickTeleport |
| `AutoEatConfig` | AutoEat |
| `MapMaskConfig` | MapMask |
| `SkillCdConfig` | SkillCd |

当有触发器被刷新，或者 `sender` 是 `MaskWindowConfig` 时，额外执行一次 `DrawContent.ClearAll()`。这样可以保留两种原有行为：关闭触发器时擦掉它的残留绘制，以及切换识别结果显示开关时清掉过期的图形。其他配置变更不再刷新触发器，也不再清空画布。

刷新仍然是同步执行的，不做防抖，因此"改开关后触发器立即生效"的语义保持不变。

显式调用无参 `RefreshTriggerConfigs()` 的地方保持全量刷新，比如 AutoPick 名单编辑窗口：它只改了文件，配置里的 bool 值可能没有变化。

### 4.3 保存调度

`ConfigService` 内部维护两个版本号：

- `_changeSeq`：每次保存请求时加 1。
- `_persistedSeq`：已经落盘的快照对应的 `_changeSeq`。

两者不相等时，表示有未保存的改动。

防抖（`RequestSave`，任意线程可调用）：

1. `_changeSeq++`。如果当前没有待执行的保存，就启动一个 500ms 的定时器。窗口内的多次请求合并为一次，最大延迟有上限。
2. 定时器触发后，在 UI 线程上以 `DispatcherPriority.Background` 生成快照：先读取 `_changeSeq`，再序列化得到 JSON。
3. 回到线程池执行原子写。写入前检查：如果快照的版本号不大于 `_persistedSeq`，说明已经有更新的内容落盘，本次直接跳过。

快照放在 UI 线程生成，是因为绝大多数集合（包括所有 WPF 绑定的 `ObservableCollection`）都只在 UI 线程修改，在这里序列化就不会与它们竞争。任务线程改的是标量或引用，读取本身是安全的。

兜底策略：

- UI 线程 3 秒内没有执行，或者 Dispatcher 已经开始关闭时，改在当前线程生成快照。
- 快照生成抛异常（多半是其他线程正在改集合）时，稍后重试。连续失败 3 次后弹窗提示，并停止自动重试，等到下一次改动或退出时再尝试。

读写互不阻塞：

- 代码中读写配置（`Config.Xxx`）操作的是内存对象，全程不加锁，写完立即可读。保存链路只登记改动，不拦截任何读写。
- 生成快照时持有的锁只在快照之间互斥，不影响属性读写。其他线程在快照期间照常读写；期间发生的改动版本号更大，会进入下一次保存。
- 读文件（`Read()`）不加锁。原子替换保证读取方只会看到完整的旧文件或新文件。文件内容比内存最多落后一个防抖窗口，需要最新文件内容的地方先调用 `Flush()`。
- 反过来，如果读取方打开文件时没有开放删除共享，替换会短暂失败。这时由写入方重试，读取方不受影响。

原子写：

1. 写入临时文件 `config.json.<pid>.<guid>.tmp`，内容为 UTF-8 无 BOM，然后 `Flush(true)`。
2. 用 `File.Move(tmp, config.json, overwrite: true)` 替换正式文件。这是同卷 rename，读取方只会看到旧文件或新文件，不会看到写了一半的文件。
3. 替换时如果遇到共享冲突，最多重试 5 次，每次间隔 50ms，最后仍失败则弹窗，与现有行为一致。
4. 临时文件名带上进程号和 GUID，避免主实例和桌面分身互相踩到同一个临时文件。
5. 启动时清理 1 分钟前残留的临时文件。

### 4.4 立即保存与退出落盘

- `Save()`：先执行 `_changeSeq++`，再同步生成快照并写盘，语义与现在一致（立即写）。不在 UI 线程时，用 `Dispatcher.Invoke` 生成快照，超时后退回当前线程。
- `Flush()`（`IConfigService` 新增）：只在有未保存改动时执行 `Save()`。

调用点：

| 场景 | 处理 |
| --- | --- |
| `App.OnExit` | `Flush()` |
| `AppDomain.ProcessExit`（覆盖 `Environment.Exit`） | `Flush()`，不弹窗 |
| `SystemControl.RestartApplication` | 启动新进程前 `Flush()`，避免新进程读到旧配置 |
| `ChildSessionProcessLauncher.LaunchBetterGiAsync` | 启动分身前 `Flush()` |
| `RepoWebBridge.GetUserConfigJson` | 读取文件前 `Flush()` |

`Process.Kill` 和崩溃时，最多丢失最近约 500ms 内的改动。

## 5. 行为变化

| 场景 | 之前 | 之后 |
| --- | --- | --- |
| 输入框每输入一个字符 | 全量刷新 7 个触发器，同步写盘 | 只刷新相关触发器（如果有），500ms 合并写一次 |
| 修改与触发器无关的配置 | SkillCd 的 CD 计时归零，AutoPick 重读名单，画布被清空 | 不影响触发器 |
| 脚本通过 `AddTrigger` 强制启用的 AutoPick | 任意配置变更都会把它重置为全局开关状态 | 只有 `AutoPickConfig` 变更时才会重置 |
| 修改 `OtherConfig` 和嵌套对象、增删集合 | 只在托盘退出时保存 | 自动保存 |
| 写盘中途进程退出 | 可能留下损坏的 `config.json` | 旧文件保持完整 |
| 非 UI 线程写配置 | 在该线程上序列化和写盘 | 只登记改动，序列化在 UI 线程完成 |

## 6. 改动清单

- 新增 `Core/Config/ConfigChangeTracker.cs`。
- `Core/Config/AllConfig.cs`：`InitEvent()` 改用 tracker，删除 `OnAnyPropertyChanged`。
- `GameTask/GameTaskManager.cs`：新增 `RefreshTriggerConfigs(object? changedConfig)`。
- `Service/ConfigService.cs`、`Service/Interface/IConfigService.cs`：实现保存调度、原子写和 `Flush()`。
- `App.xaml.cs`、`GameTask/SystemControl.cs`、`Service/ChildSession/ChildSessionProcessLauncher.cs`、`Core/Script/WebView/RepoWebBridge.cs`：在对应位置调用 `Flush()`。
