# Recognition.json 编写说明

本文档说明各任务目录下 `Assets\Recognition.json` 的写法，用于将普通静态 `RecognitionObject` 配置化，并通过 `RecognitionAssets.Get(...)` 动态加载。

## 1. 文件位置

每个任务自己的识别配置文件放在：

```text
BetterGenshinImpact\GameTask\<任务名>\Assets\Recognition.json
```

例如：

```text
BetterGenshinImpact\GameTask\AutoSkip\Assets\Recognition.json
BetterGenshinImpact\GameTask\Common\Element\Assets\Recognition.json
```

调用方式通常是：

```csharp
RecognitionAssets.Get("AutoSkip", "Collect", region);
RecognitionAssets.Get("AutoFight", "Confirm", width, height);
ElementRecognition.Get("PaimonMenu", region);
```

其中第二个参数就是 `objects` 下的对象名。

## 2. 基本结构

```json
{
  "version": 1,
  "vars": {},
  "regions": {},
  "templates": {},
  "objects": {}
}
```

各字段含义：

- `version`：配置版本号，目前固定写 `1`
- `vars`：公共变量，供表达式复用
- `regions`：公共区域表达式，供 `roi` / `reference.bbox` 通过 `@别名` 引用
- `templates`：公共模板别名，供 `template` 通过 `@别名` 引用
- `objects`：识别对象定义集合

## 3. 一个最小示例

```json
{
  "version": 1,
  "regions": {
    "topLeftQuarter": "rect(0, 0, cw / 4, ch / 4)"
  },
  "objects": {
    "PaimonMenu": {
      "type": "TemplateMatch",
      "template": "paimon_menu.png",
      "roi": "@topLeftQuarter",
      "draw": false
    }
  }
}
```

说明：

- `PaimonMenu` 是对象名，也是调用时的 `objectName`
- `type` 必须与 `RecognitionTypes` 枚举文本完全一致
- `template` 是模板文件名
- `roi` 可以直接写表达式，也可以引用 `regions` 中的别名
- 模板匹配场景下如果不写 `name`，运行时名称默认使用模板名，因此通常可以省略

## 4. 表达式系统

`roi`、`reference.bbox`、`vars` 中的值都支持表达式，底层由 NCalc 解析。

### 4.1 内置变量

- `cw`：当前截图宽度
- `ch`：当前截图高度
- `cx`：当前截图区域左上角 X
- `cy`：当前截图区域左上角 Y
- `s`：资源缩放系数
  - 当截图宽度小于 1920 时，`s = cw / 1920`
  - 其他情况 `s = 1`

### 4.2 支持的函数

- `rect(x, y, w, h)`：创建矩形
- `cutLeft(percent)`：截取左侧比例区域
- `cutRight(percent)`：截取右侧比例区域
- `cutTop(percent)`：截取上侧比例区域
- `cutBottom(percent)`：截取下侧比例区域
- `cutLeftTop(widthPercent, heightPercent)`：截取左上区域
- `cutRightTop(widthPercent, heightPercent)`：截取右上区域
- `cutLeftBottom(widthPercent, heightPercent)`：截取左下区域
- `cutRightBottom(widthPercent, heightPercent)`：截取右下区域

### 4.3 示例

```json
{
  "vars": {
    "topBarHeight": "100 * s"
  },
  "regions": {
    "inventoryTopRight": "rect(cw * 3 / 4, 0, cw / 4, topBarHeight)",
    "pageCloseWhite": "rect(cw - cw / 8, 0, cw / 8, ch / 8)",
    "leftBottom": "cutLeftBottom(0.2, 0.2)"
  }
}
```

## 5. 模板别名和区域别名

### 5.1 区域别名

在 `regions` 中先定义：

```json
"regions": {
  "option": "rect(cw / 2, ch / 12, cw - cw / 2 - cw / 6, ch - ch / 12 - 10)"
}
```

再在对象里引用：

```json
"roi": "@option"
```

