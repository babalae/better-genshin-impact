using System;
using System.Globalization;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Recognition.OCR.Paddle;
using BetterGenshinImpact.Core.Recognition.ONNX;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.Core.Recognition.OCR;

public class OcrFactory : IDisposable
{
    // public static IOcrService Media = Create(OcrEngineTypes.Media);


    public static IOcrService Paddle => App.ServiceProvider.GetRequiredService<OcrFactory>().PaddleOcr;
    public IOcrService PaddleOcr => _paddleOcrService ??= Create(OcrEngineTypes.Paddle);

    private IOcrService? _paddleOcrService;
    private readonly ILogger<BgiOnnxFactory> _logger;
    private readonly OtherConfig _config;

    /// <summary>
    ///  OCR 工厂,不可以直接实例化,请使用 App.ServiceProvider获取实例
    /// </summary>
    /// <param name="otherConfig"></param>
    /// <param name="logger"></param>
    public OcrFactory(OtherConfig otherConfig, ILogger<BgiOnnxFactory> logger)
    {
        _logger = logger;
        _config = otherConfig;
    }

    /// <summary>
    /// 创建
    /// </summary>
    private IOcrService Create(OcrEngineTypes type)
    {
        var result = type switch
        {
            OcrEngineTypes.Paddle => CreatePaddleOcrInstance(),
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
        };
        _logger.LogDebug("创建了类型为 {Type} 的 OCR服务", nameof(type));
        return result;
    }

    private PaddleOcrService CreatePaddleOcrInstance()
    {
        return _config.OcrConfig.PaddleOcrModelConfig switch
        {
            PaddleOcrModelConfig.V5Auto =>
                new PaddleOcrService(App.ServiceProvider.GetRequiredService<BgiOnnxFactory>(),
                    PaddleOcrService.PaddleOcrModelType.FromCultureInfo(new CultureInfo(_config.GameCultureInfoName)) ??
                    PaddleOcrService.PaddleOcrModelType.V5),
            PaddleOcrModelConfig.V5 =>
                new PaddleOcrService(App.ServiceProvider.GetRequiredService<BgiOnnxFactory>(),
                    PaddleOcrService.PaddleOcrModelType.V5),
            PaddleOcrModelConfig.V4 =>
                new PaddleOcrService(App.ServiceProvider.GetRequiredService<BgiOnnxFactory>(),
                    PaddleOcrService.PaddleOcrModelType.V4),
            PaddleOcrModelConfig.V4En =>
                new PaddleOcrService(App.ServiceProvider.GetRequiredService<BgiOnnxFactory>(),
                    PaddleOcrService.PaddleOcrModelType.V4En),
            PaddleOcrModelConfig.V5Korean =>
                new PaddleOcrService(App.ServiceProvider.GetRequiredService<BgiOnnxFactory>(),
                    PaddleOcrService.PaddleOcrModelType.V5Korean),
            PaddleOcrModelConfig.V5Latin =>
                new PaddleOcrService(App.ServiceProvider.GetRequiredService<BgiOnnxFactory>(),
                    PaddleOcrService.PaddleOcrModelType.V5Latin),
            PaddleOcrModelConfig.V5Eslav =>
                new PaddleOcrService(App.ServiceProvider.GetRequiredService<BgiOnnxFactory>(),
                    PaddleOcrService.PaddleOcrModelType.V5Eslav),
            _ => throw new ArgumentOutOfRangeException(nameof(_config.OcrConfig.PaddleOcrModelConfig),
                _config.OcrConfig.PaddleOcrModelConfig, "不支持的 Paddle OCR 模型配置")
        };
    }


    public Task Unload()
    {
        if (_paddleOcrService is not IDisposable disposable) return Task.CompletedTask;
        try
        {
            disposable.Dispose();
            _paddleOcrService = null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "卸载 OCR 服务时发生错误");
        }

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        Unload().GetAwaiter().GetResult();
        GC.SuppressFinalize(this);
    }

    ~OcrFactory()
    {
        Dispose();
    }
}