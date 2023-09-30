using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BetterGenshinImpact.Core.Recognition;

public enum RecognitionType
{
    None,
    TemplateMatch,
    ColorMatch,
    Ocr,
    Detect
}