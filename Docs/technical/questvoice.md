# 自动配音（QuestVoice 原神管线）

入口位于左侧「自动演奏」下方的「自动配音」，展开后包含「运行与日志」和「配置与映射」两个子页面。两个子页面均为 WPF 原生单列布局，共用同一单例服务与视图模型，切换页面不会停止或重建配音会话；借用 BetterGI 已绑定运行环境的已有帧，不创建另一套捕获器，不发送游戏输入，也不自动修改自动剧情或其他任务设置。

## 首次配置

1. 在启动页绑定原神并启用截图。按需手动关闭自动剧情的快速跳过，否则对白可能来不及稳定识别或合成。
2. 在「自动配音 → 配置与映射」选择 TTS 引擎。旧配置默认继续使用 GPT-SoVITS（`http://127.0.0.1:9880/`）；也可选火山翻译及中文预设音色。连接检测只检查可达性，成功不代表已经验证合成或后端空闲。
3. 在「OCR 设置」部署带 `character` 字典的 PP-OCRv6 Rec ONNX 模型（18708 字，输入 `x: N,3,48,W`）。默认路径为程序目录下 `User/QuestVoice/ocr/PP-OCRv6_rec_small.onnx`。模型不随此功能分发，也不能直接把其他 OCR 模型改名使用。
4. 使用 GPT-SoVITS 时展开「高级声线资源」，填写默认 NPC 的权重、参考音频和逐字原文。本地实例在本机校验文件；远程实例填写服务器已有路径。使用火山翻译无需这些资产，也无需账号、API Key 或本地 TTS 模型；用于识别游戏对白的 OCR 模型仍必需。
5. 点击「校验映射配置」或「运行与日志」页的「校验配置」，检查文件及路由诊断，再在「运行与日志」页启动自动配音。静态校验不代替实际推理、合成与声卡验收。

专用 OCR 使用 DirectML，沿用 QuestVoice 的预处理、模型和置信度接受规则。BetterGI 当前进程若加载了不含 DirectML 的 CUDA/OpenVINO 等 ONNX 原生运行库，需要在全局硬件加速中选择内置 CPU / DirectML 并重启。自动配音只检查已加载运行库，不会替换全进程 DLL，也不改变全局 OCR 配置。

## 角色与旅行者

「打开配置与资源目录」或首次校验会按需部署小型场景目录、XML 和路由示例到 `User/QuestVoice`，已有文件不会覆盖。启用「角色声线路由」后，在「配置与映射 → 角色映射与声线配置」加载所选目录，可搜索、增加、编辑和删除角色及独立声线。示例中角色仅有身份，尚未绑定声线。

目录包含 `Speakers`（规范名称、Aliases、Corrections 和 VoiceId）与 `Voices`（独立声线 profile）。选择空或荧时，仅在缺少对应角色时补齐身份；二者的声线分别维护。`ProtagonistId` 也可引用已有其他角色，`PlayerNames` 一行一个游戏昵称；新版目录拒绝昵称抢占其他角色的姓名、别名或纠错名。`FallbackVoiceId` 指定默认 NPC，空值沿用默认引擎声线；`NarratorVoiceId` 为旁白声线。明确角色、别名和纠错优先于未知 NPC 兜底；已知角色无声线或资产不可用时按 `MissingVoicePolicy` 的 `Fallback` / `Fail` 处理。当前旅行者或仍被角色/旁白/兜底引用的声线不能直接删除。

管理编辑保留在草稿中，切换选中项或页面不会清空；点击「保存配置并备份」才写外置 JSON。原文件保留为 `.bak`，临时文件完成后原子替换，摘要冲突会拒绝覆盖并保留草稿；可导出草稿再重新加载核对。导入只加载草稿，保存才覆盖当前目标；跨目录导出会重新计算本地资产路径。放弃未保存输入需明确点击「放弃草稿并重新加载」。统一引擎/OCR设置仍沿用 BetterGI 自动保存。

