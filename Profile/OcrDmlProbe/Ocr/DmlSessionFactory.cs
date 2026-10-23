using Microsoft.ML.OnnxRuntime;

namespace OcrDmlProbe.Ocr;

/// <summary>
/// 复刻 BetterGI BgiOnnxSessionFactory 对 DirectML 的会话创建：
/// ProviderTypes = [Dml, Cpu]；
/// DML 分支：EnableMemoryPattern=false + ORT_SEQUENTIAL + AppendExecutionProvider_DML(deviceId)；
/// CPU 分支：IntraOpNumThreads = 全局 CPU 线程设置（PaddleOcrCpu 细分策略只对纯 CPU 会话生效，这里不启用）。
/// </summary>
public static class DmlSessionFactory
{
    /// <summary>实验开关（由命令行设置）：DML 命令列表捕获回放 / fence 自旋等待 / 关闭内存驻留 arena。</summary>
    public static bool GraphCapture;
    public static bool CpuSyncSpinning;
    public static bool NoArena;

    /// <summary>
    /// 构造与生产一致的 SessionOptions。调用方负责释放；
    /// 需要把同一套配置交给第三方库（YoloSharp）而非直接建会话时使用。
    /// </summary>
    /// <param name="providers">both=生产默认 [Dml,Cpu]；dml=仅 DML（排查算子回退）；cpu=纯 CPU（对比基线）。</param>
    public static SessionOptions CreateSessionOptions(int dmlDeviceId, int cpuThreads,
        string providers = "both", bool enableProfiling = false,
        bool graphCapture = false, bool cpuSyncSpinning = false)
    {
        var options = new SessionOptions();
        try
        {
            options.EnableProfiling = enableProfiling;

            if (providers == "cpu")
            {
                options.IntraOpNumThreads = Math.Max(0, cpuThreads);
                options.AppendExecutionProvider_CPU();
                return options;
            }

            // DirectML 执行提供程序不支持内存模式优化或并行执行，必须禁用，否则创建会话报错。
            options.EnableMemoryPattern = false;
            options.ExecutionMode = ExecutionMode.ORT_SEQUENTIAL;
            options.AppendExecutionProvider_DML(dmlDeviceId);
            // 实验开关：命令列表捕获回放（消灭 per-run 派发）与 fence 自旋等待（降同步延迟）
            if (graphCapture || GraphCapture) options.AddSessionConfigEntry("ep.dml.enable_graph_capture", "true");
            if (cpuSyncSpinning || CpuSyncSpinning) options.AddSessionConfigEntry("ep.dml.enable_cpu_sync_spinning", "true");
            if (NoArena) options.AddSessionConfigEntry("ep.dml.disable_memory_arena", "1");
            // 与生产一致：CPU 兜底会话会设置全局线程数（0 = ORT 默认调度）
            if (cpuThreads >= 0)
            {
                options.IntraOpNumThreads = cpuThreads;
            }
            if (providers == "dml")
            {
                // 仅 DML：若与 both 差异明显，说明生产组合中有算子回退到 CPU
                return options;
            }
            options.AppendExecutionProvider_CPU();
            return options;
        }
        catch
        {
            options.Dispose();
            throw;
        }
    }

    public static InferenceSession CreateSession(string modelPath, int dmlDeviceId, int cpuThreads, string modelName,
        string providers = "both", bool enableProfiling = false, bool graphCapture = false, bool cpuSyncSpinning = false)
    {
        try
        {
            using var options = CreateSessionOptions(dmlDeviceId, cpuThreads, providers, enableProfiling,
                graphCapture, cpuSyncSpinning);
            return new InferenceSession(modelPath, options);
        }
        catch (Exception e)
        {
            var dml = Path.Combine(AppContext.BaseDirectory, "DirectML.dll");
            var ort = Path.Combine(AppContext.BaseDirectory, "onnxruntime.dll");
            throw new InvalidOperationException(
                $"创建会话失败（模型 {modelName}，设备 {dmlDeviceId}，providers={providers}）。\n" +
                $"DirectML.dll 存在: {File.Exists(dml)} ({dml})\n" +
                $"onnxruntime.dll 存在: {File.Exists(ort)} ({ort})\n" +
                $"错误: {e.Message}", e);
        }
    }

    /// <summary>打印模型输入/输出元数据（排查动态形状与 batch 支持）。</summary>
    public static void Describe(InferenceSession session, string name, Action<string> log)
    {
        static string Dims(int[]? d) => d == null || d.Length == 0 ? "?" : string.Join(",", d.Select(x => x < 0 ? "-" : x.ToString()));
        foreach (var (key, meta) in session.InputMetadata)
            log?.Invoke($"[元数据] {name} 输入 {key} [{Dims(meta.Dimensions)}] {meta.ElementType}");
        foreach (var (key, meta) in session.OutputMetadata)
            log?.Invoke($"[元数据] {name} 输出 {key} [{Dims(meta.Dimensions)}] {meta.ElementType}");
    }
}
