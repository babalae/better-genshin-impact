using System;
using System.Threading;
using BetterGenshinImpact.Core.BgiVision;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.Core.Recognition.ONNX;
using BetterGenshinImpact.GameTask.AutoFight.Assets;
using BetterGenshinImpact.GameTask.AutoFight.Model;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Exception;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.GameTask.Runtime;
using Microsoft.Extensions.Logging;
using OpenCvSharp;

namespace BetterGenshinImpact.Core.Script.Dependence;

/// <summary>
/// 脚本可以直接 new 的宿主类型的工厂，绑定了本次脚本的运行环境与配置。
/// <para>
/// BvPage、BvLocator、BvImage、KeyMouseHook、CombatScenes、Avatar 的 C# 构造函数需要 GameRuntime，
/// 脚本无法提供它。EngineExtend.InitHost 把本工厂注册为隐藏的宿主对象，再定义同名的 JS 构造函数转发到这里，
/// 脚本仍然写 <c>new BvPage()</c>、<c>new BvImage("xx:yy.png")</c> 等，参数与原来一致。
/// </para>
/// <para>
/// 需要运行环境的静态方法（CombatScenes.GetCombatScenesWithRetry / FromAvatars、Avatar.ThrowWhenDefeated / TpForRecover、
/// GameCaptureRegion.GameRegionClick / GameRegionMove / GameRegionMoveBy / GameRegion1080PPosClick / GameRegion1080PPosMove）
/// 也在这里提供绑定了运行环境的版本，由 JS 包装函数按原名暴露。
/// </para>
/// </summary>
public sealed class ScriptHostTypeFactory
{
    /// <summary>
    /// 本次脚本运行使用的运行环境
    /// </summary>
    private readonly GameRuntime _runtime;

    /// <summary>
    /// 配置根
    /// </summary>
    private readonly AllConfig _config;

    /// <summary>
    /// 创建绑定到指定运行环境的宿主类型工厂
    /// </summary>
    /// <param name="runtime">本次脚本运行使用的运行环境</param>
    /// <param name="config">配置根</param>
    public ScriptHostTypeFactory(GameRuntime runtime, AllConfig config)
    {
        _runtime = runtime;
        _config = config;
    }

    /// <summary>
    /// 对应脚本中的 <c>new BvPage(cancellationToken?)</c>
    /// </summary>
    /// <param name="cancellationToken">取消令牌，未传时与原来一样为 default</param>
    /// <returns>绑定到本次运行环境的页面对象</returns>
    public BvPage CreateBvPage(CancellationToken cancellationToken = default)
    {
        return new BvPage(_runtime, cancellationToken);
    }

    /// <summary>
    /// 对应脚本中的 <c>new BvLocator(recognitionObject, cancellationToken)</c>
    /// </summary>
    /// <param name="recognitionObject">识别对象</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>绑定到本次运行环境的定位器</returns>
    public BvLocator CreateBvLocator(RecognitionObject recognitionObject, CancellationToken cancellationToken)
    {
        return new BvLocator(_runtime, recognitionObject, cancellationToken);
    }

    /// <summary>
    /// 对应脚本中的 <c>new BvImage(templateAssert, roi?, threshold?)</c>
    /// </summary>
    /// <param name="templateAssert">素材名，格式为 "功能名:文件名"</param>
    /// <param name="roi">感兴趣区域</param>
    /// <param name="threshold">匹配阈值</param>
    /// <returns>按本次运行环境分辨率加载素材的模板对象</returns>
    public BvImage CreateBvImage(string templateAssert, Rect roi = default, double threshold = 0.8)
    {
        return new BvImage(_runtime, templateAssert, roi, threshold);
    }

    /// <summary>
    /// 对应脚本中的 <c>new KeyMouseHook()</c>
    /// </summary>
    /// <returns>绑定到本次运行环境的键鼠钩子</returns>
    public KeyMouseHook CreateKeyMouseHook()
    {
        return new KeyMouseHook(_runtime);
    }

    /// <summary>
    /// 对应脚本中的 <c>new CombatScenes(predictor?, autoFightAssets?, logger?)</c>（原第 4 个参数 systemInfo 已删除，改由运行环境提供）
    /// </summary>
    /// <param name="predictor">YOLO 预测器</param>
    /// <param name="autoFightAssets">自动战斗素材</param>
    /// <param name="logger">日志</param>
    /// <returns>绑定到本次运行环境的队伍场景</returns>
    public CombatScenes CreateCombatScenes(BgiYoloPredictor? predictor = null, AutoFightAssets? autoFightAssets = null, ILogger? logger = null)
    {
        return new CombatScenes(_runtime, _config, predictor, autoFightAssets, logger);
    }