旧 `Version: 1` 文件继续读取，管理器保存升级为 `Version: 2`。旧 GPT profile 未指定实例时继续沿用 `UseRemoteService`；新版声线可显式引用 `ProviderId`、`InstanceId`（`local` / `remote`）和 `ProviderVoiceId`。连接地址和超时仍由对应实例的引擎设置管理，目录不复制地址或凭据。普通引擎无需 GPT 权重/参考音频，多个角色可共享同一 profile。例如：

```json
{
  "Version": 2, "Game": "genshin", "ProtagonistId": "genshin.lumine", "PlayerNames": ["星海旅人"],
  "NarratorVoiceId": null, "FallbackVoiceId": "voice.npc", "MissingVoicePolicy": "Fallback",
  "Speakers": [
    { "Id": "genshin.lumine", "Names": ["荧"], "Aliases": [], "Corrections": [], "VoiceId": "voice.traveler" }
  ],
  "Voices": [
    { "Id": "voice.traveler", "Name": "旅行者", "ProviderId": "huoshan", "InstanceId": "local", "ProviderVoiceId": "zh_female_qingxin" },
    { "Id": "voice.npc", "Name": "NPC", "ProviderId": "huoshan", "InstanceId": "local", "ProviderVoiceId": "zh_male_zhubo" }
  ]
}
```

「试听选中声线」使用当前草稿及生产服务的同一互斥/取消/播放器，不要求先保存，也不需要游戏或 OCR。运行或试听时管理操作禁用；试听结束恢复可编辑。GPT 状态未知时，即使默认引擎为火山，也会显示手动恢复确认入口。试听完整接收后播放；正式连续对白按本句实际 Provider 的能力决定流式或完整播放。

本地实例的 JSON 资产路径相对于 JSON 所在目录解析；远程实例原样交给服务器解释，请优先使用服务器绝对路径。配置示例：

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

在「配置与映射 → 基础配置」中切换「流式配音（实验性）」。默认关闭，旧配置也保持关闭；请先停止配音再修改，下次启动生效。关闭时使用完整合成，开启时使用 api_v2 的按句分片模式 `streaming_mode=1`，固定 `parallel_infer=false`、`split_bucket=false`。需要后端支持单个PCM16 WAV头后连续返回PCM，不自动选择语义流式模式2/3。

开启后，只有有效格式、非静音样本且达到约200ms首段水位（短音频正常结束后可低于水位）才允许新句接替；响应头和WAV头不是就绪音频。后续PCM持续送入同一个WASAPI共享设备。旧句继续播放到新句可播；已开始播放的流也不会因为下一句进入识别而立即停止。视觉版本、对白周期、观察租约与迟到结果过滤仍按原连续对白规则执行。

HTTP接收使用最多32MiB的会话磁盘归档与播放解耦，不无限缓存内存、不因播放消费慢提前释放切权所有权。短暂缺料插零并在文件诊断中计数；持续缺料超过2秒停止本句输出，在途请求仍排空。只在正常EOF后校验和保存完整WAV；部分已播放音频不能撤回，断流不自动重合成拼接。流式失败时可先停止，关闭开关后恢复完整模式；后端未知时仍须手动恢复后端并确认。

流式接收和输出摘要只进入后台文件诊断，包含source标识、首PCM/就绪/EOF及缺料计数。就绪时间不是声卡实际发声时间。正式游戏中的首声、音质和帧时间仍需人工验收，不直接套用独立实验的延迟数字。

## 停止与恢复

完整模式的新对白先完成合成与 WAV 校验，再接替旧句；流式模式在首段有效PCM可播时接替。旧版本、重复文字和不支持的画面会被过滤。停止立即拒绝采样并取消待播内容、OCR 和播放；已发给 GPT-SoVITS 的 HTTP 请求沿用串行排空策略，合成等待默认 180 秒，可配置 1～600 秒，期间 UI 显示停止中。

