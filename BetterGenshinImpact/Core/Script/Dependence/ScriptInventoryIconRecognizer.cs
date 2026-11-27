using System;
using System.Threading;
using BetterGenshinImpact.GameTask.Common.Job;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.Service.AccountRecords;
using Newtonsoft.Json;
using OpenCvSharp;

namespace BetterGenshinImpact.Core.Script.Dependence;

/// <summary>狛荷屋脚本图标识别：本轮惰性复用模型，退出时释放，不写库存或缓存。</summary>
public sealed class ScriptInventoryIconRecognizer : IDisposable
{
    /// <summary>防止识别与退出释放同时使用模型。</summary>
    private readonly object _gate = new();
    /// <summary>公共物品目录，用于校验模型结果。</summary>
    private readonly InventoryCatalog _catalog;
    /// <summary>模型创建入口，测试可替换为离线识别器。</summary>
    private readonly Func<IItemIconRecognizer> _factory;
    /// <summary>本轮执行取消令牌。</summary>
    private readonly CancellationToken _ct;
    /// <summary>退出清理登记，正常返回和取消都会触发。</summary>
    private readonly CancellationTokenRegistration _registration;
    /// <summary>首次有效识别时才加载的模型。</summary>
    private IItemIconRecognizer? _recognizer;
    /// <summary>退出后禁止重新创建模型。</summary>
    private bool _disposed;

    /// <summary>绑定本轮脚本生命周期；不在构造时加载模型。</summary>
    public ScriptInventoryIconRecognizer(InventoryCatalog catalog, CancellationToken ct,
        Func<IItemIconRecognizer>? factory = null)
    {
        _catalog = catalog;
        _factory = factory ?? ItemIconRecognizerFactory.CreateConfigured;
        _ct = ct;
        _registration = ct.Register(Release);
    }

    /// <summary>识别调用方裁好的单个图标，保留调用方图像所有权。</summary>
    public string Identify(ImageRegion image)
    {
        using var operation = ScriptHostOperations.Enter();
        ArgumentNullException.ThrowIfNull(image);
        lock (_gate)
        {
            _ct.ThrowIfCancellationRequested();
            ObjectDisposedException.ThrowIf(_disposed, this);
            var input = image.SrcMat;
            if (input.Empty() || input.Depth() != MatType.CV_8U || input.Channels() is not (3 or 4))
                throw new ArgumentException("图标需要非空的 8 位 BGR 或 BGRA 图像。", nameof(image));
            using var bgr = new Mat();
            if (input.Channels() == 4) Cv2.CvtColor(input, bgr, ColorConversionCodes.BGRA2BGR);
            else input.CopyTo(bgr);
            using var normalized = new Mat();
            Cv2.Resize(bgr, normalized, new Size(125, 125), 0, 0, InterpolationFlags.Cubic);
            _recognizer ??= _factory();
            var name = _recognizer.Recognize(normalized);
            _ct.ThrowIfCancellationRequested();
            return name != null && _catalog.Items.TryGetValue(name, out var category)
                ? JsonConvert.SerializeObject(new { name, category }) : "null";
        }
    }

    /// <summary>等待当前识别结束后释放会话，重复调用无副作用。</summary>
    private void Release()
    {
        lock (_gate)
        {
            _disposed = true;
            _recognizer?.Dispose();
            _recognizer = null;
        }
    }

    /// <summary>独立调用方可主动释放；脚本正常退出由取消登记清理。</summary>
    public void Dispose()
    {
        _registration.Dispose();
        Release();
    }
}
