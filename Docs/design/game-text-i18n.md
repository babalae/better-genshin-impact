# 游戏文本多语言（GameText）改造设计

## 1. 背景

BetterGI 目前有两套多语言机制，分别服务两个**互相独立**的维度：

| | UI 文案 | 游戏内识别文本 |
|---|---|---|
| 用途 | 给用户看的界面文字 | 与 OCR 结果做匹配的目标文本，必须与游戏客户端显示完全一致 |
| 语言来源 | `OtherConfig.UiCultureInfoName` | `OtherConfig.GameCultureInfoName` |
| 实现 | `I18nService` + `{i18n:T 简体原文}` | `.resx` + `IStringLocalizer<T>` + `CultureHelper.WithCultureGet` |
| 资源 | `User/I18n/{en,it,ja,ru}.json`，key 即简体原文；繁体由 OpenCC 运行时转换 | 10 组 `.resx`，每组 `en/fr/it/zh-Hans/zh-Hant` |

界面语言和游戏语言可以不同（中文界面 + 英文客户端很常见），游戏文本又要求逐字精确（OpenCC 转换出的用词不一定与游戏一致），因此**两个维度应当保留**。但游戏端的实现存在以下问题：

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

### 1.3 资源分散、缺项无人察觉

对现有 resx 的统计（简体原文作为 key，10 组共 63 个 key，去重后 62 个）：

| 资源组 | key 数 | 缺失 |
|---|---|---|
| AutoArtifactSalvageTask | 21 | — |
| AutoDomainTask | 20 | `en/fr/zh-Hant` 缺 `快速编队`、`限时全部开放`、`限时开放`；`it` 缺 12 项 |
| TpTask | 9 | `it` 缺 `至冬` |
| CheckRewardsTask | 3 | `it` 缺 `委托`、`每日委托奖励` |
| 其余 6 组 | 1–3 | — |

- 缺失项会静默回退为简体原文，在对应语言下必然匹配失败。
- `GoToSereniteaPotTask` 调用了 `IStringLocalizer<GoToSereniteaPotTask>`（`阿圆`、`壶灵`、`<壶灵>`、`信任`、`洞天百宝`），但根本没有对应的 resx，任何游戏语言下都只能拿到简体原文。迁移后这 5 个 key 同样作为已知缺项列出。
- 跨组重复的 key 只有 `委托`（`CheckRewardsTask` 与 `ClaimEncounterPointsRewardsTask`），且已有译文一致。`it` 在后者有 `Incarichi`，在前者却缺失。分组存放导致同一个词要维护多份。
- `zh-Hans.resx` 并不总是“值等于 key”：`AutoDomainTask` 中 `挑战达成 → (挑战|达成)`、`自动退出 → (自动|退出)`，值是正则。这一点决定了游戏端**不能**照搬 UI 端“简体不需要文件”的约定，见 3.2。
- `View/Converters/CultureInfoNameToKVPConverter.*.resx` 只有一个 key `简体中文`，而 `CultureInfoNameToKVPConverter.GetDisplayName` 已改为硬编码 `switch`，这 5 个文件已无引用。

## 2. 目标与非目标

**目标**
1. 游戏语言下拉框只列出游戏文本表实际支持的语言。
2. 新增 `GameTextService`，以 JSON 表替代 resx，取值不再修改线程 Culture，与 UI 端的 `I18nService` 形态对称。
3. 删除 resx、`BvResxHelper`、`CultureHelper` 等不再需要的代码和资源。

**非目标**
- 不合并 UI 文案与游戏文本两个维度。
- 不改变各任务“构造时取一次文本、运行中不变”的语义。运行中切换游戏语言仍需重新启动任务才生效，与现状一致。
- 不在本次补全缺失的译文。缺失项会被列出、打日志，由熟悉对应客户端的人补充，避免凭猜测写入错误文本。

## 3. 设计

### 3.1 文件布局

```
BetterGenshinImpact/GameTask/I18n/
├── zh-Hans.json   # 仅放“值 ≠ key”的覆盖项（例如正则）
├── zh-Hant.json
├── en.json
├── fr.json
└── it.json
```

- 和 `User/I18n` 一样以 `None` + `CopyToOutputDirectory` 方式复制到输出目录，便于游戏更新文本后直接热修 JSON，不必重新编译。
- 不放在 `User/I18n` 下，因为那里是 UI 文案和用户可编辑区域，混放会重新模糊两者的边界。
- 不新增 `BetterGenshinImpact/Assets` 目录（遵守 `AGENTS.md` 约定）。

