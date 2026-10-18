using System;
using CsTrees;
using CsTrees.Visitors;
using Microsoft.Extensions.Logging;
using BetterGenshinImpact.Service;

namespace BetterGenshinImpact.GameTask.AutoFishing;

/// <summary>
/// CsTrees Visitor：在行为结束时自动截图。
/// 通过 Blackboard 获取当前帧，在行为的终态（Success/Failure）时保存截图。
/// </summary>
public class ScreenshotVisitor : VisitorBase
{
    private readonly ILogger _logger;

    /// <summary>
    /// 截图保存服务：负责 UID 遮挡与后台写盘，由钓鱼任务的创建方提供
    /// </summary>
    private readonly GameScreenshotService _screenshots;

    /// <summary>
    /// 创建关键帧截图访问器
    /// </summary>
    /// <param name="screenshots">截图保存服务</param>
    /// <param name="logger">日志</param>
    public ScreenshotVisitor(GameScreenshotService screenshots, ILogger logger) : base(full: false)
    {
        _screenshots = screenshots;
        _logger = logger;
    }

    public override void Run(Behaviour behaviour)
    {
        if (behaviour.Status == Status.Running)
            return;

        if (behaviour is IScreenshotBehaviour screenshotBehaviour)
        {
            var currentFrame = screenshotBehaviour.Screenshot.Get();
            if (currentFrame == null)
                return;

            var fileName = $"{DateTime.Now:yyyyMMddHHmmssfff}_{behaviour.GetType().Name}_{behaviour.Status}.png";
            _logger.LogInformation("保存截图: {Name}", fileName);

            // 服务在调用线程上复制画面后再后台写盘，下一轮截图释放这一帧不会影响保存
            _screenshots.Save(currentFrame.SrcMat, fileName);
        }
    }
}
