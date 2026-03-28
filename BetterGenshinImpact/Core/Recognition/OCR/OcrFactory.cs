using System;
using System.Globalization;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Recognition.OCR.Paddle;
using BetterGenshinImpact.Core.Recognition.ONNX;
using BetterGenshinImpact.GameTask;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.Core.Recognition.OCR;

public class OcrFactory : IDisposable
{
    // public static IOcrService Media = Create(OcrEngineTypes.Media);


    public static IOcrService Paddle => App.ServiceProvider.GetRequiredService<OcrFactory>().PaddleOcr;
    private IOcrService PaddleOcr => _paddleOcrService ??= Create(OcrEngineTypes.Paddle);

    private IOcrService? _paddleOcrService;
    private readonly ILogger<BgiOnnxFactory> _logger;
    private readonly OtherConfig.Ocr _config;

    /// <summary>
    ///  OCR 工厂,不可以直接实例化,请使用 App.ServiceProvider获取实例
    /// </summary>
    /// <param name="logger"></param>
    public OcrFactory(ILogger<BgiOnnxFactory> logger)
    {
        _logger = logger;
        _config = GetConfig();
    }

    /// <summary>
    /// 创建
    /// </summary>
    private IOcrService Create(OcrEngineTypes type)
    {
        var result = type switch
        {
            OcrEngineTypes.Paddle => CreatePaddleOcrInstance(),
            _ => throw new ArgumentOutOfRangeException(Enum.GetName(type), type, "不支持的 OCR 引擎类型")
        };
        _logger.LogDebug("创建了类型为 {Type} 的 OCR服务", Enum.GetName(type));
        return result;
    }

    /// <summary>
    /// 获取 OCR 配置
    /// 为了单元测试
    /// </summary>
    /// <returns></returns>
    private OtherConfig.Ocr GetConfig()
    {
        try
        {
            // 直接使用配置
            return TaskContext.Instance().Config.OtherConfig.OcrConfig;
        }
        catch (Exception e)
        {
            // 如果配置获取失败，使用默认配置
            _logger.LogWarning(e, "获取 OCR 配置失败，使用默认配置");
            return new OtherConfig.Ocr();
        }
    }

    /// <summary>
    /// 若果配置中没有设置文化信息，则使用默认的文化信息
    /// 为了单元测试
    /// </summary>
    /// <returns></returns>
    private CultureInfo GetCultureInfo()
    {
        try
        {
            return new CultureInfo(TaskContext.Instance().Config.OtherConfig.GameCultureInfoName);
        }
        catch (Exception e)
        {
            var result = new CultureInfo(new OtherConfig().GameCultureInfoName);
            _logger.LogInformation("获取游戏文化信息失败，使用默认文化信息: {CultureInfo}", result.Name);
            return result;
        }
    }

    private PaddleOcrService CreatePaddleOcrInstance()
    {
        return _config.PaddleOcrModelConfig switch
        {
            PaddleOcrModelConfig.V4Auto =>
                new PaddleOcrService(App.ServiceProvider.GetRequiredService<BgiOnnxFactory>(),
                    PaddleOcrService.PaddleOcrModelType.FromCultureInfoV4(GetCultureInfo()) ??
                    PaddleOcrService.PaddleOcrModelType.V4),
            PaddleOcrModelConfig.V5Auto =>
                new PaddleOcrService(App.ServiceProvider.GetRequiredService<BgiOnnxFactory>(),
                    PaddleOcrService.PaddleOcrModelType.FromCultureInfo(GetCultureInfo()) ??
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
            _ => throw new ArgumentOutOfRangeException(nameof(_config.PaddleOcrModelConfig),
                _config.PaddleOcrModelConfig, "不支持的 Paddle OCR 模型配置")
        };
    }


    public Task Unload()
    {
        if (_paddleOcrService is not IDisposable disposable)
        {
            _paddleOcrService = null;
            return Task.CompletedTask;
        }
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