### 3.2 表格式

和 UI 端对称：**一个扁平的 JSON 对象，key 是简体中文原文，value 是该语言客户端里的实际文本。**

```json
{
  "复苏": "Revive",
  "挑战达成": "(Challenge|Completed)",
  "是否仍要.*挑战.*秘境": "still.*challenge.*Domain"
}
```

规则：
- **扁平、不分组。** 10 组 resx 合并为一张表。现有数据中唯一的跨组重复 key `委托` 译文一致，可直接合并。如果将来同一简体原文在不同界面对应不同的游戏文本，用 `原文#上下文` 作为 key 区分（例如 `委托#奖励页`）。
- **value 可以是正则**，是否按正则使用由调用方决定（如 `AutoDomainTask` 对 `挑战达成` 用 `Regex.IsMatch`）。这一点与现状相同，只在文档和 JSON 注释约定里写明，不在格式层面区分。
- **zh-Hans.json 只放覆盖项**：key 不在表中时，返回 key 本身就是正确结果，这与 UI 端一致。区别在于游戏端存在 `挑战达成 → (挑战|达成)` 这类值不等于 key 的条目，因此文件必须存在。
- **zh-Hant 必须是显式表**：不做 OpenCC 回退。繁体客户端用词要与游戏一致，自动转换的结果不可靠，缺项应当暴露出来，而不是被“看起来像对的”转换结果掩盖。

### 3.3 GameTextService

```csharp
namespace BetterGenshinImpact.GameTask.I18n;

/// <summary>
/// 游戏内识别文本服务。key 为简体中文原文，value 为对应游戏客户端语言的实际显示文本（可能是正则）。
/// 与界面语言（I18nService）相互独立，不读写任何线程 Culture。
/// </summary>
public sealed class GameTextService
{
    public const string DefaultLanguage = "zh-Hans";

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

实现要点：
- **加载**：每种语言首次使用时懒加载，解析为 `FrozenDictionary<string, string>`，缓存在 `ConcurrentDictionary<string, Lazy<FrozenDictionary<string, string>>>` 中。表加载后不可变，多线程读取无需加锁。多实例场景下每个进程各自加载，无共享状态。
- **缺项**：key 不在表中时返回 key 本身，与现状行为一致，以免改造引入回归。非 `zh-Hans` 语言缺项时，按 `(language, key)` 去重打一次 `Warning` 日志，解决 1.3 中“缺项无人察觉”的问题。
- **不支持的语言**：`language` 不在 `SupportedLanguages` 中时，按 `zh-Hans` 处理并打一次 `Warning`。
- **文件损坏**：JSON 解析失败时记录 `Error` 日志并视为空表，不抛异常，保证不会因为一个文件损坏而导致任务启动崩溃。
- **序列化**：使用 Newtonsoft.Json（与 `I18nService` 一致，遵守 `AGENTS.md` 约定）。
- **注入**：与 `I18nService` 相同，提供 `Instance` 单例，同时在 `App.xaml.cs` 中 `services.AddSingleton(GameTextService.Instance)`。静态类 `Bv` 等不便注入的地方直接用 `Instance`，可注入的任务类优先使用构造注入。

调用点改造前后对比：

```csharp
// 改造前
CultureInfo cultureInfo = new CultureInfo(TaskContext.Instance().Config.OtherConfig.GameCultureInfoName);
IStringLocalizer stringLocalizer = App.GetService<IStringLocalizer<BvResxHelper>>() ?? throw new Exception();
string revival = stringLocalizer.WithCultureGet(cultureInfo, "复苏");

