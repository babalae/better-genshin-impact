## “自动导航”到底是什么

游戏内视觉引导跟随导航/坐标导航

游戏内视觉引导跟随导航，通过分析游戏画面中的引导信息，控制人物继续走最后一段路。

目前规划的视觉引导包括两类：

### 1. 屏幕任务目标标记跟随

也就是现有 `AutoTrackTask` 的方式：

```text
按下任务导航键
→ 截图识别屏幕上的任务目标标记
→ 计算标记相对屏幕中心的偏移
→ 转动镜头
→ 按住 W 前进
→ 持续重新识别
```

现有代码已经能识别 `BlueTrackPoint`、调整方向和按住前进，但到达判断粗糙，也没有成熟的运动检测和脱困。

对应源码：

[AutoTrackTask：任务目标标记跟随原型](https://github.com/babalae/better-genshin-impact/blob/main/BetterGenshinImpact/GameTask/AutoSkip/AutoTrackTask.cs)

### 2. 地面黄色闪光指引跟随

城市、室内导航方法：

```text
识别前方地面的黄色闪光点
→ 朝最近闪光点移动
→ 接近后寻找下一个闪光点
→ 沿着闪光点序列转弯、上楼、进门
```

* `QuestMarkerFollower`：屏幕任务标记跟随；
* `YellowTrailFollower`：地面黄色闪光引导跟随；
* 两者共同属于“游戏内视觉引导跟随”。

---

# 一、已经完成，可以直接复用

## 1. 自动剧情流程加载与编排

[自动剧情加载器](https://github.com/LX666-666/BadGI-JsScript/tree/main/%E8%87%AA%E5%8A%A8%E5%89%A7%E6%83%85%E5%8A%A0%E8%BD%BD%E5%99%A8)

[自动剧情加载器 main.js](https://github.com/LX666-666/BadGI-JsScript/blob/main/%E8%87%AA%E5%8A%A8%E5%89%A7%E6%83%85%E5%8A%A0%E8%BD%BD%E5%99%A8/main.js)

### 已经具备

* 加载 `process.json`；
* OCR 识别当前任务描述；
* 文本清洗与相似度匹配；
* 同名任务阶段按顺序执行；
* 默认任务块；
* 地图追踪；
* 键鼠脚本；
* 对话；
* 交互；
* 战斗；
* 自动拾取开关；
* 等待返回主界面；
* 暂停；
* 任务完成；
* 角色切换；
* 图像匹配；
* OCR 点击文字；
* 按键按下、长按和松开；
* 消息通知；
* 新旧格式兼容。

加载器已经拥有完整的指令解释器和步骤处理器，例如现有代码会直接调用 `pathingScript.runFile()` 执行地图路线，也有任务描述相似度计算和多种指令处理。

### PRD 中的定位

直接作为：

> `ProcessOverrideEngine` 和特殊任务流程执行器。


只需要增加几条新的系统级指令，例如：

```text
自动导航
自动导航到任务目标
等待任务阶段变化
允许剧情战斗
视觉引导
重新规划
```

---

## 2. 自动剧情录制和任务数据生产

[自动剧情录制器](https://github.com/duoduo1232/AutoStoryTranscribe)

[自动剧情录制器 main.js](https://github.com/duoduo1232/AutoStoryTranscribe/blob/main/main.js)

### 已经具备

* 录制玩家位置；
* 生成 BetterGI 地图追踪 JSON；
* 生成 `process.json`；
* 分段录制；
* 暂停和继续录制；
* OCR 提取任务描述；
* OCR 提取 NPC 名称；
* 生成对话指令；
* 生成战斗点；
* 记录剧情开始；
* 传送检测；
* RDP 路径简化；
* 保存任务完成状态；
* AutoSkip、AutoPick 常驻；
* 键鼠回调触发录制操作。

仓库 README 明确说明它能够生成地图追踪和 `process.json`，并处理对话、战斗、暂停和任务完成。

当前主脚本也已经把 AutoSkip、AutoPick 注册为实时触发器，并在录制时持续记录位置和界面变化。

### PRD 中的定位

直接作为：

> `QuestTelemetryRecorder` 和任务模板生产工具。

后续主要增加：

* 自动导航成功轨迹标记；
* 卡死点记录；
* 黄光导航轨迹记录；
* 任务阶段开始和结束标识；
* 成功轨迹回写路网。

---

## 3. AutoSkip 自动剧情

[AutoSkipTrigger](https://github.com/babalae/better-genshin-impact/blob/main/BetterGenshinImpact/GameTask/AutoSkip/AutoSkipTrigger.cs)

### 已经具备

* 对话自动推进；
* 自动点击对话选项；
* 优先选项关键词；
* 暂停选项关键词；
* 后台运行；
* Talk 界面实时触发；
* 可作为非独占触发器运行。

`AutoSkipTrigger` 本身就是非独占实时触发器，支持后台运行，适合在整个自动任务过程中常驻。

### PRD 中的定位

直接作为：

> 常驻剧情处理器。

---

## 4. AutoFight 自动战斗

[AutoFightTask](https://github.com/babalae/better-genshin-impact/blob/main/BetterGenshinImpact/GameTask/AutoFight/AutoFightTask.cs)

[AutoFight 参数](https://github.com/babalae/better-genshin-impact/blob/main/BetterGenshinImpact/GameTask/AutoFight/AutoFightParam.cs)

[战斗策略工厂](https://github.com/babalae/better-genshin-impact/tree/main/BetterGenshinImpact/GameTask/AutoFight/Factory)

### 已经具备

* 根据战斗策略切换角色；
* 普攻、技能和爆发；
* 自动选择战斗策略；
* 执行文本或 JSON 战斗策略；
* 判断战斗是否结束；
* 异常和取消处理。

AutoFight 本身已经成熟，无需重新编写战斗行为。相关核心类和策略工厂已经存在。

### PRD 中的定位

直接作为：

> 已确认剧情战斗之后的执行器。

### 仍然缺少
> `StoryCombatGate`——判断当前战斗究竟是剧情要求的战斗，还是路边野怪干扰。

---

## 5. AutoPick 自动拾取

[AutoPickTrigger](https://github.com/babalae/better-genshin-impact/blob/main/BetterGenshinImpact/GameTask/AutoPick/AutoPickTrigger.cs)

### 已经具备

* 实时识别交互提示；
* 自动按 F；
* 拾取文本处理；
* 可作为实时触发器运行。

现成的 `AutoPickTrigger` 已经存在，无需重新开发拾取主体。

### PRD 中的定位

* 普通导航时按配置常驻；
* 剧情战斗完成后短时间继续拾取；
* 对容易误触 NPC 或机关的区域，可以临时关闭。

---

## 6. 已有地图路径执行器

[PathExecutor](https://github.com/babalae/better-genshin-impact/blob/main/BetterGenshinImpact/GameTask/AutoPathing/PathExecutor.cs)

### 已经具备

* 执行地图路径点；
* 定位角色坐标；
* 调整镜头和方向；
* 支持传送点；
* 支持不同移动模式；
* 路点 Action；
* 战斗、采集和交互 Action；
* 取消和异常处理。

### PRD 中的定位

直接作为：

> PR #2978 规划出临时路线后的执行器。

---

## 7. 游戏界面和基础运动状态识别

[BvStatus：界面和运动状态识别](https://github.com/babalae/better-genshin-impact/blob/main/BetterGenshinImpact/GameTask/Common/BgiVision/BvStatus.cs)

### 已经具备

* 主界面识别；
* 对话界面识别；
* 大地图界面识别；
* 地下地图状态识别；
* 等待返回主界面；
* 攀爬状态识别；
* 滑翔状态识别。

目前 `GetMotionStatus()` 主要通过空格键和 X 键图标区分：

* `Normal`；
* `Climb`；
* `Fly`。

### 可直接复用的部分

* UI 状态；
* 攀爬；
* 滑翔；
* 主界面等待；
* 大地图检测。

### 仍需要扩展

* 正常行走；
* 奔跑；
* 冲刺；
* 游泳；
* 下落；

[游泳检测](https://github.com/LX666-666/BadGI-JsScript/blob/main/AutoTranscribePathing/lib/detection.js)

---

# 二、已有实现，但只能改造后使用

## 8. PR #2978 路网规划与新寻路框架

[PR #2978：Refactor Pathing](https://github.com/babalae/better-genshin-impact/pull/2978)

### 已经实现的部分

PR 中已经加入：

* `RouteNavigationGraphBuilder`；
* `RouteNavigationGraphProvider`；
* `RouteNavigationPlanner`；
* `RouteTelemetryManager`；
* `RouteHealthStore`；
* `PathingNavigator`；
* `PathingMovementController`；
* `PathingAnomalyResolver`；
* `StuckDetector`；
* `TrapEscaper`；
* 行走、奔跑、冲刺、跳跃、攀爬和飞行处理器；
* 路线健康度；
* 路网调试；
* 单元测试。

PR 的说明明确表示，已经完成路网生成、图上路径规划、路线遥测、移动控制拆分、卡死检测和陷阱脱困等代码，并已通过本地 Debug 编译。

其中 `StuckDetector` 已能根据多帧位置变化判断长时间原地停滞。

`TrapEscaper` 已经包含：

* 后退；
* 左右移动；
* 随机角度修正；
* 攀爬脱离；
* 向上一有效路点回退；
* 超时处理；
* 必定释放 W 键。

### 当前状态

截至当前检查：

* Open；
* Draft；
* 未合并；
* 仍需要实机长路线、传送分段、战斗和采集混合路线回归。

### PRD 中的定位

它应当作为：

> 第一优先级路网导航器，以及运动、卡死检测和脱困能力的主要来源。

### 不需要重写

以下都应优先从该 PR 抽取：

* 路网生成；
* 图搜索；
* 临时 PathingTask 生成；
* 移动控制；
* 路点策略；
* 卡死检测；
* 陷阱脱困；
* 路线遥测；
* 路线健康度；
* 移动模式处理。

### 需要增加

* 规划到离任务目标最近的可用路网节点；
* 到达路网终点后自动切换任务引导跟随；
* 无法获取准确目标坐标时的近似目标处理；
* 与任务状态机对接。

---

## 9. AutoTrackTask 任务标记跟随

[AutoTrackTask：无预制路线追踪原型](https://github.com/babalae/better-genshin-impact/blob/main/BetterGenshinImpact/GameTask/AutoSkip/AutoTrackTask.cs)

[废弃的 AutoTrack 启动入口](https://github.com/babalae/better-genshin-impact/blob/main/BetterGenshinImpact/ViewModel/Pages/TaskSettingsPageViewModel.cs)

### 已经具备

* OCR 识别任务文字；
* OCR 提取任务距离；
* 距离较远时尝试选择传送点；
* 按任务导航键；
* 识别 `BlueTrackPoint`；
* 根据目标点左右偏移转动镜头；
* 按住 W 向前移动。

### 当前问题

* 启动入口已废弃；
* 没有稳定卡死恢复；
* 没有判断距离是否持续下降；
* 没有黄色地面指引；
* 没有道路理解；
* 到达判断过于粗糙；
* 目标标记丢失后没有有效恢复；
* 可能把目标在楼上、楼下或墙后的情况误判为到达。

### PRD 中的定位

不直接恢复原类，而是拆成：

* `QuestMarkerRecognizer`；
* `QuestMarkerFollower`；
* `ArrivalVerifier`。

---

## 10. 自动寻路总体思路参考

[Issue #49：自动寻路，做任务](https://github.com/babalae/better-genshin-impact/issues/49)

这个 Issue 已经描述了原始思路：

```text
打开任务
→ 选择追踪
→ 找最近传送点
→ 按 V
→ 跟随任务箭头
→ 到达后对话或战斗
→ 重复
```

但它只是需求描述，不是完成实现。

---

# 三、目前没有完成，需要新开发

## 1. 混合导航总控制器

暂定名称：

```text
HierarchicalQuestNavigator
```

负责：

```text
PR #2978 路网规划
→ PathExecutor 执行
→ 到达最近路网节点
→ 切换任务标记或黄色闪光跟随
→ 卡死时调用 PR #2978 的恢复组件
→ 必要时重新规划
```

目前各个零件存在，但没有模块把它们连成这一套完整流程。

---

## 2. 黄色地面闪光识别

暂定名称：

```text
YellowTrailRecognizer
YellowTrailFollower
```

这是目前最明确的新增功能。

需要实现：

* 黄色闪光候选区域检测；
* 排除技能特效、火焰、灯光和拾取光柱；
* 多帧闪烁特征；
* 识别多个连续黄色点；
* 选择距离人物最近的前方点；
* 根据点序列判断转弯；
* 当前点消失后寻找下一个点；
* 与屏幕任务标记共同校验方向。

现有“追踪图标”功能不能直接等同于黄色地面闪光识别。

---

## 3. 剧情战斗判定器

暂定名称：

```text
StoryCombatGate
```

现成的 AutoFight 只解决“怎么打”，没有解决“什么时候应该打”。

需要综合：

* 当前任务描述；
* 是否已进入目标区域；
* 任务距离；
* 是否刚结束剧情；
* 是否出现任务战斗目标；
* 战斗开始前后的任务描述变化；
* `process.json` 是否明确写了 `战斗`；
* 是否只是路边野怪。

你之前的“战斗识别+剧情+自动拾取预览版”可以提供战斗画面信号，但它仍然不能独立判断是不是剧情战斗。

该压缩包目前没有公开 GitHub 地址，因此暂时无法像其他项目一样附源码链接。

---

## 4. 完整运动进度判断

PR #2978 已经有坐标级卡死检测，BetterGI 也有攀爬和滑翔状态，但任务跟随模式仍需增加：

```text
是否真的发生位移
任务距离是否下降
目标图标是否朝正确方向移动
角色是否被攻击硬直
是否连续撞墙
是否走错楼层
是否在原地绕圈
```

这一部分应当复用 PR #2978 的：

* `StuckDetector`；
* `TrapEscaper`；
* `PathingMovementController`；

而不是重新写一套完全独立的脱困器。

---

## 5. 任务目标解析器

暂定名称：

```text
QuestTargetResolver
```

需要解决：

* 当前任务目标的大地图近似坐标；
* 地表还是地下；
* 任务目标附近最近的路网节点；
* 没有准确坐标时，以地图中心或任务图标作为近似目标；
* 路网规划终点应该停在哪里；
* 什么时候从路网导航切换到任务引导跟随。

现有 `AutoTrackTask` 只有“打开任务后让目标居中，再选附近传送点”的早期办法，还不是稳定的通用目标坐标获取器。

---

## 6. 连续任务状态机

暂定名称：

```text
QuestAutomationController
```

负责：

```text
任务识别
→ 导航
→ 到达确认
→ 对话/交互/剧情战斗
→ 等待任务变化
→ 获取下一个目标
→ 继续执行
```

自动剧情加载器已经有流程编排，但还缺少一个能够在“没有对应 process 任务块”时自行决策的通用状态机。

---

# 四、最终复用结论

## 完全复用，不重新开发

* [自动剧情加载器](https://github.com/LX666-666/BadGI-JsScript/tree/main/%E8%87%AA%E5%8A%A8%E5%89%A7%E6%83%85%E5%8A%A0%E8%BD%BD%E5%99%A8)
* [自动剧情录制器](https://github.com/duoduo1232/AutoStoryTranscribe)
* [AutoSkip](https://github.com/babalae/better-genshin-impact/blob/main/BetterGenshinImpact/GameTask/AutoSkip/AutoSkipTrigger.cs)
* [AutoFight](https://github.com/babalae/better-genshin-impact/blob/main/BetterGenshinImpact/GameTask/AutoFight/AutoFightTask.cs)
* [AutoPick](https://github.com/babalae/better-genshin-impact/blob/main/BetterGenshinImpact/GameTask/AutoPick/AutoPickTrigger.cs)
* [PathExecutor](https://github.com/babalae/better-genshin-impact/blob/main/BetterGenshinImpact/GameTask/AutoPathing/PathExecutor.cs)
* [界面状态识别](https://github.com/babalae/better-genshin-impact/blob/main/BetterGenshinImpact/GameTask/Common/BgiVision/BvStatus.cs)

## 以改造和整合为主

* [PR #2978 路网与运动框架](https://github.com/babalae/better-genshin-impact/pull/2978)
* [AutoTrackTask 任务标记跟随](https://github.com/babalae/better-genshin-impact/blob/main/BetterGenshinImpact/GameTask/AutoSkip/AutoTrackTask.cs)
* 你现有的战斗画面识别预览脚本。

## 真正需要新开发

* 黄色地面闪光识别；
* 路网终点与 AutoTrackTask 自动交接；
* 剧情战斗与路边野怪区分；
* 通用任务目标解析；
* 连续任务自动状态机；
* 跟随任务标记时的运动进度判断；
* 模块间输入控制权管理；
* 完整检查点和失败恢复。

因此，项目并不是从零开发。主体工作可以压缩为：

> **复用现有流程系统、录制器、路网、路径执行、AutoSkip、AutoFight 和 AutoPick，重点开发混合导航控制器、黄色闪光跟随、剧情战斗判定和连续任务状态机。**
