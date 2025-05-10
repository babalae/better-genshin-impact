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
    /// paddleOCR V4 简体中文 检测模型
    /// </summary>
    public static readonly BgiOnnxModel PaddleOcrChDet =
        Register("ch_PP-OCRv4_det", @"Assets\Model\PaddleOCR\ch_PP-OCRv4_det\slim_model.onnx");

    /// <summary>
    /// paddleOCR V4 简体中文 识别模型
    /// </summary>
    public static readonly BgiOnnxModel PaddleOcrChRec =
        Register("ch_PP-OCRv4_rec", @"Assets\Model\PaddleOCR\ch_PP-OCRv4_rec\slim_model.onnx");

    /// <summary>
    /// paddleOCR V3 繁体中文 识别模型
    /// </summary>
    public static readonly BgiOnnxModel PaddleOcrChtRec =
        Register("chinese_cht_PP-OCRv3_rec", @"Assets\Model\PaddleOCR\chinese_cht_PP-OCRv3_rec_infer\slim_model.onnx");

    /// <summary>
    /// paddleOCR V3 英文 检测模型
    /// </summary>
    public static readonly BgiOnnxModel PaddleOcrEnDet =
        Register("en_PP-OCRv3_det", @"Assets\Model\PaddleOCR\en_PP-OCRv3_det_infer\slim_model.onnx");

    /// <summary>
    /// paddleOCR V3 拉丁文 识别模型
    /// </summary>
    public static readonly BgiOnnxModel PaddleOcrLatinRec =
        Register("latin_PP-OCRv3_rec", @"Assets\Model\PaddleOCR\latin_PP-OCRv3_rec_infer\slim_model.onnx");

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