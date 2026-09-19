using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.IO.Hashing;
using BetterGenshinImpact.Core.Config;

namespace BetterGenshinImpact.Core.Recognition.ONNX;

public class BgiOnnxModel
{
    /// <summary>
    /// ONNX 缓存根目录
    /// </summary>
    public static readonly string ModelCacheRelativePath = "Cache";

    private static readonly List<BgiOnnxModel> RegisteredModels = [];

    public string Name { get; private init; }
    public string ModelRelativePath { get; private init; }
    public string ModelPath => Global.Absolute(ModelRelativePath);
    public string CacheRelativePath { get; private set; } = ModelCacheRelativePath;
    public string CachePath => Global.Absolute(CacheRelativePath);
    public string ModelHash { get; private init; }
    public string CacheIdentifier => $"{Name}_{ModelHash}";

    #region 模型注册

    // 模型注册在这里，这样可以方便预先对模型预热和缓存管理等操作，避免冲突。
    // 硬编码虽然不那么优雅，但是也没想到什么好的解决办法
    /// <summary>
    /// yap文字识别
    /// </summary>
    public static readonly BgiOnnxModel YapModelTraining =
        Register("YapModelTraining", @"Assets\Model\Yap\model_training.onnx");

    /// <summary>
    /// 钓鱼模型
    /// </summary>
    public static readonly BgiOnnxModel BgiFish =
        Register("BgiFish", @"Assets\Model\Fish\bgi_fish.onnx");

    /// <summary>
    /// 秘境中古树
    /// </summary>
    public static readonly BgiOnnxModel BgiTree =
        Register("BgiTree", @"Assets\Model\Domain\bgi_tree.onnx");

    /// <summary>
    /// 用于捡东西等的大世界模型
    /// </summary>
    public static readonly BgiOnnxModel BgiWorld = Register("BgiWorld", @"Assets\Model\World\bgi_world.onnx");

    /// <summary>
    /// 矿物识别模型
    /// </summary>
    public static readonly BgiOnnxModel BgiMine =
        Register("BgiMine", @"Assets\Model\Mine\bgi_mine.onnx");

    /// <summary>
    /// 角色识别
    /// </summary>
    public static readonly BgiOnnxModel BgiAvatarSide =
        Register("BgiAvatarSide", @"Assets\Model\Common\avatar_side_classify_sim.onnx");

    /// <summary>
    /// Q技能冷却分类模型
    /// </summary>
    public static readonly BgiOnnxModel BgiQClassify =
        Register("BgiQClassify", @"Assets\Model\Common\q_classify_sim.onnx");

    /// <summary>
    /// 队伍配置角色头像识别模型
    /// </summary>
    public static readonly BgiOnnxModel AvatarGridIcon =
        Register("AvatarGridIcon", @"Assets\Model\AvatarGridIcon\avatar.onnx");

    /// <summary>
    /// Silero 人声检测模型
    /// </summary>
    public static readonly BgiOnnxModel SileroVad =
        Register("SileroVad", @"Assets\Model\Vad\silero_vad.onnx");

    /// <summary>
    /// paddleOCR V4 检测模型
    /// </summary>
    public static readonly BgiOnnxModel PaddleOcrDetV4 =
        Register("PpOcrDetV4", @"Assets\Model\PaddleOCR\Det\V4\PP-OCRv4_mobile_det_infer\slim.onnx");

    /// <summary>
    /// paddleOCR V5 检测模型
    /// </summary>
    public static readonly BgiOnnxModel PaddleOcrDetV5 =
        Register("PpOcrDetV5", @"Assets\Model\PaddleOCR\Det\V5\PP-OCRv5_mobile_det_infer\slim.onnx");

    /// <summary>
    /// paddleOCR V6 检测模型
    /// </summary>
    public static readonly BgiOnnxModel PaddleOcrDetV6 =
        Register("PpOcrDetV6", @"Assets\Model\PaddleOCR\Det\V6\PP-OCRv6_small_det_infer\slim.onnx");