    /// <summary>
    /// 对应脚本中的 <c>new Avatar(combatScenes, name, index, nameRect, manualSkillCd?)</c>
    /// </summary>
    /// <param name="combatScenes">所属队伍场景</param>
    /// <param name="name">角色名</param>
    /// <param name="index">队伍内序号</param>
    /// <param name="nameRect">名字所在矩形位置</param>
    /// <param name="manualSkillCd">手动配置的技能 CD，小于 0 为自动</param>
    /// <returns>绑定到本次运行环境的角色</returns>
    public Avatar CreateAvatar(CombatScenes combatScenes, string name, int index, Rect nameRect, double manualSkillCd = -1)
    {
        return new Avatar(_runtime, _config, combatScenes, name, index, nameRect, manualSkillCd);
    }

    /// <summary>
    /// 对应脚本中的静态方法 <c>CombatScenes.GetCombatScenesWithRetry()</c>
    /// </summary>
    /// <returns>识别出的队伍场景</returns>
    public CombatScenes GetCombatScenesWithRetry()
    {
        return CombatScenes.GetCombatScenesWithRetry(_runtime, _config);
    }

    /// <summary>
    /// 对应脚本中的静态方法 <c>CombatScenes.FromAvatars(avatars)</c>，同时接受宿主数组和 JS 数组
    /// </summary>
    /// <param name="avatars">要接管的队伍成员</param>
    /// <returns>接管这些成员的队伍场景</returns>
    public CombatScenes CombatScenesFromAvatars(object avatars)
    {
        var avatarList = BvPage.ParseCollection<Avatar>(avatars, nameof(avatars));
        return CombatScenes.FromAvatars(_runtime, _config, avatarList.ToArray());
    }

    /// <summary>
    /// 对应脚本中的静态方法 <c>Avatar.ThrowWhenDefeated(region, ct)</c>
    /// </summary>
    /// <param name="region">当前截图</param>
    /// <param name="ct">取消令牌</param>
    public void AvatarThrowWhenDefeated(ImageRegion region, CancellationToken ct)
    {
        Avatar.ThrowWhenDefeated(_runtime, _config, region, ct);
    }

    /// <summary>
    /// 对应脚本中的静态方法 <c>Avatar.TpForRecover(ct, retryException)</c>
    /// </summary>
    /// <param name="ct">取消令牌</param>
    /// <param name="retryException">恢复后抛出的重试异常</param>
    public void AvatarTpForRecover(CancellationToken ct, RetryException retryException)
    {
        Avatar.TpForRecover(_runtime, _config, ct, retryException);
    }

    /// <summary>
    /// 对应脚本中原来的静态方法 <c>GameCaptureRegion.GameRegionClick(posFunc)</c>。
    /// </summary>
    /// <param name="posFunc">根据游戏区域大小和缩放比例计算点击位置</param>
    public void GameRegionClick(Func<Size, double, (double, double)> posFunc)
    {
        _runtime.GameRegionClick(posFunc);
    }

    /// <summary>
    /// 对应脚本中原来的静态方法 <c>GameCaptureRegion.GameRegionMove(posFunc)</c>。
    /// </summary>
    /// <param name="posFunc">根据游戏区域大小和缩放比例计算鼠标位置</param>
    public void GameRegionMove(Func<Size, double, (double, double)> posFunc)
    {
        _runtime.GameRegionMove(posFunc);
    }

    /// <summary>
    /// 对应脚本中原来的静态方法 <c>GameCaptureRegion.GameRegionMoveBy(deltaFunc)</c>。
    /// </summary>
    /// <param name="deltaFunc">根据游戏区域大小和缩放比例计算相对位移</param>
    public void GameRegionMoveBy(Func<Size, double, (double, double)> deltaFunc)
    {
        _runtime.GameRegionMoveBy(deltaFunc);
    }

    /// <summary>
    /// 对应脚本中原来的静态方法 <c>GameCaptureRegion.GameRegion1080PPosClick(x, y)</c>（C# 侧已改为运行环境的扩展方法）
    /// </summary>
    /// <param name="cx">1080P 下的 X 坐标</param>
    /// <param name="cy">1080P 下的 Y 坐标</param>
    public void GameRegion1080PPosClick(double cx, double cy)
    {
        _runtime.GameRegion1080PPosClick(cx, cy);
    }

    /// <summary>
    /// 对应脚本中原来的静态方法 <c>GameCaptureRegion.GameRegion1080PPosMove(x, y)</c>（C# 侧已改为运行环境的扩展方法）
    /// </summary>
    /// <param name="cx">1080P 下的 X 坐标</param>
    /// <param name="cy">1080P 下的 Y 坐标</param>
    public void GameRegion1080PPosMove(double cx, double cy)
    {
        _runtime.GameRegion1080PPosMove(cx, cy);
    }
}
