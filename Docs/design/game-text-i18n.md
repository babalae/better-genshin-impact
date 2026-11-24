# 游戏文本多语言（GameText）改造设计

## 1. 背景

BetterGI 目前有两套多语言机制，分别服务两个**互相独立**的维度：

| | UI 文案 | 游戏内识别文本 |
|---|---|---|
| 用途 | 给用户看的界面文字 | 与 OCR 结果做匹配的目标文本，必须与游戏客户端显示完全一致 |
| 语言来源 | `OtherConfig.UiCultureInfoName` | `OtherConfig.GameCultureInfoName` |
| 实现 | `I18nService` + `{i18n:T 简体原文}` | `.resx` + `IStringLocalizer<T>` + `CultureHelper.WithCultureGet` |
| 资源 | `User/I18n/{en,it,ja,ru}.json`，key 即简体原文；繁体由 OpenCC 运行时转换 | 10 组 `.resx`，每组 `en/fr/it/zh-Hans/zh-Hant`，key 同样是简体原文 |

界面语言和游戏语言可以不同（中文界面 + 英文客户端很常见），游戏文本又要求逐字精确（OpenCC 转换出的用词不一定与游戏一致），因此**两个维度应当保留**。但游戏端的实现存在以下问题。

### 1.1 游戏语言可选到不支持的语言，识别静默失效

`CommonSettingsPage.xaml` 中游戏语言下拉框和 UI 语言共用 `LanguageDict`（`zh-Hans, zh-Hant, en, ja, ru, it`），而 resx 只有 `en/fr/it/zh-Hans/zh-Hant`，且没有不带语言后缀的默认 resx。

选择 `ja`/`ru` 后，`IStringLocalizer` 找不到资源，直接返回简体中文 key（如 `"复苏"`），用它去匹配日文/俄文 OCR 结果，复苏、领奖、秘境、传送等判断全部失败，且没有任何日志。反过来，`fr` 有完整 resx，却不在下拉列表中。

### 1.2 取值方式繁琐，并且会临时修改线程 Culture

每个调用点都要重复三步（共约 45 处）：

```csharp
CultureInfo cultureInfo = new CultureInfo(TaskContext.Instance().Config.OtherConfig.GameCultureInfoName);
IStringLocalizer stringLocalizer = App.GetService<IStringLocalizer<BvResxHelper>>() ?? throw new Exception();
string revival = stringLocalizer.WithCultureGet(cultureInfo, "复苏");
```

`IStringLocalizer` 不支持传入 culture，`CultureHelper.Use` 只能临时改写 `CultureInfo.CurrentCulture/CurrentUICulture` 再改回来。这是一个隐式的线程状态副作用。同时，`I18nService.ChangeLanguage` 也会写 `DefaultThreadCurrentUICulture`，两套机制在 Culture 这一点上互相耦合。

此外还有一些零散的形态：
- `BvResxHelper` 是空的抽象类，只为给静态类 `Bv` 充当 `IStringLocalizer<T>` 的泛型参数。
- `BaseTaskParam<T>`、钓鱼 `Behaviours` 等把 `IStringLocalizer` 一路作为构造参数传下去。

### 1.3 用简体原文做 key 不适合游戏文本

UI 端用简体原文做 key 是合理的：简体就是 UI 的源语言，缺译文时显示中文原文，用户仍然能看懂。游戏端不同：

- **简体只是众多客户端语言之一。** 简体客户端的值也不总是等于原文，例如 `挑战达成` 的简体值是正则 `(挑战|达成)`，`自动退出` 是 `(自动|退出)`。key 和值长得一样却含义不同，容易改错。
- **值经常是 OCR 匹配模式，不是译文。** 例如 `en` 中 `石化古树 → etrified`、`复苏 → revival`、`1星圣遗物 → 1-Star.*Artifacts`。把 key 当作“原文”来理解没有意义，key 只需要表达“在识别什么”。
- **回退成中文原文没有意义。** 非中文客户端拿到中文 key 必然匹配失败；与其掩盖成“像是有值”，不如明确记成缺项。
- **中文 key 在代码里无法做编译期检查。** 写错一个字只会静默回退，没有任何提示。

### 1.4 资源分散、缺项无人察觉

