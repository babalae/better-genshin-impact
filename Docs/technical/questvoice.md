# 自动配音（QuestVoice 原神管线）

入口位于左侧「自动演奏」下方的「自动配音」。这是 WPF 原生页面，借用 BetterGI 已绑定运行环境的已有帧，不创建另一套捕获器，不发送游戏输入，也不自动修改自动剧情或其他任务设置。

## 首次配置

1. 在启动页绑定原神并启用截图。按需手动关闭自动剧情的快速跳过，否则对白可能来不及稳定识别或合成。
2. 在自动配音页填写 GPT-SoVITS api_v2 地址（默认 `http://127.0.0.1:9880/`）。「检测后端 HTTP」只探测 `/docs`，成功不代表合成或权重有效。
3. 部署带 `character` 字典的 PP-OCRv6 Rec ONNX 模型（18708 字，输入 `x: N,3,48,W`）。默认路径为程序目录下 `User/QuestVoice/ocr/PP-OCRv6_rec_small.onnx`。模型不随此功能分发，也不能直接把其他 OCR 模型改名使用。
4. 填写默认 NPC 的 GPT/SoVITS 权重、参考音频和逐字原文。路径相对于程序目录，亦可指定用户选择的绝对路径。当前协议在本机校验这些文件，并把完整路径传给后端；后端必须能够访问同一路径。
5. 点击「校验配置」，检查文件及路由诊断，再启动配音。静态校验不代替实际推理、合成与声卡验收。

专用 OCR 使用 DirectML，沿用 QuestVoice 的预处理、模型和置信度接受规则。BetterGI 当前进程若加载了不含 DirectML 的 CUDA/OpenVINO 等 ONNX 原生运行库，需要在全局硬件加速中选择内置 CPU / DirectML 并重启。自动配音只检查已加载运行库，不会替换全进程 DLL，也不改变全局 OCR 配置。

## 角色与旅行者

「打开配置与资源目录」或首次校验会按需部署小型场景目录、XML 和路由示例到 `User/QuestVoice`，已有文件不会覆盖。启用「角色声线路由」后，编辑所选 JSON。示例中角色仅有身份，尚未绑定模型。

目录包含 `Speakers`（规范名称、Aliases、Corrections 和 VoiceId）与 `Voices`（声线资产）。`ProtagonistId` 可填写 `genshin.aether` 或 `genshin.lumine`，并在 `PlayerNames` 添加游戏昵称；对应角色需在 `Speakers` 中绑定真实 `VoiceId`。`FallbackVoiceId` 指定默认 NPC，空值沿用页面默认声线；`NarratorVoiceId` 为旁白声线。未绑定或资产不可用角色按 `MissingVoicePolicy` 的 `Fallback` / `Fail` 处理。

JSON 内的资产路径相对于 JSON 所在目录解析。配置示例：

```json
{
  "Version": 1,
  "Game": "genshin",
  "ProtagonistId": "genshin.lumine",
  "PlayerNames": ["你的游戏昵称"],
  "NarratorVoiceId": null,
  "FallbackVoiceId": null,
  "MissingVoicePolicy": "Fallback",
  "Speakers": [
    { "Id": "genshin.lumine", "Names": ["荧"], "Aliases": [], "Corrections": [], "VoiceId": "voice.lumine" }
  ],
  "Voices": [
    { "Id": "voice.lumine", "Name": "荧", "GptWeightsPath": "voices/lumine.ckpt", "SovitsWeightsPath": "voices/lumine.pth", "ReferenceAudioPath": "voices/lumine.wav", "PromptText": "请替换为参考音频的逐字原文" }
  ]
}
```

页面设置由 BetterGI 统一持久化；页面输入与外置 JSON 都在下次启动配音时生效，运行中不热切换声线。页面导航不会停止会话。

## 流式配音（实验性）

在「GPT-SoVITS 与音频」中切换「流式配音（实验性）」。默认关闭，旧配置也保持关闭；请先停止配音再修改，下次启动生效。关闭时使用完整合成，开启时使用 api_v2 的按句分片模式 `streaming_mode=1`，固定 `parallel_infer=false`、`split_bucket=false`。需要后端支持单个PCM16 WAV头后连续返回PCM，不自动选择语义流式模式2/3。

开启后，只有有效格式、非静音样本且达到约200ms首段水位（短音频正常结束后可低于水位）才允许新句接替；响应头和WAV头不是就绪音频。后续PCM持续送入同一个WASAPI共享设备。旧句继续播放到新句可播；已开始播放的流也不会因为下一句进入识别而立即停止。视觉版本、对白周期、观察租约与迟到结果过滤仍按原连续对白规则执行。

HTTP接收使用最多32MiB的会话磁盘归档与播放解耦，不无限缓存内存、不因播放消费慢提前释放切权所有权。短暂缺料插零并在文件诊断中计数；持续缺料超过2秒停止本句输出，在途请求仍排空。只在正常EOF后校验和保存完整WAV；部分已播放音频不能撤回，断流不自动重合成拼接。流式失败时可先停止，关闭开关后恢复完整模式；后端未知时仍须手动恢复后端并确认。

流式接收和输出摘要只进入后台文件诊断，包含source标识、首PCM/就绪/EOF及缺料计数。就绪时间不是声卡实际发声时间。正式游戏中的首声、音质和帧时间仍需人工验收，不直接套用独立实验的延迟数字。

