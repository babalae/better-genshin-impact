using BetterGenshinImpact.Core.Plugins.Pluggables;
using BetterGenshinImpact.Core.Recognition.ONNX;

namespace BetterGenshinImpact.Core.Recognition.OCR;

/// <summary>
/// 全局 OCR 的门面。OCR 已改为可插拔能力（见 Docs/design/plugin-system.md 第 17 节）：
/// 实际使用的模型由设置页“OCR 配置”中的选择决定（插件页“功能选择”显示同一个选择），
/// 默认是内置的 PP-OCRv6，切换后下一次调用即使用新模型。
/// </summary>
/// <remarks>
/// OCR 工厂不可以直接实例化，请使用 App.ServiceProvider 获取实例或直接使用 <see cref="Paddle"/>。
/// </remarks>
public class OcrFactory
{
    // public static IOcrService Media = Create(OcrEngineTypes.Media);

    /// <summary>
    /// 可插拔能力注册表，用于读取当前选择和创建独立实例。
    /// </summary>
    private readonly PluggableRegistry _registry;

    /// <summary>
    /// 创建 OCR 门面。
    /// </summary>
    /// <param name="ocr">OCR 可插拔能力的代理。</param>
    /// <param name="registry">可插拔能力注册表。</param>
    public OcrFactory(IOcrService ocr, PluggableRegistry registry)
    {
        PaddleOcr = ocr;
        _registry = registry;
    }

    /// <summary>
    /// 全局 OCR。名称沿用旧版本，实际模型按“OCR 配置”中的选择决定。
    /// </summary>
    public static IOcrService Paddle => Pluggable.Get<IOcrService>();

    /// <summary>
    /// 全局 OCR 的代理，长期不变。调用方可以把它保存在字段中，切换模型时也不会拿到已释放的实例。
    /// </summary>
    public IOcrService PaddleOcr { get; }

    /// <summary>
    /// 当前实际使用的 OCR 模型名称，供诊断显示。
    /// </summary>
    public string ActiveModelName => _registry.GetSelection<IOcrService>().Effective.DisplayName;

    /// <summary>
    /// 按当前选择创建一个独立的 OCR 实例，不影响全局 OCR，供推理验证和测速使用。调用方负责释放。
    /// </summary>
    /// <param name="sessions">绑定待验证推理运行时的会话工厂。</param>
    public IOcrService CreateDetached(BgiOnnxSessionFactory sessions) => _registry.CreateDetached<IOcrService>([sessions]);
}
