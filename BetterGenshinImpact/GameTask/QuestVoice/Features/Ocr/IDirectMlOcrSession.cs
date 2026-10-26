using System;
using System.Collections.Generic;
using System.Threading;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Ocr;

namespace BetterGenshinImpact.GameTask.QuestVoice.Features.Ocr;

/// <summary>由引擎独占并串行调用的原生边界。</summary>
internal interface IDirectMlOcrSession : IDisposable
{
    /// <summary>模型身份和已验证的provider证据。</summary>
    OcrModelInfo Model { get; }
    /// <summary>直到原生计算与输出读取完成后才归还。</summary>
    IReadOnlyList<OcrText> Run(IReadOnlyList<OcrTensor> tensors, CancellationToken token);
}
