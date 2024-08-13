using BetterGenshinImpact.Core.Recorder;
using BetterGenshinImpact.Core.Script.Dependence;
using BetterGenshinImpact.Core.Script;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace BetterGenshinImpact.Core.Script.Dependence
{
    internal class AutoPathing(string rootPath)
    {
        public async Task Run(string json)
        {
            //await KeyMouseMacroPlayer.PlayMacro(json, CancellationContext.Instance.Cts.Token, false);
        }

        public async Task RunFile(string path)
        {
            //var json = await new LimitedFile(rootPath).ReadText(path);
            //await Run(json);
        }
    }
}
