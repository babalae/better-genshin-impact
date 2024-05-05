using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Recognition.OpenCv;
using CommunityToolkit.Mvvm.Messaging.Messages;
using CommunityToolkit.Mvvm.Messaging;
using OpenCvSharp;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BetterGenshinImpact.GameTask.AutoFight.Assets;
using BetterGenshinImpact.GameTask.AutoFishing.Assets;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Assets;
using BetterGenshinImpact.GameTask.AutoPick.Assets;
using BetterGenshinImpact.GameTask.AutoSkip.Assets;
using BetterGenshinImpact.GameTask.AutoWood.Assets;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using BetterGenshinImpact.GameTask.GameLoading;
using BetterGenshinImpact.GameTask.GameLoading.Assets;
using BetterGenshinImpact.GameTask.Placeholder;
using BetterGenshinImpact.GameTask.QuickSereniteaPot.Assets;
using BetterGenshinImpact.GameTask.QuickTeleport.Assets;
using BetterGenshinImpact.View.Drawable;

namespace BetterGenshinImpact.GameTask
{
    internal class GameTaskManager
    {
        public static Dictionary<string, ITaskTrigger>? TriggerDictionary { get; set; }

        /// <summary>
        /// 一定要在任务上下文初始化完毕后使用
        /// </summary>
        /// <returns></returns>
        public static List<ITaskTrigger> LoadTriggers()
        {
            ReloadAssets();
            TriggerDictionary = new Dictionary<string, ITaskTrigger>()
            {
                { "RecognitionTest", new TestTrigger() },
                { "GameLoading", new GameLoadingTrigger() },
                { "AutoPick", new AutoPick.AutoPickTrigger() },
                { "QuickTeleport", new QuickTeleport.QuickTeleportTrigger() },
                { "AutoSkip", new AutoSkip.AutoSkipTrigger() },
                { "AutoFishing", new AutoFishing.AutoFishingTrigger() }
            };

            var loadedTriggers = TriggerDictionary.Values.ToList();

            loadedTriggers.ForEach(i => i.Init());

            loadedTriggers = loadedTriggers.OrderByDescending(i => i.Priority).ToList();
            return loadedTriggers;
        }

        public static void RefreshTriggerConfigs()
        {
            if (TriggerDictionary is { Count: > 0 })
            {
                TriggerDictionary["AutoPick"].IsEnabled = TaskContext.Instance().Config.AutoPickConfig.Enabled;
                // 用于刷新AutoPick的黑白名单
                TriggerDictionary["AutoPick"].Init();
                TriggerDictionary["AutoSkip"].IsEnabled = TaskContext.Instance().Config.AutoSkipConfig.Enabled;
                TriggerDictionary["AutoFishing"].IsEnabled = TaskContext.Instance().Config.AutoFishingConfig.Enabled;
                // 钓鱼有很多变量要初始化，直接重新newZA
                if (TriggerDictionary["AutoFishing"].IsEnabled == false)
                {
                    TriggerDictionary["AutoFishing"].Init();
                }
                TriggerDictionary["QuickTeleport"].IsEnabled = TaskContext.Instance().Config.QuickTeleportConfig.Enabled;
                TriggerDictionary["GameLoading"].Init();
                // 清理画布
                WeakReferenceMessenger.Default.Send(new PropertyChangedMessage<object>(new object(), "RemoveAllButton", new object(), ""));
                VisionContext.Instance().DrawContent.ClearAll();
            }
            ReloadAssets();
        }

        public static void ReloadAssets()
        {
            AutoPickAssets.DestroyInstance();
            AutoSkipAssets.DestroyInstance();
            AutoFishingAssets.DestroyInstance();
            QuickTeleportAssets.DestroyInstance();
            AutoWoodAssets.DestroyInstance();
            AutoGeniusInvokationAssets.DestroyInstance();
            AutoFightAssets.DestroyInstance();
            ElementAssets.DestroyInstance();
            QuickSereniteaPotAssets.DestroyInstance();
            GameLoadingAssets.DestroyInstance();
        }

        /// <summary>
        /// 获取素材图片并缩放
        /// todo 支持多语言
        /// </summary>
        /// <param name="featName">任务名称</param>
        /// <param name="assertName">素材文件名</param>
        /// <param name="flags"></param>
        /// <returns></returns>
        /// <exception cref="FileNotFoundException"></exception>
        public static Mat LoadAssetImage(string featName, string assertName, ImreadModes flags = ImreadModes.Color)
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

            var mat = Mat.FromStream(File.OpenRead(filePath), flags);
            if (info.GameScreenSize.Width != 1920)
            {
                mat = ResizeHelper.Resize(mat, info.AssetScale);
            }

            return mat;
        }
    }
}
