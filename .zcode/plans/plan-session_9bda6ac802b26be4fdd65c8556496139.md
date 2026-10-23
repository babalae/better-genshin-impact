## 目标

在 BetterGI 生产代码里给 YOLO 检测加低频耗时统计，日志直接进日志栏，便于用真实截图实测；只统计 YOLO 检测，不碰 OCR 日志。

## 改动

### 1. 新增 `Core/Recognition/ONNX/YoloLatencyMonitor.cs`（internal）

- 按模型名聚合，`lock` 保证线程安全，环形保留最近 200 个样本
- **第 1 次调用**输出一行，之后**每 50 次**输出一行 —— 拾取扫圈约 3 次/秒时约 17 秒一行，不刷屏
- 输出走 `Serilog.Log.Information`：日志栏 sink 的门槛正是 Information，能直接看到；OCR 的 `LogDebug` 不会进日志栏
- 单行格式：

```
[YOLO] BgiWorld 50 次 | 均 18.3ms p50 14.1 p90 42.0 最大 192.3 | 慢于 50ms 7 次 | 最近 3 框
```

`慢于 50ms` 计数专门用来观察 DML 尾延迟。

### 2. `BgiYoloPredictor` 增加计时入口

- 现有 `Detect(ImageRegion)` 内包一层计时并上报（ScanPickTask、AutoFight 战斗结束检测走这里）
- 新增重载 `public YoloResult<Detection> Detect(Image<Rgb24> image)`，同样计时上报，供现在直接调用 `Predictor.Detect` 的调用方使用

### 3. 5 处直接调用改走新重载

- `AutoDomainTask.cs:1272`
- `AutoFishing/Behaviours.cs:95`、`:498`
- `AutoFishing/Behaviours.PartII.cs:139`
- `LinneaMiningTask.cs:272`

`InferenceModelDiagnostics` 与脚本 API（`ScriptYoloPredictor`）不接入，避免把诊断/用户脚本的调用算进生产统计。

### 4. 不改的东西

- 不动 OCR 任何日志（现有 `[OCR][...]` 都是 Debug 级，本来就进不了日志栏）
- 不加分类模型（Q/E/AvatarSide）统计，聚焦检测路径
- 不改任何模型推理逻辑

## 验证

1. `dotnet build BetterGenshinImpact.sln -c Debug`
2. 你把新 DLL 拷到 `bin\Debug\net8.0-windows10.0.22621.0\`，重启后跑一次拾取/战斗
3. 日志栏应出现 `[YOLO] BgiWorld ...` 行；若 50 次内没扫满，第一次调用也会先出一行

## 注意

统计是常驻的（每 50 次一行 Information）。如果实测完希望彻底静默，说一声我再把它挂到配置开关后面。