using System.Diagnostics;
using OpenCvSharp;
using OcrDmlProbe.Ocr;

// ============================================================================
// OCR DirectML 探针：独立复刻 BetterGI 的 PpOcrV6 Det/Rec 推理路径
// （形状分桶 + 预热 + DBNet 后处理 + 透视裁剪 + CTC 解码），
// 会话参数与 BgiOnnxFactory 的 DirectML 分支完全一致，用于 VTune 等分析器定位耗时。
//
// 常用示例：
//   OcrDmlProbe.exe                                  # 完整管线，预热1次+计时10次（与内置测速一致）
//   OcrDmlProbe.exe --mode stage --iters 20          # 带逐阶段分解的完整管线
//   OcrDmlProbe.exe --mode buckets --iters 20        # 纯 session.Run 逐档位成本
//   OcrDmlProbe.exe --pause --loop 120               # 打印 PID 等待 attach，然后连续跑 120 秒
// ============================================================================

var argsList = new List<string>(args);
string? Opt(string name)
{
    var i = argsList.IndexOf(name);
    return i >= 0 && i + 1 < argsList.Count ? argsList[i + 1] : null;
}
bool Has(string name) => argsList.Contains(name);

var mode = Opt("--mode") ?? "pipeline";
var dmlDeviceId = int.TryParse(Opt("--device"), out var dev) ? dev : 0;
var cpuThreads = int.TryParse(Opt("--cpu-threads"), out var ct) ? ct : 0;
var providers = Opt("--providers") ?? "both"; // both=生产默认 [Dml,Cpu] / dml=仅DML / cpu=纯CPU
var iters = int.TryParse(Opt("--iters"), out var it) ? it : 10;
var warmupCount = int.TryParse(Opt("--warmup"), out var wc) ? wc : 1;
var recBatch = int.TryParse(Opt("--rec-batch"), out var rb) ? rb : 0;
var loopSeconds = double.TryParse(Opt("--loop"), out var ls) ? ls : 0;
var pause = Has("--pause");
var quiet = Has("--quiet");
var cold = Has("--cold");
var enableProfile = Has("--profile");
var threads = int.TryParse(Opt("--threads"), out var th) ? th : 4;
var verbose = Has("--verbose") || !quiet;

DmlSessionFactory.GraphCapture = Has("--graph-capture");
DmlSessionFactory.CpuSyncSpinning = Has("--spin");
DmlSessionFactory.NoArena = Has("--no-arena");
DmlSessionFactory.CpuSyncSpinning = Has("--spin");
Console.OutputEncoding = System.Text.Encoding.UTF8;
Console.WriteLine($"PID = {Environment.ProcessId}（可用 --pause 暂停等待分析器 attach）");
Console.WriteLine($"providers={providers} | DirectML 设备 {dmlDeviceId} | CPU兜底线程 {cpuThreads} | 模型 PpOcrDetV6/PpOcrRecV6 | 模式 {mode}");

// ---------- 资产定位 ----------
var root = Opt("--root");
string[] candidates;
{
    // 候选目录：显式指定 > 探针自身目录 > 探针各级上级目录及其中的 BetterGI 标准输出路径
    var candidateList = new List<string> { root ?? "", AppContext.BaseDirectory };
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    for (var i = 0; i < 6 && dir != null; i++, dir = dir.Parent)
    {
        candidateList.Add(dir.FullName);
        candidateList.Add(Path.Combine(dir.FullName,
            "BetterGenshinImpact", "bin", "x64", "Debug", "net8.0-windows10.0.22621.0"));
    }

    candidates = [.. candidateList];
}
var appRoot = candidates.FirstOrDefault(c => !string.IsNullOrEmpty(c) &&
    Directory.Exists(Path.Combine(c, "Assets", "Model", "PaddleOCR")))
    ?? throw new FileNotFoundException(
        "未找到 Assets\\Model\\PaddleOCR，请用 --root <目录> 指定 BetterGI 输出目录（含 Assets）。");
Console.WriteLine($"资产目录: {appRoot}");

var detModelPath = Opt("--det-model") ?? Path.Combine(appRoot, "Assets", "Model", "PaddleOCR", "Det", "V6",
    "PP-OCRv6_small_det_infer", "slim.onnx");