### 5.2 模板别名

在 `templates` 中先定义：

```json
"templates": {
  "pageClose": "page_close.png"
}
```

再在对象里引用：

```json
"template": "@pageClose"
```

适合多个对象共用同一模板，或者模板文件名比较长的时候。

## 6. objects 常用字段

下面只列常用和当前加载器支持的字段。

### 6.1 通用字段

- `name`
  - 可选
  - 运行时名称
  - 模板匹配通常可以省略

- `type`
  - 必填
  - 必须与 `RecognitionTypes` 一致
  - 常见值：
    - `TemplateMatch`
    - `ColorMatch`
    - `OcrMatch`
    - `Ocr`
    - `ColorRangeAndOcr`

- `roi`
  - 可选
  - 返回 `Rect` 的表达式，或 `@区域别名`

- `draw`
  - 可选
  - 是否绘制调试框

- `drawColor`
  - 可选
  - 调试框颜色，HTML 颜色格式，例如 `#FF0000`

- `drawWidth`
  - 可选
  - 调试框线宽

## 7. TemplateMatch 字段

适用于 `type = "TemplateMatch"`。

- `template`
  - 必填
  - 模板文件名、相对路径或 `@模板别名`

- `templateMode`
  - 可选
  - 对应 OpenCV `ImreadModes`
  - 默认 `Color`

- `threshold`
  - 可选
  - 匹配阈值
  - 默认通常为 `0.8`

- `use3Channels`
  - 可选
  - 是否使用三通道匹配

- `templateMatchMode`
  - 可选
  - 对应 OpenCV `TemplateMatchModes`
  - 常见值：
    - `CCoeffNormed`
    - `CCorrNormed`
    - `SqDiff`

- `useMask`
  - 可选
  - 是否启用遮罩

- `maskColor`
  - 可选
  - 遮罩颜色，HTML 颜色格式

- `maxMatchCount`
  - 可选
  - 最大匹配数量
  - 不限制时可不填

- `useBinaryMatch`
  - 可选
  - 是否先二值化再匹配

- `binaryThreshold`
  - 可选
  - 二值化阈值

示例：

```json
"SubmitGoods": {
  "type": "TemplateMatch",
  "template": "submit_goods.png",
  "roi": "rect(0, 0, cw / 2, ch / 3)",
  "threshold": 0.9,
  "use3Channels": true,
  "templateMatchMode": "CCorrNormed",
  "draw": true
}
```

## 8. ColorMatch / ColorRangeAndOcr 字段

适用于颜色范围识别相关场景。

- `colorCode`
  - 可选
  - 对应 OpenCV `ColorConversionCodes`
  - 常见值：
    - `BGR2RGB`
    - `BGR2HSV`
    - `BGR2GRAY`

- `lowerColor`
  - 可选
  - 颜色下界
  - 支持 1 到 4 个数字

- `upperColor`
  - 可选
  - 颜色上界
  - 支持 1 到 4 个数字

- `matchCount`
  - 可选
  - 至少命中的像素点数量

示例：

```json
"SomeColorMark": {
  "type": "ColorMatch",
  "roi": "rect(0, 0, cw / 4, ch / 4)",
  "colorCode": "BGR2HSV",
  "lowerColor": [90, 80, 80],
  "upperColor": [130, 255, 255],
  "matchCount": 10
}
```

## 9. OCR / OcrMatch 字段

适用于 `type = "Ocr"` 或 `type = "OcrMatch"`。

- `ocrEngine`
  - 可选
  - 对应 `OcrEngineTypes`
  - 当前通常写 `Paddle`

- `text`
  - 可选
  - 用于 OCR 结果筛选的目标文本

- `replace`
  - 可选
  - OCR 误识别替换表

- `allContains`
  - 可选
  - 所有文本都包含才算成功

- `oneContains`
  - 可选
  - 任一文本包含即成功

- `regex`
  - 可选
  - 正则表达式列表
  - 当前实现要求列表中的所有正则都命中

