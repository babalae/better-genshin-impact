# BetterGenshinImpact.I18nSync

`BetterGenshinImpact.I18nSync` 用于扫描主项目 XAML 中的 `{i18n:T 中文原文}`，并自动同步 `BetterGenshinImpact/User/I18n/*.json` 中的多语言 Key。

中文原文同时作为 i18n Key 和默认文案，因此 XAML 设计器可以直接显示中文；当目标语言没有对应翻译，或翻译值为空时，程序运行时也会回退显示这个中文 Key。

## 快速开始

请在仓库根目录运行：

```powershell
# 默认模式：补充缺少的 Key，并删除已经废弃的 Key
.\Build\Scripts\i18n-sync.ps1

# 只补充缺少的 Key，不删除废弃 Key
.\Build\Scripts\i18n-sync.ps1 -AddOnly

# 只检查默认同步模式是否会产生变更，不修改文件
.\Build\Scripts\i18n-sync.ps1 -Check

# 只检查“只补充”模式是否会产生变更，不修改文件
.\Build\Scripts\i18n-sync.ps1 -AddOnly -Check
```

脚本会自动把 `BetterGenshinImpact` 主项目目录传给同步工具，日常开发优先使用该脚本即可。

## XAML 写法

直接将中文界面文本作为 Key：

```xml
<TextBlock Text="{i18n:T 用户设置}" />
<Button Content="{i18n:T 保存}" />
<GroupBox Header="{i18n:T 自动战斗}" />
```

同步后，语言文件中会生成相同的 Key：

```json
{
  "保存": "",
  "用户设置": "",
  "自动战斗": ""
}
```

新增 Key 的值会故意留空，方便搜索和判断哪些翻译尚未补充。空字符串不会让界面显示为空，`I18nService` 会回退显示中文 Key。

## 同步模式

| 调用方式 | 补充缺少 Key | 删除废弃 Key | 写入文件 |
| --- | --- | --- | --- |
| 默认 | 是 | 是 | 是 |
| `-AddOnly` / `--add-only` | 是 | 否 | 是 |
| `-Check` / `--check` | 按默认模式检查 | 按默认模式检查 | 否 |
| `-AddOnly -Check` / `--add-only --check` | 是 | 否 | 否 |

“废弃 Key”是指语言 JSON 中存在、但当前项目 XAML 已经不再引用的 Key。默认模式会删除它们；如果正在重构界面、暂时不希望清理旧 Key，请使用 `-AddOnly`。

`Check` 模式不仅检查缺失或废弃 Key，也会检查文件的排序、缩进、换行等是否符合工具的规范化输出。发现任何需要同步的内容时，工具不会修改文件，但会返回退出码 `1`。

## 直接运行工具

需要指定其他项目目录，或者不经过 PowerShell 脚本时，可以直接运行：

```powershell
dotnet run --project .\Build\BetterGenshinImpact.I18nSync\BetterGenshinImpact.I18nSync.csproj -- --project .\BetterGenshinImpact
```

可用参数：

| 参数 | 说明 |
| --- | --- |
| `--project <目录>` | 指定要扫描的主项目目录，该目录下必须存在 `BetterGenshinImpact.csproj` |
| `--add-only` | 只补充缺少的 Key，保留废弃 Key |
| `--check` | 只检查是否需要同步，不写入文件 |
| `-h`、`--help` | 显示帮助信息 |

未传入 `--project` 时，工具会按以下顺序定位主项目：

1. 当前目录包含 `BetterGenshinImpact.csproj` 时，使用当前目录。
2. 否则尝试使用当前目录下的 `BetterGenshinImpact` 子目录。

## 语言文件约定

语言文件位于：

```text
BetterGenshinImpact/User/I18n/
```

例如：

```text
en-US.json
it-IT.json
ja-JP.json
ru-RU.json
```

同步规则如下：

- 处理该目录下所有 `*.json` 文件。
- 文件名以 `_` 开头的 JSON 会被忽略，可用于保存不参与同步的辅助文件。
- 已有 Key 的翻译值会原样保留。
- 新增 Key 的翻译值固定写为空字符串 `""`。
- 默认模式会删除 XAML 中已经不存在的 Key；`AddOnly` 模式会保留它们。
- 每次写入前都会使用 `StringComparer.Ordinal` 对全部 Key 排序。
- 输出格式为两空格缩进、LF 换行、UTF-8 无 BOM。
- 只有内容确实发生变化的语言文件才会被写入。

## Key 提取规则

工具会递归扫描项目中的 `*.xaml` 文件，并遵循以下规则：

- 提取 XML 属性值中的 `{i18n:T ...}`。
- 忽略 `bin` 和 `obj` 目录。
- XAML 注释中的文本不会被提取。
- 相同 Key 只保留一份。
- JSON 格式错误、重复 Key 或非字符串翻译值都会使同步失败，避免覆盖异常数据。
- 先验证并生成全部文件的同步计划，确认无错误后才开始写入，避免只更新一部分语言文件。

## 推荐工作流

1. 在 XAML 中新增、修改或删除 `{i18n:T 中文原文}`。
2. 运行 `.\Build\Scripts\i18n-sync.ps1` 同步所有语言文件。
3. 在各语言 JSON 中搜索值为 `""` 的项目并补充翻译。
4. 提交前运行 `.\Build\Scripts\i18n-sync.ps1 -Check`。
5. 检查 Git 差异，确认删除的废弃 Key 符合预期。

如果界面重构尚未完成，建议第 2 步临时使用 `-AddOnly`，等 XAML 调整完成后再运行默认模式统一清理废弃 Key。

## CI 检查

可以在持续集成环境中运行：

```powershell
pwsh .\Build\Scripts\i18n-sync.ps1 -Check
```

退出码约定：

| 退出码 | 含义 |
| --- | --- |
| `0` | 同步成功，或检查确认无需变更 |
| `1` | `Check` 模式发现需要同步的内容 |
| `2` | 参数错误、路径错误、XAML/JSON 解析失败等执行错误 |

## 常见问题

### 新增 Key 后为什么翻译值是空字符串？

这是预期行为。空字符串用于标记待翻译内容，运行时会回退显示中文 Key，不会显示空白。

### 为什么 `Check` 模式返回失败，但没有缺少的 Key？

文件中可能存在废弃 Key，或者排序、缩进、换行等格式与工具的规范化输出不一致。先运行一次对应的同步模式，再检查 Git 差异。

### 默认同步删除了某个 Key，应该怎么办？

确认该 Key 是否仍在真实的 XAML 属性中使用。注释中的引用不会被视为有效引用。重构期间需要暂时保留旧 Key 时，请使用 `-AddOnly`。

### 修改了 JSON，但界面仍显示中文 Key

请确认当前语言文件中的 Key 与 XAML 中文原文完全一致，并且翻译值不是空字符串或纯空白。Key 比较区分大小写，也不会自动忽略前后空格。