var recModelPath = Opt("--rec-model") ?? Path.Combine(appRoot, "Assets", "Model", "PaddleOCR", "Rec", "V6",
    "PP-OCRv6_small_rec_infer", "slim.onnx");
var preheatImagePath = Path.Combine(appRoot, "Assets", "Model", "PaddleOCR", "test_pp_ocr.png");
foreach (var f in new[] { detModelPath, recModelPath, preheatImagePath })
{
    if (!File.Exists(f)) throw new FileNotFoundException("模型/图片缺失", f);
}

var config = OcrVersionConfig.PpOcrV6;

// ---------- 构建管线（与 PaddleOcrService 构造一致：9 档形状预热 + 1 次完整预热推理） ----------
Action<string> log = quiet ? _ => { } : Console.WriteLine;
using var det = new DetProbe("PpOcrDetV6", detModelPath, config, dmlDeviceId, cpuThreads, log, providers, enableProfile);
var labels = PaddlePipeline.LoadRecLabels(recModelPath);
using var rec = new RecProbe("PpOcrRecV6", recModelPath, labels, config, dmlDeviceId, cpuThreads, log, providers, enableProfile);
using var pipeline = new PaddlePipeline(det, rec);

var swTotal = Stopwatch.StartNew();
if (!cold)
{
    var detBuckets = det.GetWarmupBuckets();
    var recBuckets = rec.GetWarmupBuckets();
    var totalBuckets = detBuckets.Length + recBuckets.Length;
    Console.WriteLine($"[OCR] 预热编译形状 0/{totalBuckets}");
    var index = 0;
    foreach (var bucket in detBuckets)
    {
        index++;
        var sw = Stopwatch.StartNew();
        det.Warmup(bucket);
        Console.WriteLine($"[OCR] 预热编译形状 {index}/{totalBuckets}：检测 {bucket}x{bucket}  耗时 {sw.Elapsed.TotalMilliseconds:F0}ms");
    }

    foreach (var (batch, width) in recBuckets)
    {
        index++;
        var sw = Stopwatch.StartNew();
        rec.Warmup(batch, width);
        Console.WriteLine($"[OCR] 预热编译形状 {index}/{totalBuckets}：识别 批{batch} 宽{width}  耗时 {sw.Elapsed.TotalMilliseconds:F0}ms");
    }

}
else
{
    Console.WriteLine("[OCR] --cold：跳过形状预热（用于观察首次编译开销）");
}

using var preheatImage = Cv2.ImRead(preheatImagePath, ImreadModes.Color);
if (preheatImage.Empty()) throw new InvalidDataException($"测试图片无法读取: {preheatImagePath}");
Console.WriteLine($"测试图片: {preheatImage.Width}x{preheatImage.Height}");
try { pipeline.RunAll(preheatImage, out _, 1); } catch (Exception e) { Console.WriteLine($"[预热推理失败(对纯计时模式无影响): {e.Message}"); } // PaddleOcrService 构造里的完整预热推理
Console.WriteLine($"[OCR] 初始化完成（含形状预热），用时 {swTotal.Elapsed.TotalMilliseconds:F0}ms");

if (pause)
{
    Console.WriteLine("已暂停：请在分析器中 attach 本进程（PID 见首行），然后按回车继续...");
    Console.ReadLine();
}

var proc = System.Diagnostics.Process.GetCurrentProcess();
Console.WriteLine($"[内存] 初始 提交 {proc.PrivateMemorySize64 / 1048576}MB / 工作集 {proc.WorkingSet64 / 1048576}MB");

// ---------- 模式执行 ----------
switch (mode)
{
    case "pipeline":
    case "stage":
        RunPipelineMode(preheatImage, iters, warmupCount, recBatch, verbose);
        if (loopSeconds > 0) LoopPipeline(preheatImage, loopSeconds, recBatch);
        break;
    case "det":
        RunDetMode(preheatImage, iters, warmupCount);
        break;
    case "rec":
        RunRecMode(preheatImage, iters, warmupCount, recBatch);
        break;
    case "buckets":
        RunBucketsMode(iters);
        break;
    case "single":
        RunSingleMode(preheatImage, iters, warmupCount);
        break;
    case "batch":
        RunBatchMode(preheatImage, iters);
        break;
    case "concurrency":
        RunConcurrencyMode(threads, loopSeconds > 0 ? loopSeconds : 5);
        break;
    default:
        throw new ArgumentException($"未知模式 {mode}，支持: pipeline / det / rec / buckets / batch / concurrency");
}

