using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.AutoPathing.Model;
using BetterGenshinImpact.GameTask.AutoPathing;
using BetterGenshinImpact.GameTask.Common;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.Common.BgiVision;

namespace BetterGenshinImpact.GameTask.AutoFishing
{
    public class AutoFishingTask : ISoloTask
    {
        private readonly ILogger<AutoFishingTrigger> _logger = App.GetLogger<AutoFishingTrigger>();
        public string Name => "全、都给你钓完";

        private CancellationToken ct;

        public Task Start(CancellationToken ct)
        {
            this.ct = ct;

            var task = PathingTask.BuildFromFilePath(Global.Absolute(@$"GameTask\Common\Element\Assets\Json\冒险家协会_蒙德.json"));   // todo 制作一些钓点的json
            var pathingTask = new PathExecutor(ct)
            {
                PartyConfig = new PathingPartyConfig
                {
                    Enabled = true,
                    AutoSkipEnabled = true
                },
                EndAction = region => Bv.FindFAndPress(region, "钓鱼")
            };
            pathingTask.Pathing(task).Wait();

            AutoFishingTrigger trigger = new AutoFishingTrigger();  // todo 试试能不能通过复用BehaviourTree的形式来做
            trigger.Init();
            while (true)
            {
                if (this.ct is { IsCancellationRequested: true })
                {
                    _logger.LogInformation("取消自动任务");
                    break;
                }

                var ra = TaskControl.CaptureToRectArea(forceNew: true);
                trigger.OnCapture(new CaptureContent(ra.SrcBitmap, 0, 0));

                if (trigger.BehaviourTree.Status == BehaviourTree.BehaviourStatus.Succeeded)
                {
                    _logger.LogInformation("→ 钓鱼任务结束");
                    break;
                }
            }

            return Task.CompletedTask;
        }
    }
}