## 停止与恢复

完整模式的新对白先完成合成与 WAV 校验，再接替旧句；流式模式在首段有效PCM可播时接替。旧版本、重复文字和不支持的画面会被过滤。停止立即拒绝采样并取消待播内容、OCR 和播放；已发给 GPT-SoVITS 的 HTTP 请求沿用串行排空策略，最长合成等待为 180 秒，期间 UI 显示停止中。

短暂 Unknown、Rejected、文字几何不支持和超过 1500ms 的观察断档只让未播放结果失效，不停止已经播放的声音，也不清除本轮同文身份。宿主在同一帧正向识别主界面且排除对白 UI，连续至少 3 帧并持续 600ms 后，才以 `ConfirmedMainUiExit` 确认退出并推进对白周期。当前帧重新选中对白会否定退出；断档重置退出证据。连续有观察但场景一直不确定达到 30 秒时，以独立的 `UncertainSceneTimeout` 原因结束当前对白周期；没有帧回调时不会凭空确认退出，当前有限 WAV 可自然结束。用户停止、游戏窗口失效和宿主结束仍立即停止。

每轮配音延迟初始化并复用一个串行 DirectML OCR 会话，同时缓存模型哈希、字典和输入输出元数据。首批成功推理结束 profiling 并验证实际 DML 节点；热批次沿用该证据，不再逐批写 profile。每批独立 `RunOptions`；原生取消或错误返回后先释放不可信会话，下批按需重建。停止先排空在途调用再释放，模型/设备变化在下次启动生效。

连接超时或响应中断后不能确认后端是否仍在工作，需要先停止自动配音、手动重启后端，再点击「确认后端已重启」。该按钮是用户确认，不会发送远程重启指令。应用整体退出可以关闭本地 HTTP 连接，但不会关闭 GPT-SoVITS。

详细日志接入 BetterGI 主日志，页面保留有限诊断。每轮缓存位于 `User/QuestVoice/cache/<随机标识>`，停止排空后清理本轮音频和 OCR profile。窗口失效会停止配音；失焦、最小化和推理暂停沿用宿主取帧限制。

Debug 日志每 10 秒至多输出一次 `QuestVoice metrics`，停止排空后输出 `final metrics`；关闭 Debug 级别会关闭这些摘要。计数区分接收/过滤帧、Clone、BMP 编码、退出模板检查、场景检查、OCR 等待/批次/前处理、初始化、原生 Run 与 CTC；快照为累计次数/总毫秒/最大毫秒，不包含正文或资产路径。`Initialization` 是尝试次数；`NativeRun` 为原生调用墙钟（包括 CPU、EP 和同步），不能当纯 GPU kernel 时间；`Preprocess` 仅统计成功准备张量。现有 TTS 和播放事件保留合成耗时及停止原因。秒级 CPU 采样和组件基准不能代替游戏帧时间、GC 或鼠标实景验收。

### 悬浮摘要与文件诊断

悬浮日志仅显示真实生命周期、阶段变化、正式接受的OCR姓名/正文及简洁错误。周期OCR复查和已丢弃结果不显示为识别成功；以当前revision/epoch/source身份去重，新对白即使文本相同也允许再次显示。迟到旧合成/播放阶段不能覆盖新对白，停止期间仅保留实际停止故障通知。`QuestVoiceOverlayOptions`集中管理阶段与正文显示，当前测试版两项均开启，重要生命周期及错误不受它们影响。

`QuestVoiceLogKind`结构化属性在Sink边界分流，不解析日志字符串；`SceneSelected`完整Evidence改为Debug文件诊断，配音事件、性能摘要、ROI/签名与错误链继续进入原主日志。仅供悬浮显示的摘要不在文件额外复制OCR正文。文件级别与滚动保留策略不依赖悬浮开关，也未关闭其他模块的Information日志。关闭Debug会停止详细场景证据记录，需要诊断时保持默认Debug。

正文仅在显示时按Unicode字符限长（正文800、姓名64），合并换行并去除控制字符；原OCR、路由和合成输入不变。不要在摘要附加请求参数、认证信息或堆栈，也不要额外上传或长期复制对白日志。

悬浮通道最多128条待显示事件及一个不超过16条的在途批次，超载时丢弃较旧的UI通知，独立文件日志不丢失这些事件。后台格式化、异步Dispatcher投递，生产线程不等待UI。文档维护合并为一个待办，批量保留最新200个顶层Inline及最多10000字符，超长内容保留尾部；超限退为纯文本时可能丢失旧片段的颜色。关闭遮罩后取消维护、退订事件并解除旧渲染目标。

## 代码边界

`GameTask/QuestVoice/Core` 和 `Features` 来自 QuestVoice `1fc59d9c374a2c36180c2116be4397a0b8be719c` 的原神业务契约和算法。`QuestVoiceSessionController` / `QuestVoiceSessionFactory` / `QuestVoiceSession` 管理独立单轮所有权，`Service/QuestVoiceService` 接入宿主运行环境与已有帧，ViewModel 只异步投递状态和执行命令。没有引入 QuestVoice WinUI 宿主、第二套 WGC、星铁业务或七页面导航。

自动验证可使用组件测试与独立离屏 XAML harness；遵守仓库 AGENTS.md，不自动启动 BetterGI 主程序。实际游戏连续对白、后端、声音、DPI 与其他任务共存需要人工验收。