对现有 resx 的统计（10 组共 63 个 key，去重后 62 个；另有 6 个 key 在代码里使用但没有任何 resx 条目）：

- `AutoDomainTask`：`en/fr/zh-Hant` 缺 `快速编队`、`限时全部开放`、`限时开放`；`it` 缺 12 项。`匹配挑战` 在代码中使用，所有语言都没有条目。
- `TpTask`：`it` 缺 `至冬`；`CheckRewardsTask`：`it` 缺 `委托`、`每日委托奖励`。
- `GoToSereniteaPotTask` 调用了 `IStringLocalizer<GoToSereniteaPotTask>`（`阿圆`、`壶灵`、`<壶灵>`、`信任`、`洞天百宝`），但根本没有对应的 resx，任何游戏语言下都只能拿到简体原文。
- 跨组重复的 key 只有 `委托`（`CheckRewardsTask` 与 `ClaimEncounterPointsRewardsTask`），已有译文一致，但 `it` 在后者有 `Incarichi`，在前者却缺失。分组存放导致同一个词要维护多份。
- `View/Converters/CultureInfoNameToKVPConverter.*.resx` 只有一个 key `简体中文`，而 `CultureInfoNameToKVPConverter.GetDisplayName` 已改为硬编码 `switch`，这 5 个文件已无引用。

完整清单见附录 A 的“缺失语言”列。

## 2. 目标与非目标

**目标**
1. 游戏语言下拉框只列出游戏文本表实际支持的语言。
2. 新增 `GameTextService`，以 JSON 表替代 resx，key 改为英文语义标识，取值不再修改线程 Culture。
3. 删除 resx、`BvResxHelper`、`CultureHelper` 等不再需要的代码和资源。

**非目标**
- 不合并 UI 文案与游戏文本两个维度，UI 端继续使用简体原文作为 key。
- 不改变各任务“构造时取一次文本、运行中不变”的语义。运行中切换游戏语言仍需重新启动任务才生效，与现状一致。
- 不在本次补全缺失的译文。缺失项由测试基线列出，由熟悉对应客户端的人补充，避免凭猜测写入错误文本。

## 3. 设计

### 3.1 文件布局

```
BetterGenshinImpact/GameTask/I18n/
├── zh-Hans.json
├── zh-Hant.json
├── en.json
├── fr.json
└── it.json
```

- 和 `User/I18n` 一样以 `None` + `CopyToOutputDirectory` 方式复制到输出目录，便于游戏更新文本后直接热修 JSON，不必重新编译。
- 不放在 `User/I18n` 下，因为那里是 UI 文案和用户可编辑区域，混放会重新模糊两者的边界。
- 不新增 `BetterGenshinImpact/Assets` 目录（遵守 `AGENTS.md` 约定）。

### 3.2 key 规则

key 是**英文语义标识**，不是任何语言的原文：

- 格式为 `<模块>.<名称>`，可多级，每段 PascalCase，例如 `Domain.ChallengeCompleted`、`Artifact.Affix.CRITRate`。
- 模块按**游戏界面/功能**划分，不按 C# 类划分。多个任务共享的文本放在 `Common`、`Resin` 等公共模块，避免像 `委托` 那样重复维护。
- 名称描述“识别的是什么”，不描述匹配写法。例如 `Domain.RetryPrompt` 而不是 `Domain.StillChallengeDomainRegex`。
- 能复用游戏内英文术语的就直接复用（`Map.Area.Fontaine`、`Resin.Condensed`、`Artifact.Affix.CRITDMG`），方便对照英文客户端。
- 所有 key 在代码中以常量定义在 `GameTextKeys` 里，调用方不写字符串字面量：

```csharp
namespace BetterGenshinImpact.GameTask.I18n;

public static class GameTextKeys
{
    public static class Common
    {
        public const string Revive = "Common.Revive";
        public const string Commissions = "Common.Commissions";
        // ...
    }

    public static class Domain
    {
        public const string ChallengeCompleted = "Domain.ChallengeCompleted";
        public const string RetryPrompt = "Domain.RetryPrompt";
        // ...
    }
}
```

全部 68 个 key 与原中文 key 的对照见附录 A。

### 3.3 表格式与示例