短暂 Unknown、Rejected、文字几何不支持和超过 1500ms 的观察断档只让未播放结果失效，不停止已经播放的声音，也不清除本轮同文身份。宿主在同一帧正向识别主界面且排除对白 UI，连续至少 3 帧并持续 600ms 后，才以 `ConfirmedMainUiExit` 确认退出并推进对白周期。当前帧重新选中对白会否定退出；断档重置退出证据。连续有观察但场景一直不确定达到 30 秒时，以独立的 `UncertainSceneTimeout` 原因结束当前对白周期；没有帧回调时不会凭空确认退出，当前有限 WAV 可自然结束。用户停止、游戏窗口失效和宿主结束仍立即停止。

每轮配音延迟初始化并复用一个串行 DirectML OCR 会话，同时缓存模型哈希、字典和输入输出元数据。首批成功推理结束 profiling 并验证实际 DML 节点；热批次沿用该证据，不再逐批写 profile。每批独立 `RunOptions`；原生取消或错误返回后先释放不可信会话，下批按需重建。停止先排空在途调用再释放，模型/设备变化在下次启动生效。

连接超时或响应中断后不能确认后端是否仍在工作，需要先停止自动配音、手动重启后端，再点击「确认后端已重启」。该按钮是用户确认，不会发送远程重启指令。应用整体退出可以关闭本地 HTTP 连接，但不会关闭 GPT-SoVITS。

详细日志接入 BetterGI 主日志，「运行与日志」页面展示 6 阶段流程进度（对话界面检测 → OCR 识别 → 角色识别 → 声线路由 → TTS 合成 → 音频播放）、当前对白与实际路由声线以及最近最多 8 条运行反馈，并提供打开 BetterGI 主日志文件与日志目录的入口。每轮缓存位于 `User/QuestVoice/cache/<随机标识>`，停止排空后清理本轮音频和 OCR profile。窗口失效会停止配音；失焦、最小化和推理暂停沿用宿主取帧限制。

Debug 日志每 10 秒至多输出一次 `QuestVoice metrics`，停止排空后输出 `final metrics`；关闭 Debug 级别会关闭这些摘要。计数区分接收/过滤帧、Clone、BMP 编码、退出模板检查、场景检查、OCR 等待/批次/前处理、初始化、原生 Run 与 CTC；快照为累计次数/总毫秒/最大毫秒，不包含正文或资产路径。`Initialization` 是尝试次数；`NativeRun` 为原生调用墙钟（包括 CPU、EP 和同步），不能当纯 GPU kernel 时间；`Preprocess` 仅统计成功准备张量。现有 TTS 和播放事件保留合成耗时及停止原因。秒级 CPU 采样和组件基准不能代替游戏帧时间、GC 或鼠标实景验收。

### 悬浮摘要与文件诊断

悬浮日志仅显示真实生命周期、阶段变化、正式接受的OCR姓名/正文及简洁错误。周期OCR复查和已丢弃结果不显示为识别成功；以当前revision/epoch/source身份去重，新对白即使文本相同也允许再次显示。迟到旧合成/播放阶段不能覆盖新对白，停止期间仅保留实际停止故障通知。`QuestVoiceOverlayOptions`集中管理阶段与正文显示，当前测试版两项均开启，重要生命周期及错误不受它们影响。

`QuestVoiceLogKind`结构化属性在Sink边界分流，不解析日志字符串；`SceneSelected`完整Evidence改为Debug文件诊断，配音事件、性能摘要、ROI/签名与错误链继续进入原主日志。仅供悬浮显示的摘要不在文件额外复制OCR正文。文件级别与滚动保留策略不依赖悬浮开关，也未关闭其他模块的Information日志。关闭Debug会停止详细场景证据记录，需要诊断时保持默认Debug。

正文仅在显示时按Unicode字符限长（正文800、姓名64），合并换行并去除控制字符；原OCR、路由和合成输入不变。不要在摘要附加请求参数、认证信息或堆栈，也不要额外上传或长期复制对白日志。

