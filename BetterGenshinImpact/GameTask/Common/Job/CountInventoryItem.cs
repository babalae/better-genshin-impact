using BetterGenshinImpact.Core.Recognition.OCR;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask.AutoArtifactSalvage;
using BetterGenshinImpact.GameTask.GetGridIcons;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.GameTask.Model.GameUI;
using Fischless.WindowsInput;
using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntime;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BetterGenshinImpact.GameTask.Common.Job
{
    internal class CountInventoryItem : ISoloTask<int>
    {
        public string Name => "背包数物品";

        private readonly ILogger logger = App.GetLogger<CountInventoryItem>();
        private readonly InputSimulator input = Simulation.SendInput;
        private CancellationToken ct;
        private readonly GridScreenName gridScreenName;
        private readonly string itemName;

        public CountInventoryItem(GridScreenName gridScreenName, string itemName)
        {
            this.gridScreenName = gridScreenName;
            this.itemName = itemName;
        }

        public async Task<int> Start(CancellationToken ct)
        {
            this.ct = ct;

            logger.LogInformation("打开背包并在{grid}寻找{name}……", this.gridScreenName, this.itemName);
            await new ReturnMainUiTask().Start(ct);
            await AutoArtifactSalvageTask.OpenInventory(this.gridScreenName, input, logger, this.ct);

            using InferenceSession session = GridIconsAccuracyTestTask.LoadModel(out Dictionary<string, float[]> prototypes);

            using var ra = TaskControl.CaptureToRectArea();
            GridScreen gridScreen = new GridScreen(GridParams.Templates[this.gridScreenName], logger, ct);
            int? count = null;
            await foreach (ImageRegion itemRegion in gridScreen)
            {
                using Mat icon = itemRegion.SrcMat.GetGridIcon();
                var result = GridIconsAccuracyTestTask.Infer(icon, session, prototypes);
                string predName = result.Item1;
                if (predName == this.itemName)
                {
                    string numStr = itemRegion.SrcMat.GetGridItemIconText(OcrFactory.Paddle);
                    if (int.TryParse(numStr, out int num))
                    {
                        count = num;
                    }
                    else
                    {
                        count = -2;
                        logger.LogWarning("无法识别数量：{text}", numStr);
                    }

                    break;
                }
            }
            if (count == null)
            {
                count = -1;
                logger.LogInformation("没有找到{name}", this.itemName);
            }
            await new ReturnMainUiTask().Start(ct);

            return count.Value;
        }

        async Task ISoloTask.Start(CancellationToken ct)
        {
            await Start(ct);
        }
    }
}