每种语言一个扁平 JSON 对象：key 是 3.2 的语义标识，value 是该语言客户端中的实际文本或匹配模式（可能是正则，是否按正则使用由调用方决定，这与现状相同）。

**zh-Hans 也是一张完整的表**，不再有“缺项等于 key 本身”的约定；它是基准语言，代码里用到的每个 key 都必须在 zh-Hans 中存在。

迁移后预期生成的 JSON（节选，值全部来自现有 resx，未新增任何译文）：

`zh-Hans.json`
```json
{
  "Common.Revive": "复苏",
  "Common.Commissions": "委托",
  "Resin.Original": "原粹树脂",
  "Domain.ChallengeCompleted": "(挑战|达成)",
  "Domain.AutoLeaving": "(自动|退出)",
  "Domain.RetryPrompt": "是否仍要.*挑战.*秘境",
  "Domain.QuickSetup": "快速编队",
  "Artifact.Star1": "1星圣遗物",
  "Artifact.Affix.CRITRate": "暴击率",
  "Map.Area.Fontaine": "枫丹",
  "SereniteaPot.Ayuan": "阿圆"
}
```

`en.json`
```json
{
  "Common.Revive": "revival",
  "Common.Commissions": "Commissions",
  "Resin.Original": "Original Resin",
  "Domain.ChallengeCompleted": "(Challenge|Completed)",
  "Domain.AutoLeaving": "Leaving",
  "Domain.RetryPrompt": "still.*[Dd]omain",
  "Artifact.Star1": "1-Star.*Artifacts",
  "Artifact.Affix.CRITRate": "CRIT Rate",
  "Map.Area.Fontaine": "Fontaine"
}
```

`zh-Hant.json`
```json
{
  "Common.Revive": "復甦",
  "Common.Commissions": "委[託話]",
  "Resin.Original": "原粹樹脂",
  "Domain.ChallengeCompleted": "(挑戰|達成)",
  "Domain.RetryPrompt": "是否仍要.*挑戰.*秘境",
  "Artifact.Star1": "一星聖遺物",
  "Map.Area.Fontaine": "楓丹"
}
```

`it.json`
```json
{
  "Common.Revive": "Rianima",
  "Common.Commissions": "Incarichi",
  "Domain.ChallengeCompleted": "(Sfida|completata)",
  "Domain.QuickSetup": "Configurazione rapida",
  "Artifact.Star1": "Manufatti.*da.*1★?",
  "Map.Area.Fontaine": "Fontaine"
}
```

说明：
- `en.json` 中没有 `Domain.QuickSetup`、`SereniteaPot.Ayuan`，`it.json` 中没有 `Resin.Original`、`Domain.RetryPrompt`，都是原 resx 中就缺失的项，迁移时保持缺失，交给测试基线跟踪（见第 5 节）。
- `it.json` 中 `Common.Commissions` 的值来自 `ClaimEncounterPointsRewardsTask.it.resx`。合并后 `CheckRewardsTask` 也能用上，修复了 1.4 中的不一致。
- 繁体不做 OpenCC 回退，必须显式写表。繁体客户端用词要与游戏一致，自动转换的结果不可靠，缺项应当暴露出来，而不是被“看起来像对的”转换结果掩盖。

### 3.4 GameTextService

```csharp
namespace BetterGenshinImpact.GameTask.I18n;

/// <summary>
/// 游戏内识别文本服务。key 见 GameTextKeys，value 为对应游戏客户端语言的实际显示文本（可能是正则）。
/// 与界面语言（I18nService）相互独立，不读写任何线程 Culture。
/// </summary>
public sealed class GameTextService
{
    public const string BaseLanguage = "zh-Hans";

    /// <summary>游戏文本表支持的语言，也是游戏语言下拉框的数据源。</summary>
    public static IReadOnlyList<string> SupportedLanguages { get; } = ["zh-Hans", "zh-Hant", "en", "fr", "it"];

    public static GameTextService Instance { get; }

    /// <summary>按当前游戏语言（OtherConfig.GameCultureInfoName）取值。</summary>
    public string Get(string key);

    /// <summary>按指定游戏语言取值，主要供单元测试和需要显式语言的调用方使用。</summary>
    public string Get(string key, string language);

    public bool TryGet(string key, string language, [NotNullWhen(true)] out string? value);
}
```