悬浮通道最多128条待显示事件及一个不超过16条的在途批次，超载时丢弃较旧的UI通知，独立文件日志不丢失这些事件。后台格式化、异步Dispatcher投递，生产线程不等待UI。文档维护合并为一个待办，批量保留最新200个顶层Inline及最多10000字符，超长内容保留尾部；超限退为纯文本时可能丢失旧片段的颜色。关闭遮罩后取消维护、退订事件并解除旧渲染目标。

## 代码边界

### 多 TTS Provider

`ITtsProvider` 在既有 `ITtsEngine` 上补充身份/能力、配置校验、声音列表、只读探测和资源释放；`IStreamingTtsEngine` 仍是可选流式能力。`TtsProviderRegistry` 集中注册描述与构造函数，每轮会话只创建一个 Provider。连续对白协调器和 WASAPI 播放器不包含火山协议代码。

火山翻译使用普通匿名 HTTPS 请求，完整接收 JSON/base64/MP3，再通过现有 NAudio 解码为 PCM16 WAV，复用既有播放器。该端点是非公开客户端接口，当前能够匿名调用，但没有第三方 API 稳定性、可用性或限流承诺；失败时显示具体错误，不自动重试或伪装授权。仅发送台词文本及选中音色，不发送模型、参考音频或账号信息。预设语言为中文，不显示无效的语速、音高或流式选项。JSON 上限 24 MiB、压缩音频 16 MiB、解码输出 64 MiB；GPT 完整和流式音频上限均为 32 MiB。

本地和远程 GPT-SoVITS 是同一 Provider 的两个保存实例。`UseRemoteService` 选择实例，旧 `ServiceUrl` 和默认声线字段原样保留，远程使用独立 `Remote*` 字段。远程支持 HTTP/HTTPS 和反向代理子路径；路径由服务器解释，不检查客户端同名文件、不自动上传，也不实现不存在的上传 API。连接超时 1～120 秒，请求和合成超时 1～600 秒；不在 URL 内嵌账号、密码、查询参数或令牌。受保护服务可在用户管理的反向代理侧配置认证，本阶段未增加凭据管理。

配置由 BetterGI 统一保存，缺少 `ProviderId` 时自动采用 `gpt-sovits`，无需迁移或覆盖旧字段。引擎切换保留所有配置；旧 v1 目录在默认火山引擎下继续保持固定音色，管理器保存升级到 v2 后，启用角色路由即可为角色选择不同引擎/声音。运行或测试时设置不可编辑，修改在下次启动生效。GPT 的 Unknown 只由实际后端状态传播；在线引擎的普通网络失败不会要求重启 GPT。

「测试合成并播放」不要求截图或 OCR，使用当前默认声线、完整接收后播放，并与正式配音互斥。「取消测试」立即取消本地播放；GPT 已发请求仍排空。整体退出可关闭测试和正式会话的本地 HTTP 连接，不关闭远端服务。测试入口不代表正式流式或真实游戏已验收。

新增第三个普通 Provider：实现 `ITtsProvider`（返回已解码 PCM16 WAV 的 `TtsResult`），在注册表添加描述与创建函数，并为实际参数增加独立持久化字段及工厂快照。共通设置通过能力控制显隐；有新参数时添加对应 MVVM 模板即可，无需修改 OCR、游戏捕获、连续对白调度或播放器。角色管理已经使用 ProviderId、InstanceId 和专属 voice ID 引用声音；`RoutedTtsProvider` 按冻结实例复用 Provider，同实例不同声线保留安全切权状态。`IStreamingTtsEngine.CanStream` 仅决定当前请求是否走增量播放，Ready/EOF、GPT 排空与 Unknown 继续由原实现拥有。

`GameTask/QuestVoice/Core` 和 `Features` 来自 QuestVoice `1fc59d9c374a2c36180c2116be4397a0b8be719c` 的原神业务契约和算法。`QuestVoiceSessionController` / `QuestVoiceSessionFactory` / `QuestVoiceSession` 管理独立单轮所有权，`Service/QuestVoiceService` 接入宿主运行环境与已有帧，ViewModel 只异步投递状态和执行命令。没有引入 QuestVoice WinUI 宿主、第二套 WGC、星铁业务或七页面导航。

