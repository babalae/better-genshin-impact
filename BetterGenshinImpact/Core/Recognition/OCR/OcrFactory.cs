using System;
using System.Globalization;
using System.Threading;
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

    /// <summary>
    /// 获取支持模糊匹配的 OCR 服务。
    /// 若引擎原生支持 IOcrMatchService 则直接返回，否则回退到普通 OCR + 字符串相似度。
    /// 访问此属性会触发 Paddle 引擎的懒加载。
    /// </summary>
    public static IOcrMatchService PaddleMatch
    {
        get
        {
            var factory = App.ServiceProvider.GetRequiredService<OcrFactory>();
            var service = factory.PaddleOcr;
            if (service is IOcrMatchService matchService)
                return matchService;
            var fallback = new OcrMatchFallbackService(service);
            return Interlocked.CompareExchange(ref factory._paddleOcrMatchFallback, fallback, null)
                   ?? fallback;
        }
    }

    private IOcrService? _paddleOcrService;
    private IOcrMatchService? _paddleOcrMatchFallback;
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
        var allowDuplicateChar = _config.AllowDuplicateChar;
        var threshold = (float)_config.PaddleOcrThreshold;
        var factory = App.ServiceProvider.GetRequiredService<BgiOnnxFactory>();
        return _config.PaddleOcrModelConfig switch
        {
            PaddleOcrModelConfig.V4Auto =>
                new PaddleOcrService(factory,
                    PaddleOcrService.PaddleOcrModelType.FromCultureInfoV4(GetCultureInfo()) ??
                    PaddleOcrService.PaddleOcrModelType.V4,
                    allowDuplicateChar, threshold),
            PaddleOcrModelConfig.V5Auto =>
                new PaddleOcrService(factory,
                    PaddleOcrService.PaddleOcrModelType.FromCultureInfo(GetCultureInfo()) ??
                    PaddleOcrService.PaddleOcrModelType.V5,
                    allowDuplicateChar, threshold),
            PaddleOcrModelConfig.V5 =>
                new PaddleOcrService(factory, PaddleOcrService.PaddleOcrModelType.V5, allowDuplicateChar, threshold),
            PaddleOcrModelConfig.V4 =>
                new PaddleOcrService(factory, PaddleOcrService.PaddleOcrModelType.V4, allowDuplicateChar, threshold),
            PaddleOcrModelConfig.V4En =>
                new PaddleOcrService(factory, PaddleOcrService.PaddleOcrModelType.V4En, allowDuplicateChar, threshold),
            PaddleOcrModelConfig.V5Korean =>
                new PaddleOcrService(factory, PaddleOcrService.PaddleOcrModelType.V5Korean, allowDuplicateChar, threshold),
            PaddleOcrModelConfig.V5Latin =>
                new PaddleOcrService(factory, PaddleOcrService.PaddleOcrModelType.V5Latin, allowDuplicateChar, threshold),
            PaddleOcrModelConfig.V5Eslav =>
                new PaddleOcrService(factory, PaddleOcrService.PaddleOcrModelType.V5Eslav, allowDuplicateChar, threshold),
            _ => throw new ArgumentOutOfRangeException(nameof(_config.PaddleOcrModelConfig),
                _config.PaddleOcrModelConfig, "不支持的 Paddle OCR 模型配置")
        };
    }


    public Task Unload()
    {
        _paddleOcrMatchFallback = null;
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