取值规则（**缺少文本属于预期情况，静默回退，不记录日志**）：

| 情况 | 返回值 |
|---|---|
| 目标语言表中有该 key | 目标语言的值 |
| 目标语言缺项，zh-Hans 有 | zh-Hans 的值（与现状“回退简体原文”行为一致，避免改造引入回归） |
| zh-Hans 也没有 | key 本身（说明代码里用了未登记的 key，由单元测试拦住） |
| `language` 不在 `SupportedLanguages` 中 | 按 zh-Hans 处理 |
| 语言文件不存在 | 视为空表，按上述规则回退 |

缺项不在运行时报告，而是交给第 5 节的缺项基线测试跟踪。

实现要点：
- **加载**：每种语言首次使用时懒加载，解析为 `FrozenDictionary<string, string>`，缓存在 `ConcurrentDictionary<string, Lazy<FrozenDictionary<string, string>>>` 中。表加载后不可变，多线程读取无需加锁。多实例场景下每个进程各自加载，无共享状态。
- **文件损坏**：JSON 解析失败是唯一记录日志的情况（`Error`，每种语言只加载一次，因此最多一条），随后视为空表按上表回退，不抛异常，保证不会因为一个文件损坏而导致任务启动崩溃。
- **序列化**：使用 Newtonsoft.Json（与 `I18nService` 一致，遵守 `AGENTS.md` 约定）。
- **注入**：与 `I18nService` 相同，提供 `Instance` 单例，同时在 `App.xaml.cs` 中 `services.AddSingleton(GameTextService.Instance)`。静态类 `Bv` 等不便注入的地方直接用 `Instance`，可注入的任务类优先使用构造注入。

调用点改造前后对比：

```csharp
// 改造前
CultureInfo cultureInfo = new CultureInfo(TaskContext.Instance().Config.OtherConfig.GameCultureInfoName);
IStringLocalizer stringLocalizer = App.GetService<IStringLocalizer<BvResxHelper>>() ?? throw new Exception();
string revival = stringLocalizer.WithCultureGet(cultureInfo, "复苏");

// 改造后
string revival = GameTextService.Instance.Get(GameTextKeys.Common.Revive);
```

### 3.5 动态 key：中文业务值到 key 的映射

有三处调用方传入的是运行时的中文业务值，而不是固定字符串：

| 调用方 | 运行时值的来源 | 处理方式 |
|---|---|---|
| `TpTask.TrySwitchArea(areaName)` | 传送点数据的 `Country`、`MapTypes` 的 `Description`、`GoToSereniteaPotTask` 传入的 `"尘歌壶"`，均为中文地区名 | `TpTask` 内维护静态映射 `{"蒙德" → GameTextKeys.Map.Area.Mondstadt, ...}` |
| `AutoDomainTask.ResolveResinNamePattern(resinName)` | 用户配置中的树脂名，中文 | `AutoDomainTask` 内维护静态映射 `{"原粹树脂" → GameTextKeys.Resin.Original, ...}` |
| `ArtifactAffix.DefaultStrDic` | 词条类型枚举 → 中文名 | 改为枚举 → key，`ATK/ATKPercent` 都映射到 `Artifact.Affix.ATK`，其余同理 |

中文业务值本身（传送点数据、用户配置）不在本次改动范围内。映射表找不到时把原值直接当作匹配文本使用，与现状一致，不记录日志（例如 `TpTask` 中层岩巨渊、渊下宫等子地图名称本来就没有游戏文本）。

### 3.6 游戏语言下拉框（GameLanguageDict）

- `CommonSettingsPageViewModel` 新增 `GameLanguageDict`，数据来自 `GameTextService.SupportedLanguages`；`CommonSettingsPage.xaml` 中游戏语言下拉框的 `ItemsSource` 改为绑定它。UI 语言继续使用 `LanguageDict`。
- `CultureInfoNameToKVPConverter.GetDisplayName` 补充 `"fr" => "Français"`。
- **配置迁移**：启动时如果 `GameCultureInfoName` 不在 `SupportedLanguages` 中（历史上可选的 `ja`/`ru`），重置为 `zh-Hans`、保存配置，并打 `Warning` 日志提示用户手动确认游戏语言。这些用户在改造前识别本来就是失效的，重置后至少能让下拉框显示一个合法值。迁移逻辑放在 `App.xaml.cs` 中现有 UI 语言迁移代码的旁边。

