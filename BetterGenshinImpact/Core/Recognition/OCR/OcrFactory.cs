using System;
using System.Threading.Tasks;

namespace BetterGenshinImpact.Core.Recognition.OCR;

public class OcrFactory
{
    // public static IOcrService Media = Create(OcrEngineTypes.Media);
    public static IOcrService Paddle { get; private set; } = Create(OcrEngineTypes.Paddle);

    public static IOcrService Create(OcrEngineTypes type, string? cultureInfoName = null)
    {
        return type switch
        {
            OcrEngineTypes.Paddle => new PaddleOcrService(cultureInfoName),
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
        };
    }

    public static async Task ChangeCulture(string cultrueInfoName)
    {
        await Task.Run(() =>
        {
            lock (Paddle)
            {
                Paddle = Create(OcrEngineTypes.Paddle, cultrueInfoName);
                GC.Collect();
            }
        });
    }
}