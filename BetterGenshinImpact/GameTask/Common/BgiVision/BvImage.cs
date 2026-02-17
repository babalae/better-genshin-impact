using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.GameTask.Model.Area;

namespace BetterGenshinImpact.GameTask.Common.BgiVision;

/// <summary>
/// 一些基础图像识别操作
/// </summary>
public static partial class Bv
{
    /// <summary>
    /// 等待图像被找到
    /// </summary>
    /// <param name="ro">识别对象</param>
    /// <param name="ct">任务取消令牌</param>
    /// <param name="retryTimes">重试次数</param>
    /// <param name="delayMs">重试间隔</param>
    /// <returns></returns>
    public static async Task<bool> WaitUntilFound(RecognitionObject ro, CancellationToken ct, int retryTimes = 5, int delayMs = 1000)
    {
        return await NewRetry.WaitForAction(() => TaskControl.CaptureToRectArea().Find(ro).IsExist(), ct, retryTimes, delayMs);
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="ro"></param>
    /// <param name="ct"></param>
    /// <param name="retryTimes"></param>
    /// <param name="delayMs"></param>
    /// <returns></returns>
    public static async Task<bool> ClickUntilFound(RecognitionObject ro, CancellationToken ct, int retryTimes = 5, int delayMs = 1000)
    {
        return await NewRetry.WaitForAction(() =>
        {
            var region = TaskControl.CaptureToRectArea();
            var foundRa = region.Find(ro);
            if (foundRa.IsExist())
            {
                foundRa.Click();
                return true;
            }

            return false;
        }, ct, retryTimes, delayMs);
    }


    /// <summary>
    /// 是否找到对应的识别对象
    /// </summary>
    /// <param name="region">图像区域</param>
    /// <param name="ro">识别目标</param>
    /// <returns></returns>
    public static bool Find(ImageRegion region, RecognitionObject ro)
    {
        return region.Find(ro).IsExist();
    }
}