### 3.7 删除清单

| 删除项 | 说明 |
|---|---|
| `GameTask/**/*.resx`（50 个） | 数据迁入 `GameTask/I18n/*.json` |
| `View/Converters/CultureInfoNameToKVPConverter.*.resx`（5 个） | 已无引用 |
| `GameTask/Common/BgiVision/BvResxHelper.cs` | 仅作为泛型占位 |
| `Helpers/CultureHelper.cs` | `WithCultureGet` / `Use` 只被游戏文本使用 |
| `BaseTaskParam<T>.StringLocalizer`，以及 `TpTaskParam`、`AutoFishingTaskParam`、`AutoArtifactSalvageTaskParam`、钓鱼 `Behaviours`/`AutoFishingTaskCatalog` 中的 `IStringLocalizer` 参数 | 改为直接使用 `GameTextService`。泛型参数 `T` 只为 `IStringLocalizer<T>` 存在，一并去掉，`BaseTaskParam<T>` 改为 `BaseTaskParam`；`GameCultureInfo` 保留（圣遗物分解还用它解析数字） |
| `App.xaml.cs` 中的 `services.AddLocalization()` | 已无消费者 |
| `Microsoft.Extensions.Localization` 包引用 | 删除前确认没有其他引用；`AutoEatParam.cs`、`CommonSettingsPageViewModel.cs` 中的 `using` 是残留，一并清理 |
| `Test/.../LocalizationFixture.cs` | 测试改为直接构造 `GameTextService` |

## 4. 迁移步骤

每一步结束时都要求 `dotnet build BetterGenshinImpact.sln -c Debug` 通过。

1. **GameLanguageDict（独立，可先合入）**
   - 新增 `GameLanguageDict` 并绑定到游戏语言下拉框，补 `fr` 显示名，加启动迁移。
   - 这一步先临时把支持列表写在 ViewModel 中，第 2 步再改为引用 `GameTextService.SupportedLanguages`。
2. **GameTextService 与数据迁移**
   - 写一次性导出脚本：读取 10 组 resx，按附录 A 的映射把中文 key 改写为语义 key，合并输出 `GameTask/I18n/{lang}.json`。导出时过滤 resx 模板注释中的示例项，检测跨组冲突（当前仅有 `委托`，无冲突）。zh-Hans 原本缺项的条目，以原中文 key 作为值补齐（与现状回退行为一致）。脚本不入库，导出结果入库。
   - 新增 `GameTextKeys`、`GameTextService`，在 csproj 中配置 JSON 复制到输出目录，在 `App.xaml.cs` 中注册。
   - 逐个替换 45 处 `WithCultureGet` 调用为 `GameTextKeys` 常量，并按 3.5 为动态 key 加映射。
3. **清理**
   - 按 3.7 的清单删除；更新单元测试；在 `AGENTS.md` 中新增一条约定：“UI 文案用 `{i18n:T 简体原文}` / `I18nService`；与游戏客户端文字做匹配的文本用 `GameTextService` + `GameTextKeys`，写入 `GameTask/I18n/*.json`，key 使用英文语义标识”。

## 5. 测试

- **key 登记**（新增）：反射枚举 `GameTextKeys` 的全部常量，每个都必须存在于 `zh-Hans.json`；每个 JSON 中都不能出现 `GameTextKeys` 之外的 key（防止拼写错误和遗留死 key）。
- **缺项基线**（新增）：对每种非基准语言，计算 `GameTextKeys` 中缺失的 key，与附录 A 的已知缺项基线比对。新增缺项时测试失败，补上译文后同步缩小基线，从而倒逼补译文，而不是静默回退。
- **格式**：每个 JSON 都能被 Newtonsoft.Json 解析为 `Dictionary<string, string>`，且没有空值。
- **行为**：`Get` 在目标语言缺项时返回 zh-Hans 值；不支持的语言按 zh-Hans 处理；`Domain.ChallengeCompleted` 在 zh-Hans 下返回 `(挑战|达成)`。
- **数据一致性**（迁移时一次性执行）：逐条比对 resx 原值与导出后的 JSON 值，必须完全相等。
- **迁移**：`AutoArtifactSalvageTaskTests`、`BehavioursTests` 改为使用 `GameTextService`。`PaddleOcrServiceTests` 中按语言给出的期望文本可以用来交叉核对导出后的 JSON。