// ---------- ORT 内置 profiler：按算子聚合输出热点（无需 VTune 即可定位是哪类算子在烧时间） ----------
if (enableProfile)
{
    PrintOpSummary("Det", CaptureTrace(det.EndProfiling(), "det"));
    PrintOpSummary("Rec", CaptureTrace(rec.EndProfiling(), "rec"));
}

return;

/// <summary>把 ORT trace 复制到探针目录的固定名字（双会话同秒文件名会冲突，且句柄延迟释放）。</summary>
static string CaptureTrace(string tracePath, string tag)
{
    var dest = Path.Combine(AppContext.BaseDirectory, $"ort_trace_{tag}.json");
    for (var retry = 0; ; retry++)
    {
        try
        {
            using var src = new FileStream(tracePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var dst = File.Create(dest);
            src.CopyTo(dst);
            return dest;
        }
        catch (IOException) when (retry < 10)
        {
            Thread.Sleep(300);
        }
    }
}

/// <summary>解析 ORT chrome trace JSON，按算子名聚合总耗时并输出 Top-N。</summary>
static void PrintOpSummary(string tag, string tracePath)
{
    if (string.IsNullOrEmpty(tracePath) || !File.Exists(tracePath))
    {
        Console.WriteLine($"[{tag}] profiler 未产出文件: {tracePath}");
        return;
    }
    using var fs = File.OpenRead(tracePath);
    using var doc = System.Text.Json.JsonDocument.Parse(fs);
    var byName = new Dictionary<string, (double TotalUs, int Count)>();
    double totalNodeUs = 0;
    int nodeEvents = 0;
    foreach (var ev in doc.RootElement.EnumerateArray())
    {
        if (!ev.TryGetProperty("cat", out var cat) || cat.GetString() != "Node") continue;
        if (!ev.TryGetProperty("name", out var nameEl)) continue;
        if (!ev.TryGetProperty("dur", out var durEl)) continue;
        var name = nameEl.GetString() ?? "?";
        var dur = durEl.GetDouble();
        totalNodeUs += dur;
        nodeEvents++;
        var (t, c) = byName.TryGetValue(name, out var v) ? v : (0, 0);
        byName[name] = (t + dur, c + 1);
    }

    Console.WriteLine($"==== [{tag}] ORT 算子热点（{nodeEvents} 个事件，算子总耗时 {totalNodeUs / 1000:F1}ms） trace: {tracePath} ====");
    foreach (var (name, (total, count)) in byName.OrderByDescending(kv => kv.Value.Item1).Take(25))
    {
        Console.WriteLine($"  {total / 1000,9:F2}ms  x{count,-5} 均 {total / Math.Max(1, count),8:F1}us  {name}");
    }
}

// ---------- 完整管线（等价于内置"OCR 测速"：service.Ocr(image)） ----------
void RunPipelineMode(Mat image, int iterations, int warmups, int batchSize, bool showStage)
{
    for (var w = 0; w < warmups; w++)
    {
        pipeline.RunAll(image, out _, batchSize);
    }

    var times = new double[iterations];
    var rows = new string[iterations];
    for (var i = 0; i < iterations; i++)
    {
        var result = pipeline.RunAll(image, out var t, batchSize);
        times[i] = t.TotalMs;
        if (i == 0)
        {
            Console.WriteLine("识别文本: " + string.Join(" | ", result.Regions.Select(r => r.Text)));
        }
        rows[i] =
            $"det[pre {t.Det.ResizeMs,6:F1} norm {t.Det.NormMs,6:F1} infer {t.Det.InferMs,6:F1} out {t.Det.OutConvMs,5:F1} post {t.Det.PostMs,5:F1}] " +
            $"crop {t.CropMs,5:F1} | rec[pre {t.Rec.PreMs,5:F1} infer {t.Rec.InferMs,6:F1} dec {t.Rec.DecodeMs,5:F1} {t.Rec.Lines}行 桶{t.Rec.BucketWidth}] " +
            $"| 文本区 {result.Regions.Length}";
    }

    PrintSummary("完整管线", times);
    if (showStage)
    {
        Console.WriteLine("---- 逐次阶段分解 ----");
        for (var i = 0; i < iterations; i++)
        {
            Console.WriteLine($"#{i + 1,3} total {times[i],7:F1}ms  {rows[i]}");
        }
    }
}

void LoopPipeline(Mat image, double seconds, int batchSize)
{
    Console.WriteLine($"---- 连续运行 {seconds:F0}s（profiler 采集窗口） ----");
    var deadline = Stopwatch.StartNew();
    var n = 0;
    while (deadline.Elapsed.TotalSeconds < seconds)
    {
        pipeline.RunAll(image, out _, batchSize);
        n++;
    }
    Console.WriteLine($"连续运行完成：{n} 次，平均 {deadline.Elapsed.TotalMilliseconds / Math.Max(1, n):F1}ms/次");
}

// ---------- 仅检测 ----------
void RunDetMode(Mat image, int iterations, int warmups)
{
    for (var w = 0; w < warmups; w++)
    {
        det.RunBoxes(image, out _);
    }

    var times = new double[iterations];
    for (var i = 0; i < iterations; i++)
    {
        var start = Stopwatch.GetTimestamp();
        det.RunBoxes(image, out var t);
        times[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        Console.WriteLine(
            $"#{i + 1,3} det total {times[i],7:F1}ms [pre {t.ResizeMs:F1} norm {t.NormMs:F1} infer {t.InferMs:F1} out {t.OutConvMs:F1} post {t.PostMs:F1}] 桶数 {t.Boxes}");
    }

    PrintSummary("仅检测 Det", times);
}

// ---------- 仅识别（用真实检测裁剪图，稳定复现 Rec 成本） ----------
void RunRecMode(Mat image, int iterations, int warmups, int batchSize)
{
    var crops = pipeline.ExportCrops(image);
    try
    {
        Console.WriteLine($"检测裁剪图 {crops.Length} 张（真实管线裁剪，固定后逐批识别）");

        for (var w = 0; w < warmups; w++)
        {
            rec.Run(crops, out _, batchSize);
        }

        var times = new double[iterations];
        for (var i = 0; i < iterations; i++)
        {
            var start = Stopwatch.GetTimestamp();
            rec.Run(crops, out var t, batchSize);
            times[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            Console.WriteLine(
                $"#{i + 1,3} rec total {times[i],7:F1}ms [pre {t.PreMs:F1} infer {t.InferMs:F1} dec {t.DecodeMs:F1}] {t.Lines}行 桶宽{t.BucketWidth}");
        }

        PrintSummary("仅识别 Rec", times);
    }
    finally
    {
        foreach (var m in crops) m.Dispose();
    }
}

// ---------- 真·批量对照：逐张各跑一次（生产现状） vs 堆叠 [N,3,48,W] 一次 Run ----------
void RunBatchMode(Mat image, int iterations)
{
    var crops = pipeline.ExportCrops(image);
    Console.WriteLine($"检测裁剪图 {crops.Length} 张，桶宽 {rec.GetWarmupBuckets().Last()} 上限");
    try
    {
        // 生产现状：RunMulti 每张一次 [1,3,48,W] Run
        rec.Run(crops, out var perLine, 1); // batch=1 强制逐张
        Console.WriteLine($"[生产现状] 逐张 {crops.Length} 次 Run 合计 推理 {perLine.InferMs:F1}ms（{perLine.InferMs / crops.Length:F1}ms/张）");

        for (var w = 0; w < 3; w++)
        {
            rec.TimeStackedBatch(crops); // 预热堆叠形状
        }

        var stacked = new double[iterations];
        for (var i = 0; i < iterations; i++)
        {
            stacked[i] = rec.TimeStackedBatch(crops);
        }

        Console.WriteLine($"[堆叠批量] 单次 Run 平均 {stacked.Average():F1}ms  最小 {stacked.Min():F1}  最大 {stacked.Max():F1}");
        Console.WriteLine($"==> 若堆叠明显更快，说明 DML 每次 Run 的固定开销是主因（生产 RunMulti 是伪批量）");
    }
    finally
    {
        foreach (var m in crops) m.Dispose();
    }
}

// ---------- 并发吞吐：判别 CPU 派发受限 vs GPU 执行受限 ----------
// 同一 session 多线程 Run 会直接 AccessViolation（DML EP 限制，生产用锁规避），
// 所以这里每个线程建独立 session：聚合吞吐随线程数增长 => CPU 侧派发受限；
// 不增长 => GPU/队列串行执行受限。
void RunConcurrencyMode(int threadCount, double seconds)
{
    const int bucket = 480;
    rec.Warmup(1, bucket);
    Console.WriteLine($"并发实验：{threadCount} 线程（独立 session）× Rec 桶{bucket} 零张量，运行 {seconds:F0}s");

    // 先测单线程基线
    var sw = Stopwatch.StartNew();
    var singleCount = 0L;
    while (sw.Elapsed.TotalSeconds < seconds)
    {
        rec.TimeBucketRunUnlocked(bucket);
        singleCount++;
    }
    Console.WriteLine($"单线程: {singleCount} 次, {singleCount / seconds:F1} 次/秒, 均 {sw.Elapsed.TotalMilliseconds / Math.Max(1, singleCount):F2}ms/次");

    var startSignal = new ManualResetEventSlim(false);
    var counts = new long[threadCount];
    var sessions = new List<RecProbe>();
    var threadsList = new List<Thread>();
    for (var t = 0; t < threadCount; t++)
    {
        var s = new RecProbe("PpOcrRecV6", recModelPath, labels, config, dmlDeviceId, cpuThreads, _ => { }, providers);
        s.Warmup(1, bucket);
        sessions.Add(s);
        var th = new Thread(() =>
        {
            var idx = sessions.IndexOf(s);
            startSignal.Wait();
            var threadStart = Stopwatch.GetTimestamp();
            while (Stopwatch.GetElapsedTime(threadStart).TotalSeconds < seconds)
            {
                s.TimeBucketRunUnlocked(bucket);
                counts[idx]++;
            }
        });
        th.IsBackground = true;
        threadsList.Add(th);
        th.Start();
    }

    var multiSw = Stopwatch.StartNew();
    startSignal.Set();
    threadsList.ForEach(t => t.Join());
    multiSw.Stop();
    var totalMulti = counts.Sum();
    Console.WriteLine($"{threadCount} 线程（独立session）: {totalMulti} 次, {totalMulti / seconds:F1} 次/秒（单线程的 {totalMulti / Math.Max(1, (double)singleCount):F2}x）");
    Console.WriteLine($"==> 吞吐接近线性增长 = CPU 派发受限；不增长 = GPU/队列串行");
    sessions.ForEach(s => s.Dispose());
}

// ---------- 单行场景基准：从测试图裁第一行文本 ROI，跑完整 det+rec 管线（模拟拾取类单行 OCR） ----------
void RunSingleMode(Mat image, int iterations, int warmups)
{
    // 用检测框生成单行 ROI（模拟 BetterGI 传入的截图子区域）
    var crops = pipeline.ExportCrops(image);
    foreach (var c in crops) c.Dispose();
    var boxes = det.RunBoxes(image, out _);
    var first = boxes.OrderBy(b => b.Points[0].Y).ThenBy(b => b.Points[0].X).First();
    var rect = Cv2.BoundingRect(first.Points.Select(p => new Point((int)p.X, (int)p.Y)));
    var margin = 8;
    rect.X = Math.Max(0, rect.X - margin);
    rect.Y = Math.Max(0, rect.Y - margin);
    rect.Width = Math.Min(image.Width - rect.X, rect.Width + margin * 2);
    rect.Height = Math.Min(image.Height - rect.Y, rect.Height + margin * 2);
    using var roi = new Mat(image, rect);
    Console.WriteLine($"单行 ROI: {roi.Width}x{roi.Height}");

    for (var w = 0; w < warmups; w++)
    {
        pipeline.RunAll(roi, out _);
    }

    var times = new double[iterations];
    for (var i = 0; i < iterations; i++)
    {
        var start = Stopwatch.GetTimestamp();
        var result = pipeline.RunAll(roi, out var t);
        times[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        if (i == 0) Console.WriteLine("识别文本: " + string.Join(" | ", result.Regions.Select(r => r.Text)));
        Console.WriteLine(
            $"#{i + 1,3} total {times[i],6:F1}ms  det[pre {t.Det.ResizeMs:F1} norm {t.Det.NormMs:F1} infer {t.Det.InferMs:F1} post {t.Det.PostMs:F1} 桶数{t.Det.Boxes}] rec[infer {t.Rec.InferMs:F1} 桶{t.Rec.BucketWidth}]");
    }

    PrintSummary("单行完整管线", times);
    var pm = System.Diagnostics.Process.GetCurrentProcess();
    Console.WriteLine($"[内存] 运行后 提交 {pm.PrivateMemorySize64 / 1048576}MB / 工作集 {pm.WorkingSet64 / 1048576}MB");

    // 无检测器单行识别（等价 OcrWithoutDetector）
    using var line = pipeline.ExportCrops(roi)[0];
    for (var w = 0; w < warmups; w++)
    {
        rec.Run([line], out _);
    }

    var recTimes = new double[iterations];
    for (var i = 0; i < iterations; i++)
    {
        var start = Stopwatch.GetTimestamp();
        rec.Run([line], out _);
        recTimes[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    }

    PrintSummary("单行 Rec(无检测器)", recTimes);
}

// ---------- 逐档位纯推理成本 ----------
void RunBucketsMode(int iterations)
{
    // 细宽度扫描：定位派发地板的拐点（--widths 160,192,224,...）
    var widthsArg = Opt("--widths");
    if (widthsArg != null)
    {
        var widths = widthsArg.Split(',').Select(w => int.TryParse(w, out var x) ? x : 0).Where(x => x > 0).ToArray();
        Console.WriteLine("---- Rec 细宽度扫描（零张量，含各宽度首次编译） ----");
        foreach (var width in widths)
        {
            rec.Warmup(1, width);
            var times = new double[iterations];
            for (var i = 0; i < iterations; i++)
            {
                times[i] = rec.TimeBucketRun(width, 1, DmlSessionFactory.GraphCapture ? width : -1);
            }

            Console.WriteLine($"Rec 宽{width,4} 平均 {times.Average(),7:F2}ms  最小 {times.Min(),7:F2}  最大 {times.Max(),7:F2}");
        }

        return;
    }

    Console.WriteLine("---- Det 各档位（零张量纯 session.Run，预热后计时） ----");
    foreach (var bucket in det.GetWarmupBuckets())
    {
        det.Warmup(bucket);
        var times = new double[iterations];
        for (var i = 0; i < iterations; i++)
        {
            times[i] = det.TimeBucketRun(bucket);
        }

        Console.WriteLine($"Det {bucket,4}x{bucket,-4} 平均 {times.Average(),7:F2}ms  最小 {times.Min(),7:F2}  最大 {times.Max(),7:F2}");
    }

    Console.WriteLine("---- Rec 各档位（零张量纯 session.Run，预热后计时） ----");
    foreach (var (batch, width) in rec.GetWarmupBuckets())
    {
        rec.Warmup(batch, width);
        var times = new double[iterations];
        for (var i = 0; i < iterations; i++)
        {
            times[i] = rec.TimeBucketRun(width, batch);
        }

        Console.WriteLine($"Rec 批{batch} 宽{width,4} 平均 {times.Average(),7:F2}ms  最小 {times.Min(),7:F2}  最大 {times.Max(),7:F2}");
    }
}

static void PrintSummary(string title, double[] times)
{
    var sorted = times.OrderBy(t => t).ToArray();
    static double Percentile(double[] sortedData, double p)
    {
        var idx = (int)Math.Ceiling(sortedData.Length * p) - 1;
        return sortedData[Math.Clamp(idx, 0, sortedData.Length - 1)];
    }

    Console.WriteLine($"==== {title} {sorted.Length} 次 ====");
    Console.WriteLine(
        $"平均 {sorted.Average():F1}ms  最快 {sorted.Min():F1}ms  最慢 {sorted.Max():F1}ms  " +
        $"p50 {Percentile(sorted, 0.5):F1}ms  p90 {Percentile(sorted, 0.9):F1}ms");
    Console.WriteLine("每次耗时(ms)：" + string.Join(" / ", times.Select(t => t.ToString("F1"))));
}