自动验证可使用组件测试与独立离屏 XAML harness；遵守仓库 AGENTS.md，不自动启动 BetterGI 主程序。实际游戏连续对白、后端、声音、DPI 与其他任务共存需要人工验收。

### 扩展语音引擎

在原「配置与映射」页面选择引擎。声音ID与显示名分开保存；手输ID后也可以给角色创建独立profile。新增引擎参数位于`ExtraProviders`，与原GPT/火山字段隔离；本地/远程URL分别保存，profile引用`local`或`remote`，为空时沿用该引擎的当前实例。两实例共享该引擎的语言、模型、密钥与调节参数。配置由宿主自动保存，运行/试听使用冻结快照。

|引擎|设置与声音|协议/限制|
|---|---|---|
|Windows 系统语音|刷新已安装SAPI声音，保存完整token ID；速度0.5～2|无需网络、账号、Python或额外模型。专用STA内存生成22050Hz PCM16 WAV；不调用系统直出扬声器。语速等级对应实际倍率由声音引擎决定。|
|VOICEVOX|引擎URL（默认50021）、刷新speaker/style列表、style ID、速度0.5～2、音高-0.15～0.15|POST audio_query后保留查询对象并POST synthesis，完整WAV。自行提供合法引擎和声音库，遵守VOICEVOX署名和各声音条款。|
|vits-simple-api|默认23456、可选X-API-KEY、语言auto/zh/ja/en、速度0.5～2；声音如VITS:0或BERT-VITS2:0|当前`/voice/speakers`及`/voice/vits`、`/voice/bert-vits2`，POST JSON，强制streaming=false/format=wav。其他模型类型不发布为可用声音。不下载模型或安装服务。|
|OpenAI兼容语音API|Base URL含服务前缀（例如`https://api.openai.com/v1/`）、可选Bearer Key、model、voice、速度0.25～4、wav/mp3/pcm|POST audio/speech。pcm按协议解释为24kHz单声道小端PCM16；其他格式不显示为可用。内置声音仅为建议，不是远程发现；支持兼容服务自定义voice。连接检测只做格式检查，实际认证与推理用试听验证。|
|Google Translate TTS|预设语言为voice ID；正常1或慢速0.5|非Cloud承诺接口，100字符分段；每段MP3独立解码再顺序合并PCM，不拼接WAV首部。需要网络可达，不影响宿主启动，不绕过服务拒绝。|

服务声音列表只在用户点击刷新时联网（VOICEVOX/VITS）；普通页面切换不发送发现请求。地址/实例/选中profile改变后，迟到列表或错误不得覆盖当前选择。SAPI和静态声音建议可离线枚举。

SAPI枚举与合成通过进程内可取消的串行入口访问COM，再在专用STA完成操作与清理；不同实例不会并发释放共享系统token的RCW。排队取消不干扰当前持有者，合成总期限包含排队等待。原生COM调用若自身永久挂起，不能由托管取消强制终止；不会把这一情况声称为可杀死后端。

新增引擎均完整接收和解码后播放，`Streaming=false`；HTTP分块不等于实际增量播放。单个HTTP请求和整个合成都有期限，正文读取、取消及大小限制贯穿处理。普通网络/认证/格式错误可以重试，不改变GPT的排空与Unknown语义。Key只进入请求头，错误不回显服务响应；密码输入遮罩，Key仍由宿主配置文件保存在本机，勿分享该配置。URL不接受内嵌凭据或查询令牌。

Edge当前Luna实现依赖Sec-MS-GEC v2客户端令牌，BCUT没有确立第三方开放调用前提，VoiceRoid/A.I.VOICE/NeoSpeech需要独立软件和许可；本阶段未注册这些空引擎，也不模拟访问控制或下载商业资源。Tolk直接输出到读屏软件且不返回音频，与当前播放器所有权不符；自定义Python及当前不存在的Chrome适配同样不引入。

