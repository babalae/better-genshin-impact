using System.Collections.Generic;
using System.Text;
using BetterGenshinImpact.Core.Recognition.OCR;
using OpenCvSharp;

namespace BetterGenshinImpact.Core.Recognition.ONNX.OCR.Paddle;

public sealed class TextBox
{
    public List<Point> Points { get; set; }
    public float Score { get; set; }

    public override string ToString()
    {
        return $"TextBox[score({Score}),[x: {Points[0].X}, y: {Points[0].Y}], [x: {Points[1].X}, y: {Points[1].Y}], [x: {Points[2].X}, y: {Points[2].Y}], [x: {Points[3].X}, y: {Points[3].Y}]]";
    }
}

public sealed class Angle
{
    public int Index { get; set; }
    public float Score { get; set; }
    public float Time { get; set; }

    public override string ToString()
    {
        string header = Index >= 0 ? "Angle" : "AngleDisabled";
        return $"{header}[Index({Index}), Score({Score}), Time({Time}ms)]";
    }
}

public sealed class TextLine
{
    public string Text { get; set; }
    public List<float> CharScores { get; set; }
    public float Time { get; set; }

    public override string ToString()
    {
        StringBuilder sb = new StringBuilder();
        CharScores.ForEach(x => sb.Append($"{x},"));
        return $"TextLine[Text({Text}),CharScores({sb.ToString()}),Time({Time}ms)]";
    }
}

public sealed class TextBlock
{
    public List<Point> BoxPoints { get; set; }
    public float BoxScore { get; set; }
    public int AngleIndex { get; set; }
    public float AngleScore { get; set; }
    public float AngleTime { get; set; }
    public string Text { get; set; }
    public List<float> CharScores { get; set; }
    public float CrnnTime { get; set; }
    public float BlockTime { get; set; }

    public override string ToString()
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("├─TextBlock");
        string textBox = $"│   ├──TextBox[score({BoxScore}),[x: {BoxPoints[0].X}, y: {BoxPoints[0].Y}], [x: {BoxPoints[1].X}, y: {BoxPoints[1].Y}], [x: {BoxPoints[2].X}, y: {BoxPoints[2].Y}], [x: {BoxPoints[3].X}, y: {BoxPoints[3].Y}]]";
        sb.AppendLine(textBox);
        string header = AngleIndex >= 0 ? "Angle" : "AngleDisabled";
        string angle = $"│   ├──{header}[Index({AngleIndex}), Score({AngleScore}), Time({AngleTime}ms)]";
        sb.AppendLine(angle);
        StringBuilder sbScores = new StringBuilder();
        CharScores.ForEach(x => sbScores.Append($"{x},"));
        string textLine = $"│   ├──TextLine[Text({Text}),CharScores({sbScores.ToString()}),Time({CrnnTime}ms)]";
        sb.AppendLine(textLine);
        sb.AppendLine($"│   └──BlockTime({BlockTime}ms)");
        return sb.ToString();
    }
}

public sealed class RapidOcrResult
{
    public List<TextBlock> TextBlocks { get; set; }
    public float DbNetTime { get; set; }

    // public Mat BoxImg { get; set; }
    public float DetectTime { get; set; }

    public string StrRes { get; set; }

    public override string ToString()
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("OcrResult");
        TextBlocks.ForEach(x => sb.Append(x));
        sb.AppendLine($"├─DbNetTime({DbNetTime}ms)");
        sb.AppendLine($"├─DetectTime({DetectTime}ms)");
        sb.AppendLine($"└─StrRes({StrRes})");
        return sb.ToString();
    }

    public OcrResult ToBgiOcrResult()
    {
        List<OcrResultRegion> regions = [];
        foreach (var block in TextBlocks)
        {
            var rotatedRect = Cv2.MinAreaRect(block.BoxPoints);
            regions.Add(new OcrResultRegion(rotatedRect, block.Text, block.BoxScore));
        }
        return new OcrResult([.. regions]);
    }
}