// 改造后
string revival = GameTextService.Instance.Get("复苏");
```

### 3.4 游戏语言下拉框（GameLanguageDict）

- `CommonSettingsPageViewModel` 新增 `GameLanguageDict`，数据来自 `GameTextService.SupportedLanguages`；`CommonSettingsPage.xaml` 中游戏语言下拉框的 `ItemsSource` 改为绑定它。UI 语言继续使用 `LanguageDict`。
- `CultureInfoNameToKVPConverter.GetDisplayName` 补充 `"fr" => "Français"`。
- **配置迁移**：启动时如果 `GameCultureInfoName` 不在 `SupportedLanguages` 中（历史上可选的 `ja`/`ru`），重置为 `zh-Hans`、保存配置，并打 `Warning` 日志提示用户手动确认游戏语言。这些用户在改造前识别本来就是失效的，重置后至少能让下拉框显示一个合法值。迁移逻辑放在 `App.xaml.cs` 中现有 UI 语言迁移代码的旁边。

### 3.5 删除清单

| 删除项 | 说明 |
|---|---|
| `GameTask/**/*.resx`（50 个） | 数据迁入 `GameTask/I18n/*.json` |
| `View/Converters/CultureInfoNameToKVPConverter.*.resx`（5 个） | 已无引用 |
| `GameTask/Common/BgiVision/BvResxHelper.cs` | 仅作为泛型占位 |
| `Helpers/CultureHelper.cs` | `WithCultureGet` / `Use` 只被游戏文本使用 |
| `BaseTaskParam<T>.StringLocalizer`，以及 `TpTaskParam`、`AutoFishingTaskParam`、`AutoArtifactSalvageTaskParam`、钓鱼 `Behaviours`/`AutoFishingTaskCatalog` 中的 `IStringLocalizer` 参数 | 改为直接使用 `GameTextService`；`GameCultureInfo` 改为 `GameLanguage`（string）或保留，按调用方需要决定 |
| `App.xaml.cs` 中的 `services.AddLocalization()` | 已无消费者 |
| `Microsoft.Extensions.Localization` 包引用 | 删除前确认没有其他引用；`AutoEatParam.cs`、`CommonSettingsPageViewModel.cs` 中的 `using` 是残留，一并清理 |
| `Test/.../LocalizationFixture.cs` | 测试改为直接构造 `GameTextService` |

## 4. 迁移步骤

每一步结束时都要求 `dotnet build BetterGenshinImpact.sln -c Debug` 通过。

1. **GameLanguageDict（独立，可先合入）**
   - 新增 `GameLanguageDict` 并绑定到游戏语言下拉框，补 `fr` 显示名，加启动迁移。
   - 这一步先临时把支持列表写在 ViewModel 中，第 2 步再改为引用 `GameTextService.SupportedLanguages`。
2. **GameTextService 与数据迁移**
   - 写一次性脚本，把 10 组 resx 合并导出为 `GameTask/I18n/{lang}.json`：过滤 resx 模板注释中的示例项，检测跨组冲突（当前仅有 `委托`，无冲突），`zh-Hans` 只导出值不等于 key 的条目。脚本不入库，导出结果入库。
   - 实现 `GameTextService`，在 csproj 中配置 JSON 复制到输出目录，在 `App.xaml.cs` 中注册。
   - 逐个替换 45 处 `WithCultureGet` 调用；动态 key（`TpTask.TrySwitchArea(areaName)`、`AutoDomainTask` 的树脂名、`ArtifactAffix.DefaultStrDic` 的值）同样直接传给 `Get`。
3. **清理**
   - 按 3.5 的清单删除；更新单元测试；在 `AGENTS.md` 中新增一条约定：“UI 文案用 `{i18n:T}` / `I18nService`；与游戏客户端文字做匹配的文本用 `GameTextService`，写入 `GameTask/I18n/*.json`”。

## 5. 测试

- **表一致性**（新增）：以所有非 zh-Hans 表的 key 并集为基准，列出每种语言的缺项。现有缺项（见 1.3）作为已知基线写入测试，新增缺项时测试失败，从而倒逼补译文，而不是静默回退。`zh-Hans.json` 的 key 必须是该并集的子集。
- **格式**：每个 JSON 都能被 Newtonsoft.Json 解析为 `Dictionary<string, string>`，且没有空值。
- **行为**：`Get` 对缺项返回 key 本身；不支持的语言按 `zh-Hans` 处理；`挑战达成` 在 `zh-Hans` 下返回 `(挑战|达成)`。
- **迁移**：`AutoArtifactSalvageTaskTests`、`BehavioursTests` 改为使用 `GameTextService`。`PaddleOcrServiceTests` 中按语言给出的期望文本可以用来交叉核对导出后的 JSON。

## 6. 风险

| 风险 | 应对 |
|---|---|
| 导出脚本漏项或改动了文本（空白、转义） | 导出后用脚本逐条比对 resx 与 JSON 的值，全部相等才提交 |
| 用户手动改坏 JSON | 解析失败视为空表并打 `Error` 日志，行为退化为“回退简体原文”，与现在的缺项表现一致，不会崩溃 |
| 第三方脚本或插件依赖 `IStringLocalizer` | 仓库内没有发现外部可见的 API 暴露它；JS 脚本接口不涉及 resx |
| `ja`/`ru` 用户配置被重置 | 改造前这些用户的识别已经失效；重置时打日志提示用户确认 |