实现为独立C#协议适配，参考当前LunaTranslator公开源码和服务官方接口；未嵌入/运行Python、LunaTranslator.exe或商业DLL。服务与模型的许可仍分别适用。真实服务缺失时的隔离HTTP测试只证明协议行为，不代表真实模型推理通过。官方Speech API模型有生命周期限制，使用OpenAI官方服务时需按其当前弃用公告选择可用模型；兼容服务的模型名称由服务端决定。

## Phase 8 配置页面与自动保存

自动配音导航分为「运行与日志」「配置与映射」「GPT-SoVITS 配置」「其他引擎设置」。运行页面继续使用原单例服务；切页不重建会话。配置页只有公共 OCR、默认引擎、旅行者昵称与声线、未知 NPC 和旁白声线以及折叠的已知角色映射。姓名栏存在但姓名识别失败仍沿用原 NPC 回退，不能仅凭空姓名判定旁白。

先在 GPT 声线库新建并补齐资源，或在其他引擎中选好声音、填写显示名称并点击「加入通用声线库」，再到配置页绑定。三个默认规则共用友好的 Profile 下拉，可引用不同引擎或同一个 Profile。名称可自由编辑，内部身份不变。删除有引用的声线会列出旅行者/角色/NPC/旁白引用并阻止删除；资源缺失保留绑定并显示诊断，不会换成第一项。其他引擎页的选择只改变编辑对象，全局默认引擎在配置页选择。

七个引擎拥有独立设置模型；六种其他引擎通过类型模板展示独立 UserControl。Provider、HTTP 协议、OCR、连续对白与唯一 WASAPI 播放器继续复用。GPT 当前协议可选完整或现有按句流式；参考语言及文本语言固定 zh、切分固定 cut5。未提供生产代码未支持的语速或流式模式。

普通设置继续通过 AllConfig 的嵌套变更追踪与 ConfigService 500ms 批写保存。URL 输入先进入设置草稿，合法 HTTP/HTTPS 根地址才提交，不随输入访问网络。动态表单与路由文本即时进入内存，避免切换声线/引擎时丢失延迟绑定；路由和旧默认 GPT 资源成套草稿在停顿 750ms 后校验。未完成的 Profile 或新输入的缺失本机资源不会覆盖有效目录，原已保存的暂不可用资源仍可保留。远程路径不作本机存在性校验。

角色目录仍是原 v2 JSON：单一草稿与串行保存门锁、编辑修订号、原 SHA 冲突检查、原子替换及 .bak 备份。保存期间允许继续输入，成功不替换编辑对象；较新的输入安排下一次保存。失败/冲突保留草稿并显示状态，需修正、导出或明确放弃重新加载。路径变化前后均检查目标，旧草稿不会写到新路径。导入/导出、删除和放弃是显式操作，沿用主题确认或文件对话框。

正常关窗及托盘退出先等待文本提交并排空有效草稿；失败会保留窗口供修正。重复关闭请求共享同一排空，仍逐次取消关闭。强制结束进程、断电或操作系统强制退出不能保证保存内存中的未完成输入。旧默认资源草稿有明确撤销入口；撤销后其旧定时器不能再提交。

配置自动保存不代表热切换。运行启动时冻结 Provider、实例及 Profile；修改后下次启动配音生效。路由编辑继续遵守原运行/试听冻结守卫；其他引擎普通参数可编辑但不会修改在途请求。试听由原服务互斥执行，GPT 试听仍完整接收后播放，不能把它当流式试听验收。

旧 GPT/火山字段、ExtraProviders 和 v1/v2 目录保持兼容。API Key 继续用宿主 PasswordBox 遮罩并随本机配置保存，本阶段没有加密迁移；配置文件仍含本机明文密钥，不应分享。错误反馈不包含认证头或服务响应正文。