using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BetterGenshinImpact.Core.Recognition
{
    public enum OcrEngineTypes
    {
        // 通用
        Media,
        Paddle,

        // 特定模型
        YasModel,
        YapModel
    }
}