    /// <summary>
    /// paddleOCR V4 识别模型
    /// </summary>
    public static readonly BgiOnnxModel PaddleOcrRecV4 =
        Register("PpOcrRecV4", @"Assets\Model\PaddleOCR\Rec\V4\PP-OCRv4_mobile_rec_infer\slim.onnx");

    /// <summary>
    /// paddleOCR V4 英文/数字 识别模型
    /// </summary>
    public static readonly BgiOnnxModel PaddleOcrRecV4En =
        Register("PpOcrRecV4En", @"Assets\Model\PaddleOCR\Rec\V4\en_PP-OCRv4_mobile_rec_infer\slim.onnx");

    /// <summary>
    /// paddleOCR V5 识别模型
    /// </summary>
    public static readonly BgiOnnxModel PaddleOcrRecV5 =
        Register("PpOcrRecV5", @"Assets\Model\PaddleOCR\Rec\V5\PP-OCRv5_mobile_rec_infer\slim.onnx");

    /// <summary>
    /// paddleOCR V6 识别模型
    /// </summary>
    public static readonly BgiOnnxModel PaddleOcrRecV6 =
        Register("PpOcrRecV6", @"Assets\Model\PaddleOCR\Rec\V6\PP-OCRv6_small_rec_infer\slim.onnx");

    /// <summary>
    /// paddleOCR V5 拉丁文 识别模型
    /// </summary>
    public static readonly BgiOnnxModel PaddleOcrRecV5Latin =
        Register("PpOcrRecV5Latin", @"Assets\Model\PaddleOCR\Rec\V5\latin_PP-OCRv5_mobile_rec_infer\slim.onnx");

    /// <summary>
    /// paddleOCR V5 斯拉夫文 识别模型
    /// </summary>
    public static readonly BgiOnnxModel PaddleOcrRecV5Eslav =
        Register("PpOcrRecV5Eslav", @"Assets\Model\PaddleOCR\Rec\V5\eslav_PP-OCRv5_mobile_rec_infer\slim.onnx");

    /// <summary>
    /// paddleOCR V5 韩文 识别模型
    /// </summary>
    public static readonly BgiOnnxModel PaddleOcrRecV5Korean =
        Register("PpOcrRecV5Korean", @"Assets\Model\PaddleOCR\Rec\V5\korean_PP-OCRv5_mobile_rec_infer\slim.onnx");

    #endregion

    private BgiOnnxModel(string name, string modelRelativePath)
    {
        Name = name;
        ModelRelativePath = modelRelativePath;
        ModelHash = ModelHashCompute(ModelPath);
    }

    private static string ModelHashCompute(string modelPath)
    {
        using var modelStream = File.OpenRead(modelPath);
        var hash = new XxHash3();
        hash.Append(modelStream);
        return hash.GetCurrentHashAsUInt64().ToString("x16");
    }

    /// <summary>
    /// 为当前模型指定执行提供程序对应的缓存目录
    /// </summary>
    /// <param name="cacheDirectory">缓存根目录下的子目录</param>
    /// <returns>缓存目录的绝对路径</returns>
    public string CachePathDeploy(string cacheDirectory)
    {
        CacheRelativePath = Path.Combine(ModelCacheRelativePath, cacheDirectory);
        return CachePath;
    }

    public static bool ModelExistence(BgiOnnxModel model)
    {
        return File.Exists(model.ModelPath);
    }


    /// <summary>
    /// 获取全部已注册的模型文件
    /// </summary>
    public static ImmutableList<BgiOnnxModel> ModelsGetAll()
    {
        return RegisteredModels.ToImmutableList();
    }

    private static BgiOnnxModel Register(string name, string modelRelativePath)
    {
        var model = new BgiOnnxModel(name, modelRelativePath);
        RegisteredModels.Add(model);
        return model;
    }
}