## 6. 风险

| 风险 | 应对 |
|---|---|
| 导出脚本漏项或改动了文本（空白、转义） | 导出后逐条比对 resx 与 JSON 的值，全部相等才提交 |
| 动态 key 映射遗漏（新增地区、树脂名） | 映射找不到时按原值匹配，行为与现状一致；新增地区时需要同时在映射和 JSON 中登记 |
| 用户手动改坏 JSON | 解析失败视为空表并打 `Error` 日志，行为退化为“回退 zh-Hans 值”，与现在的缺项表现一致，不会崩溃 |
| 第三方脚本或插件依赖 `IStringLocalizer` | 仓库内没有发现外部可见的 API 暴露它；JS 脚本接口不涉及 resx |
| `ja`/`ru` 用户配置被重置 | 改造前这些用户的识别已经失效；重置时打日志提示用户确认 |

## 附录 A：key 对照表

由现有 resx 与代码调用点整理，共 68 个 key。“缺失语言”指迁移后该语言 JSON 中没有这个 key（运行时静默回退 zh-Hans 值），即第 5 节缺项基线的初始内容。

| 新 key | 原 key（简体原文） | 使用位置 | 缺失语言 |
|---|---|---|---|
| `Common.Revive` | `复苏` | BvStatus | — |
| `Common.Commissions` | `委托` | CheckRewardsTask, ClaimEncounterPointsRewardsTask | — |
| `Common.Skip` | `跳过` | AutoDomainTask | — |
| `Common.ClickAnywhereToClose` | `点击任意位置关闭` | AutoDomainTask | — |
| `Common.Use` | `使用` | AutoDomainTask.PressUseResin（多任务共享） | `it` |
| `Common.Cancel` | `取消` | AutoDomainTask | `it` |
| `Resin.Original` | `原粹树脂` | AutoDomainTask.PressUseResin | `it` |
| `Resin.Condensed` | `浓缩树脂` | AutoDomainTask.PressUseResin | `it` |
| `Resin.Transient` | `须臾树脂` | AutoDomainTask.PressUseResin | `it` |
| `Resin.Fragile` | `脆弱树脂` | AutoDomainTask.PressUseResin | `it` |
| `Resin.ReplenishOriginal` | `补充原粹树脂` | AutoDomainTask | `it` |
| `Resin.Insufficient` | `数量不足` | AutoDomainTask | `it` |
| `Domain.ChallengeCompleted` | `挑战达成` | AutoDomainTask | — |
| `Domain.AutoLeaving` | `自动退出` | AutoDomainTask | — |
| `Domain.LeyLineDisorder` | `地脉异常` | AutoDomainTask | — |
| `Domain.MatchingChallenge` | `匹配挑战` | AutoDomainTask | `zh-Hant`, `en`, `fr`, `it` |
| `Domain.QuickSetup` | `快速编队` | AutoDomainTask | `zh-Hant`, `en`, `fr` |
| `Domain.LimitedTimeFullyOpen` | `限时全部开放` | AutoDomainTask | `zh-Hant`, `en`, `fr` |
| `Domain.LimitedTimeOpen` | `限时开放` | AutoDomainTask | `zh-Hant`, `en`, `fr` |
| `Domain.SinglePlayerChallenge` | `单人挑战` | AutoDomainTask | `it` |
| `Domain.StartChallenge` | `开始挑战` | AutoDomainTask | `it` |
| `Domain.RetryPrompt` | `是否仍要.*挑战.*秘境` | AutoDomainTask | `it` |
| `Domain.PetrifiedTree` | `石化古树` | AutoDomainTask | `it` |
| `Artifact.QuickSelect` | `快速选择` | AutoArtifactSalvageTask | — |
| `Artifact.Star1` | `1星圣遗物` | AutoArtifactSalvageTask | — |
| `Artifact.Star2` | `2星圣遗物` | AutoArtifactSalvageTask | — |
| `Artifact.Star3` | `3星圣遗物` | AutoArtifactSalvageTask | — |
| `Artifact.Star4` | `4星圣遗物` | AutoArtifactSalvageTask | — |
| `Artifact.Affix.ATK` | `攻击力` | ArtifactAffix（ATK / ATKPercent） | — |
| `Artifact.Affix.DEF` | `防御力` | ArtifactAffix（DEF / DEFPercent） | — |
| `Artifact.Affix.HP` | `生命值` | ArtifactAffix（HP / HPPercent） | — |
| `Artifact.Affix.CRITRate` | `暴击率` | ArtifactAffix | — |
| `Artifact.Affix.CRITDMG` | `暴击伤害` | ArtifactAffix | — |
| `Artifact.Affix.ElementalMastery` | `元素精通` | ArtifactAffix | — |
| `Artifact.Affix.EnergyRecharge` | `元素充能效率` | ArtifactAffix | — |
| `Artifact.Affix.HealingBonus` | `治疗加成` | ArtifactAffix | — |
| `Artifact.Affix.PhysicalDMGBonus` | `物理伤害加成` | ArtifactAffix | — |
| `Artifact.Affix.PyroDMGBonus` | `火元素伤害加成` | ArtifactAffix | — |
| `Artifact.Affix.HydroDMGBonus` | `水元素伤害加成` | ArtifactAffix | — |
| `Artifact.Affix.DendroDMGBonus` | `草元素伤害加成` | ArtifactAffix | — |
| `Artifact.Affix.ElectroDMGBonus` | `雷元素伤害加成` | ArtifactAffix | — |
| `Artifact.Affix.AnemoDMGBonus` | `风元素伤害加成` | ArtifactAffix | — |
| `Artifact.Affix.CryoDMGBonus` | `冰元素伤害加成` | ArtifactAffix | — |
| `Artifact.Affix.GeoDMGBonus` | `岩元素伤害加成` | ArtifactAffix | — |
| `Fishing.Fish` | `钓鱼` | AutoFishing Behaviours | — |
| `Fishing.Bite` | `上钩` | AutoFishing Behaviours | — |
| `Map.Area.Mondstadt` | `蒙德` | TpTask | — |
| `Map.Area.Liyue` | `璃月` | TpTask | — |
| `Map.Area.Inazuma` | `稻妻` | TpTask | — |
| `Map.Area.Sumeru` | `须弥` | TpTask | — |
| `Map.Area.Fontaine` | `枫丹` | TpTask | — |
| `Map.Area.Natlan` | `纳塔` | TpTask | — |
| `Map.Area.NodKrai` | `挪德卡莱` | TpTask | — |
| `Map.Area.Snezhnaya` | `至冬` | TpTask | `it` |
| `Map.Area.SereniteaPot` | `尘歌壶` | TpTask | — |
| `Rewards.DailyClaimed` | `今日奖励已领取` | CheckRewardsTask | — |
| `Rewards.DailyCommissionRewards` | `每日委托奖励` | CheckRewardsTask | `it` |
| `BattlePass.OneClick` | `一键` | ClaimBattlePassRewardsTask | — |
| `BattlePass.Claim` | `领取` | ClaimBattlePassRewardsTask | — |
| `Guild.Katheryne` | `凯瑟琳` | GoToAdventurersGuildTask | — |
| `Guild.Daily` | `每日` | GoToAdventurersGuildTask | — |
| `Guild.Expedition` | `探索` | GoToAdventurersGuildTask | — |
| `Crafting.Craft` | `合成` | GoToCraftingBenchTask | — |
| `SereniteaPot.Ayuan` | `阿圆` | GoToSereniteaPotTask | `zh-Hant`, `en`, `fr`, `it` |
| `SereniteaPot.Tubby` | `壶灵` | GoToSereniteaPotTask | `zh-Hant`, `en`, `fr`, `it` |
| `SereniteaPot.TubbyTitle` | `<壶灵>` | GoToSereniteaPotTask | `zh-Hant`, `en`, `fr`, `it` |
| `SereniteaPot.Trust` | `信任` | GoToSereniteaPotTask | `zh-Hant`, `en`, `fr`, `it` |
| `SereniteaPot.RealmDepot` | `洞天百宝` | GoToSereniteaPotTask | `zh-Hant`, `en`, `fr`, `it` |
