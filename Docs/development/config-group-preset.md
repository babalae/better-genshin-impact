# 调度器预制菜编写说明

预制菜是随 BetterGI 本体发布的配置组模板，用于快速创建一组已经编排好的调度器任务。预制菜属于本体资源，不放入 Javascript 脚本仓库，也不会参与脚本仓库订阅和更新。

## 目录结构

在本体项目中，每个预制菜使用一个以预制菜名称命名的目录：

```text
BetterGenshinImpact/
└─ Assets/
   └─ Config/
      └─ Preset/
         └─ 自动晶蝶日常/
            ├─ manifest.json
            ├─ 自动晶蝶日常.json
            └─ README.md
```

目录名、`manifest.json` 中的 `name`、配置组 JSON 中的 `name` 必须完全一致。预制菜目录下至少应包含以下三个文件：

- `manifest.json`：声明预制菜的基本信息和依赖。
- 配置组 JSON：用于导入调度器，通常命名为 `<预制菜名称>.json`。
- `README.md`：在预制菜浏览窗口中展示使用说明、注意事项和配置建议。

## 编写 manifest.json

文件使用 UTF-8 编码，字段采用 snake_case 命名。下面是完整示例：

```json
{
  "manifest_version": 1,
  "id": "preset.crystalfly.daily",
  "name": "自动晶蝶日常",
  "version": "1.0.0",
  "min_bgi_version": "0.64.2",
  "description": "执行自动晶蝶采集任务的日常配置组。",
  "authors": ["作者名称"],
  "config_group_file": "自动晶蝶日常.json",
  "readme_file": "README.md",
  "dependencies": [
    {
      "type": "Javascript",
      "name": "自动晶蝶",
      "path": "AutoCrystalfly"
    }
  ]
}
```

字段说明：

| 字段 | 必填 | 说明 |
| --- | --- | --- |
| `manifest_version` | 是 | manifest 格式版本，目前填写 `1`。 |
| `id` | 是 | 预制菜的稳定标识，建议使用 `preset.<作者或组织>.<名称>`，发布后不要随意修改。 |
| `name` | 是 | 预制菜显示名称，同时必须与目录名和配置组 JSON 的 `name` 一致。 |
| `version` | 是 | 预制菜版本号，建议使用语义化版本。 |
| `min_bgi_version` | 否 | 支持的最低 BetterGI 版本说明。目前用于声明信息，不会自动阻止应用。 |
| `description` | 否 | 列表和详情页显示的简介。 |
| `authors` | 否 | 作者名称列表。 |
| `config_group_file` | 否 | 配置组 JSON 的相对路径，默认为 `<name>.json`。路径必须位于当前预制菜目录内。 |
| `readme_file` | 否 | 使用说明文件的相对路径，默认为 `README.md`。 |
| `dependencies` | 否 | 应用前需要存在的脚本、地图追踪任务或键鼠脚本。 |

### 依赖声明

每项依赖包含以下字段：

```json
{
  "type": "Javascript",
  "name": "自动晶蝶",
  "path": "AutoCrystalfly"
}
```

- `type` 支持 `Javascript`（或 `js`）、`Pathing`、`KeyMouse`（或 `key_mouse`）。
- `name` 是给用户看的名称。缺失依赖提示会优先使用该字段。
- `path` 是相对于对应用户目录的路径：
  - Javascript：`User/JsScript/<path>`，目录中必须存在 `manifest.json`；
  - Pathing：`User/AutoPathing/<path>`，文件或目录存在即可；
  - KeyMouse：`User/KeyMouseScript/<path>`，文件或目录存在即可。

应用预制菜时只进行本地完整性检查。如果依赖不完整，界面会列出缺失项并提示用户前往订阅，不会自动订阅、跳转或修改订阅状态。

## 编写配置组 JSON

配置组 JSON 使用调度器现有的 `ScriptGroup` 格式。建议先在调度器中手动创建并配置一个配置组，再导出或参考其 JSON 内容，避免遗漏任务配置字段。

最小示例：

```json
{
  "index": 0,
  "name": "自动晶蝶日常",
  "config": {},
  "projects": [
    {
      "index": 0,
      "name": "自动晶蝶",
      "folderName": "AutoCrystalfly",
      "type": "Javascript",
      "status": "Enabled",
      "schedule": "Daily",
      "runNum": 1,
      "jsScriptSettingsObject": {}
    }
  ]
}
```

注意事项：

1. 顶层 `name` 必须与 `manifest.json` 的 `name` 完全一致。
2. `index` 会在应用时由 BetterGI 根据用户现有配置组重新分配，模板中的值不会覆盖用户排序。
3. `projects` 中的任务名称、目录名和类型应与实际依赖匹配。
4. Javascript 脚本的个性化参数放在 `jsScriptSettingsObject` 中；如果脚本有设置页，建议在 README 中说明需要调整的参数。
5. 不要把用户本地生成的绝对路径、账号信息、Cookie 或其他敏感数据写入模板。

## 编写 README.md

README 会在预制菜详情窗口中渲染，建议至少包含：

- 预制菜用途和适用场景；
- 使用前需要订阅的依赖及其用途；
- 应用后需要检查或修改的脚本参数；
- 执行前提、运行顺序和注意事项；
- 常见问题及排查方法。

README 中的图片和其他 Markdown 相对链接应放在当前预制菜目录内，并使用相对路径引用。例如：

```markdown
![流程示意图](images/flow.png)
```

详情窗口会以预制菜目录作为 Markdown 基路径解析这些链接。

## 本地验证清单

提交前建议逐项检查：

1. 目录位于 `BetterGenshinImpact/Assets/Config/Preset/`，且目录名与 `name` 一致。
2. `manifest.json` 可以按 snake_case 字段正常解析，必填字段均已填写。
3. 配置组 JSON 可以被调度器读取，顶层 `name` 与 manifest 一致。
4. `README.md` 中没有绝对路径、账号信息或敏感数据。
5. 依赖的 `path` 与实际 `User` 目录结构一致。
6. 在未安装依赖的环境中点击应用，能够看到缺失依赖提示。
7. 安装依赖后应用，调度器中会新增同名配置组；再次应用同一预制菜会提示配置组已存在，不会覆盖用户配置。

完成以上检查后，再将整个预制菜目录随本体资源提交。不要将其复制到脚本仓库或订阅目录中。