示例：

```json
"PlayingText": {
  "type": "OcrMatch",
  "roi": "rect(100 * s, 35 * s, 85 * s, 35 * s)",
  "oneContains": [
    "播放",
    "暂停",
    "继续"
  ],
  "draw": true
}
```

`replace` 示例：

```json
"replace": {
  "播放": ["播故", "搰放"],
  "继续": ["绫续"]
}
```

## 10. reference 字段

`reference` 用于记录模板截图来源信息，便于后续按参考尺寸或原始包围盒做定位扩展。

字段：

- `size`
  - 格式 `[width, height]`
  - 表示模板来源截图尺寸

- `bbox`
  - 返回 `Rect` 的表达式
  - 表示模板在来源截图中的包围盒

示例：

```json
"reference": {
  "size": [1920, 1080],
  "bbox": "rect(1680, 32, 180, 72)"
}
```

## 11. search 字段

`search` 用于描述锚点和额外扩展区域。

字段：

- `anchor`
  - 对应 `SearchAnchorMode` 枚举名

- `expand`
  - 格式 `[width, height]`
  - 表示搜索时向外扩展的尺寸

示例：

```json
"search": {
  "anchor": "Center",
  "expand": [120, 80]
}
```

## 12. 推荐写法

- 普通模板匹配对象优先只写 `type`、`template`、`roi`、`threshold`
- 重复使用的区域优先抽到 `regions`
- 重复使用的模板优先抽到 `templates`
- 可通过表达式描述的 ROI 尽量写进 JSON，不再写死在 C# 里
- 普通静态对象不要再回退到 `XXXAssets` 字段里缓存，直接调用 `RecognitionAssets.Get(...)`
- 模板匹配对象通常不需要手写 `name`

## 13. 不建议放进 JSON 的场景

下面这些仍然更适合保留在 C# 里：

- 需要运行时变量替换，而且变量来源复杂
- 需要动态拼接多份 Mat / 模板列表
- 需要依赖特殊 OpenCV 预处理逻辑
- 需要依赖外部配置、业务状态或复杂流程控制
- ROI 不是单纯表达式，而是与多个运行时对象联动

这类对象可以继续保留在对应任务类或专用 helper 中。

## 14. 排错建议

如果加载失败，当前日志会输出类似：

```text
Recognition 加载失败: Collect @ 1920x1080, file=...
Recognition 构建失败: Collect @ 1920x1080
```

建议优先检查：

1. `type` 是否和枚举名完全一致
2. `templateMatchMode`、`templateMode`、`colorCode`、`ocrEngine`、`anchor` 是否和对应枚举名完全一致
3. `template` 引用的模板文件或模板别名是否存在
4. `roi` / `bbox` 表达式是否能返回 `Rect`
5. `@区域别名`、`@模板别名`、`vars` 引用的名字是否存在
6. `lowerColor` / `upperColor` 数组长度是否在 1 到 4 之间

## 15. 一个较完整示例

```json
{
  "version": 1,
  "vars": {
    "topBarHeight": "100 * s"
  },
  "regions": {
    "topLeftQuarter": "rect(0, 0, cw / 4, ch / 4)",
    "inventoryTopRight": "rect(cw * 3 / 4, 0, cw / 4, topBarHeight)"
  },
  "templates": {
    "menu": "paimon_menu.png"
  },
  "objects": {
    "PaimonMenu": {
      "type": "TemplateMatch",
      "template": "@menu",
      "roi": "@topLeftQuarter",
      "threshold": 0.8,
      "draw": false
    },
    "Inventory": {
      "type": "TemplateMatch",
      "template": "inventory.png",
      "roi": "@inventoryTopRight",
      "draw": false
    },
    "ConfirmText": {
      "type": "OcrMatch",
      "roi": "rect(cw / 2, ch / 2, 200 * s, 80 * s)",
      "oneContains": ["确认", "确定"]
    }
  }
}
```
