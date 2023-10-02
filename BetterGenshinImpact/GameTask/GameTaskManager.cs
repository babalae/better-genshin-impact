using BetterGenshinImpact.Core.Config;
using OpenCvSharp;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BetterGenshinImpact.Core.Recognition.OpenCv;

namespace BetterGenshinImpact.GameTask
{
    internal class GameTaskManager
    {
        public static List<ITaskTrigger> LoadTriggers()
        {
            List<ITaskTrigger> loadedTriggers = new()
            {
                new AutoPick.AutoPickTrigger(),
                new AutoSkip.AutoSkipTrigger(),
                new AutoFishing.AutoFishingTrigger()
            };

            loadedTriggers.ForEach(i => i.Init());

            return loadedTriggers.OrderByDescending(i => i.Priority).ToList();
        }

        /// <summary>
        /// 获取素材图片并缩放
        /// todo 支持多语言
        /// </summary>
        /// <param name="featName">任务名称</param>
        /// <param name="assertName">素材文件名</param>
        /// <returns></returns>
        /// <exception cref="FileNotFoundException"></exception>
        public static Mat LoadAssertImage(string featName, string assertName)
        {
            var info = TaskContext.Instance().SystemInfo;
            var assetsFolder = Global.Absolute($@"GameTask\{featName}\Assets\{info.GameScreenSize.Width}x{info.GameScreenSize.Height}");
            if (!Directory.Exists(assetsFolder))
            {
                assetsFolder = Global.Absolute($@"GameTask\{featName}\Assets\1920x1080");
            }
            if (!Directory.Exists(assetsFolder))
            {
                throw new FileNotFoundException($"未找到{featName}的素材文件夹");
            }
            var filePath = Path.Combine(assetsFolder, assertName);
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException($"未找到{featName}中的{assertName}文件");
            }
            var mat = new Mat(filePath, ImreadModes.AnyColor);
            if (info.GameScreenSize.Width != 1920)
            {
                mat = ResizeHelper.Resize(mat, info.AssetScale);
            }
            return mat;
        }
    }
}