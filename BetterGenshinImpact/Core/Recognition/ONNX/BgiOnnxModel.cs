using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using BetterGenshinImpact.Core.Config;

namespace BetterGenshinImpact.Core.Recognition.ONNX;

public class BgiOnnxModel
{
    /// <summary>
    /// 模型使用的缓存文件的相对目录
    /// </summary>
    public static readonly string ModelCacheRelativePath = Path.Combine("Cache", Global.Version, "Model");

    private static readonly List<BgiOnnxModel> RegisteredModels = [];
    public string Name { get; private init; }
    public string ModelRelativePath { get; private init; }
    public string ModalPath => Global.Absolute(ModelRelativePath);
    public string CacheRelativePath { get; private init; }
    public string CachePath => Global.Absolute(CacheRelativePath);

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
    public static readonly BgiOnnxModel BgiFish = Register("BgiFish", @"Assets\Model\Fish\bgi_fish.onnx");

    /// <summary>
    /// 秘境中古树
    /// </summary>
    public static readonly BgiOnnxModel BgiTree = Register("BgiTree", @"Assets\Model\Domain\bgi_tree.onnx");

    /// <summary>
    /// 用于捡东西等的大世界模型
    /// </summary>
    public static readonly BgiOnnxModel BgiWorld = Register("BgiTree", @"Assets\Model\World\bgi_world.onnx");

    /// <summary>
    /// 角色识别
    /// </summary>
    public static readonly BgiOnnxModel BgiAvatarSide =
        Register("BgiAvatarSide", @"Assets\Model\Common\avatar_side_classify_sim.onnx");

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

    private BgiOnnxModel(string name, string modelRelativePath, string cacheRelativePath)
    {
        Name = name;
        ModelRelativePath = modelRelativePath;
        CacheRelativePath = cacheRelativePath;
    }

    public static bool IsModelExist(BgiOnnxModel model)
    {
        return File.Exists(model.ModalPath);
    }


    /// <summary>
    /// 获取全部已注册的模型文件
    /// </summary>
    /// <returns></returns>
    public static ImmutableList<BgiOnnxModel> GetAll()
    {
        return RegisteredModels.ToImmutableList();
    }

    private static BgiOnnxModel Register(string name, string modelRelativePath)
    {
        return Register(name, modelRelativePath, Path.Combine(ModelCacheRelativePath, name));
    }

    private static BgiOnnxModel Register(string name, string modelRelativePath, string cacheRelativePath)
    {
        var model = new BgiOnnxModel(name, modelRelativePath, cacheRelativePath);
        var cachePath = model.CachePath;
        if (!Directory.Exists(cachePath))
        {
            Directory.CreateDirectory(cachePath);
        }

        RegisteredModels.Add(model);
        return model;
    }
}