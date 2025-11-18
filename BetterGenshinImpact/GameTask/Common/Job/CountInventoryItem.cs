using BetterGenshinImpact.Core.Recognition.OCR;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask.AutoArtifactSalvage;
using BetterGenshinImpact.GameTask.GetGridIcons;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.GameTask.Model.GameUI;
using BetterGenshinImpact.View.Drawable;
using Fischless.WindowsInput;
using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntime;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BetterGenshinImpact.GameTask.Common.Job
{
    internal class CountInventoryItem : ISoloTask<object>
    {
        public string Name => "背包数物品";

        private readonly ILogger logger = App.GetLogger<CountInventoryItem>();
        private readonly InputSimulator input = Simulation.SendInput;
        private CancellationToken ct;
        private readonly GridScreenName gridScreenName;
        private readonly string? itemName;
        private readonly IEnumerable<string>? itemNames;

        public CountInventoryItem(GridScreenName gridScreenName, string? itemName = null, IEnumerable<string>? itemNames = null)
        {
            this.gridScreenName = gridScreenName;
            if (itemName != null && itemNames != null)
            {
                throw new ArgumentException($"参数{nameof(itemName)}和{nameof(itemNames)}不能同时使用");
            }
            if (itemName == null && itemNames == null)
            {
                throw new ArgumentException($"参数{nameof(itemName)}和{nameof(itemNames)}不能同时为空");
            }
            if (itemNames != null && !itemNames.Any())
            {
                throw new ArgumentException($"参数{nameof(itemNames)}不能为空序列");
            }
            this.itemName = itemName;
            this.itemNames = itemNames;
        }

        public async Task<object> Start(CancellationToken ct)
        {
            this.ct = ct;

            if (this.itemName != null)
            {
                logger.LogInformation("打开背包并在{grid}寻找{name}……", this.gridScreenName, this.itemName!);
            }
            else
            {
                logger.LogInformation("打开背包并在{grid}寻找{first}等{count}类物品……", this.gridScreenName, this.itemNames!.First(), this.itemNames!.Count());
            }
            await new ReturnMainUiTask().Start(ct);
            await AutoArtifactSalvageTask.OpenInventory(this.gridScreenName, input, logger, this.ct);

            using InferenceSession session = GridIconsAccuracyTestTask.LoadModel(out Dictionary<string, float[]> prototypes);

            object result;
            if (this.itemName != null)
            {
                result = await FindOne(session, prototypes);
            }
            else
            {
                result = await FindMulti(session, prototypes);
            }

            await new ReturnMainUiTask().Start(ct);

            return result;
        }

        private async Task<int> FindOne(InferenceSession session, Dictionary<string, float[]> prototypes)
        {
            GridScreen gridScreen = new GridScreen(GridParams.Templates[this.gridScreenName], logger, ct);
            gridScreen.OnAfterTurnToNewPage += GridScreen.DrawItemsAfterTurnToNewPage;
            gridScreen.OnBeforeScroll += () => VisionContext.Instance().DrawContent.ClearAll();
            int? count = null;
            try
            {
                await foreach ((ImageRegion pageRegion, Rect itemRect) in gridScreen)
                {
                    using ImageRegion itemRegion = pageRegion.DeriveCrop(itemRect);
                    using Mat icon = itemRegion.SrcMat.GetGridIcon();
                    var result = GridIconsAccuracyTestTask.Infer(icon, session, prototypes);
                    if (result.Item1 == null)
                    {
                        continue;
                    }
                    string predName = result.Item1;
                    if (predName == this.itemName!)
                    {
                        string numStr = itemRegion.SrcMat.GetGridItemIconText(OcrFactory.Paddle);
                        if (int.TryParse(numStr, out int num))
                        {
                            count = num;
                        }
                        else
                        {
                            logger.LogWarning("无法识别数量：{text}", numStr);
                            count = -2;
                        }
                        break;
                    }
                }
            }
            finally
            {
                VisionContext.Instance().DrawContent.ClearAll();
            }
            if (count == null)
            {
                count = -1;
                logger.LogInformation("没有找到{name}", this.itemName!);
            }
            return count.Value;
        }

        private async Task<Dictionary<string, int>> FindMulti(InferenceSession session, Dictionary<string, float[]> prototypes)
        {
            Dictionary<string, int> itemsCountDic = new Dictionary<string, int>();
            List<string> notFoundItemNames = this.itemNames!.ToList();

            GridScreen gridScreen = new GridScreen(GridParams.Templates[this.gridScreenName], logger, ct);
            gridScreen.OnAfterTurnToNewPage += GridScreen.DrawItemsAfterTurnToNewPage;
            gridScreen.OnBeforeScroll += () => VisionContext.Instance().DrawContent.ClearAll();
            try
            {
                await foreach ((ImageRegion pageRegion, Rect itemRect) in gridScreen)
                {
                    using ImageRegion itemRegion = pageRegion.DeriveCrop(itemRect);
                    using Mat icon = itemRegion.SrcMat.GetGridIcon();
                    var result = GridIconsAccuracyTestTask.Infer(icon, session, prototypes);
                    if (result.Item1 == null)
                    {
                        continue;
                    }
                    string predName = result.Item1;
                    if (this.itemNames!.Contains(predName) && !itemsCountDic!.ContainsKey(predName))
                    {
                        int count;
                        string numStr = itemRegion.SrcMat.GetGridItemIconText(OcrFactory.Paddle);
                        if (int.TryParse(numStr, out int num))
                        {
                            count = num;
                        }
                        else
                        {
                            logger.LogWarning("无法识别数量：{text}", numStr);
                            count = -2;
                        }

                        if (!itemsCountDic!.TryAdd(predName, count))
                        {
                            logger.LogWarning("重复的名称：{name}", predName);
                        }

                        notFoundItemNames.RemoveAll(n => n == predName);

                        if (notFoundItemNames.Count <= 0)
                        {
                            break;
                        }
                    }
                }
            }
            finally
            {
                VisionContext.Instance().DrawContent.ClearAll();
            }

            if (notFoundItemNames.Count > 0)
            {
                logger.LogInformation("没有找到{name}", String.Join(", ", notFoundItemNames));
            }
            return itemsCountDic;
        }

        async Task ISoloTask.Start(CancellationToken ct)
        {
            await Start(ct);
        }
    }
}
