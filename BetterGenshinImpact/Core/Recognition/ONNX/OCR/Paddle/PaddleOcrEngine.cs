using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace BetterGenshinImpact.Core.Recognition.ONNX.OCR.Paddle;

/// <summary>
/// edit from: https://github.com/RapidAI/RapidOCRCSharp
/// </summary>
public class PaddleOcrEngine
{
    public bool isPartImg { get; set; }
    public bool isDebugImg { get; set; }
    private DbNet dbNet;
    private AngleNet angleNet;
    private CrnnNet crnnNet;

    public PaddleOcrEngine()
    {
        dbNet = new DbNet();
        angleNet = new AngleNet();
        crnnNet = new CrnnNet();
    }

    public void InitModels(string detPath, string clsPath, string recPath, string keysPath, int numThread)
    {
        try
        {
            dbNet.InitModel(detPath, numThread);
            angleNet.InitModel(clsPath, numThread);
            crnnNet.InitModel(recPath, keysPath, numThread);
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex.Message + ex.StackTrace);
            throw;
        }
    }

    public Mat ToChannel3Mat(Mat mat)
    {
        return mat.Channels() switch
        {
            4 => mat.CvtColor(ColorConversionCodes.RGBA2BGR),
            1 => mat.CvtColor(ColorConversionCodes.GRAY2RGB),
            3 => mat,
            var x => throw new Exception($"Unexpect src channel: {x}, allow: (1/3/4)")
        };
    }

    public RapidOcrResult Run(Mat mat)
    {
        using var channel3 = ToChannel3Mat(mat);
        return Run(channel3, 0, 1024, 0.5f, 0.3f, 1.6f, false, false);
    }

    public string OnlyRecognizerRun(Mat mat)
    {
        using var channel3 = ToChannel3Mat(mat);
        var textLine = crnnNet.GetTextLine(channel3);
        return textLine.Text;
    }

    public RapidOcrResult Run(Mat originSrc, int padding, int maxSideLen, float boxScoreThresh, float boxThresh,
        float unClipRatio, bool doAngle, bool mostAngle)
    {
        int originMaxSide = Math.Max(originSrc.Cols, originSrc.Rows);

        int resize;
        if (maxSideLen <= 0 || maxSideLen > originMaxSide)
        {
            resize = originMaxSide;
        }
        else
        {
            resize = maxSideLen;
        }
        resize += 2 * padding;
        Rect paddingRect = new Rect(padding, padding, originSrc.Cols, originSrc.Rows);
        Mat paddingSrc = OcrUtils.MakePadding(originSrc, padding);

        ScaleParam scale = ScaleParam.GetScaleParam(paddingSrc, resize);

        return RunOnce(paddingSrc, paddingRect, scale, boxScoreThresh, boxThresh, unClipRatio, doAngle, mostAngle);
    }

    private RapidOcrResult RunOnce(Mat src, Rect originRect, ScaleParam scale, float boxScoreThresh, float boxThresh,
        float unClipRatio, bool doAngle, bool mostAngle)
    {
        // Mat textBoxPaddingImg = src.Clone();
        // int thickness = OcrUtils.GetThickness(src);
        // Debug.WriteLine("=====Start detect=====");
        var startTicks = DateTime.Now.Ticks;

        // Debug.WriteLine("---------- step: dbNet getTextBoxes ----------");
        var textBoxes = dbNet.GetTextBoxes(src, scale, boxScoreThresh, boxThresh, unClipRatio);
        var dbNetTime = (DateTime.Now.Ticks - startTicks) / 10000F;

        // Debug.WriteLine($"TextBoxesSize({textBoxes.Count})");
        textBoxes.ForEach(x => Debug.WriteLine(x));
        //Debug.WriteLine($"dbNetTime({dbNetTime}ms)");

        // Debug.WriteLine("---------- step: drawTextBoxes ----------");
        // OcrUtils.DrawTextBoxes(textBoxPaddingImg, textBoxes, thickness);
        //Cv2.Imshow("ResultPadding", textBoxPaddingImg);

        //---------- getPartImages ----------
        List<Mat> partImages = OcrUtils.GetPartImages(src, textBoxes);
        if (isPartImg)
        {
            for (int i = 0; i < partImages.Count; i++)
            {
                Cv2.ImShow($"PartImg({i})", partImages[i]);
            }
        }

        // Debug.WriteLine("---------- step: angleNet getAngles ----------");
        List<Angle> angles = angleNet.GetAngles(partImages, doAngle, mostAngle);
        //angles.ForEach(x => Debug.WriteLine(x));

        //Rotate partImgs
        for (int i = 0; i < partImages.Count; ++i)
        {
            if (angles[i].Index == 1)
            {
                partImages[i] = OcrUtils.MatRotateClockWise180(partImages[i]);
            }
            if (isDebugImg)
            {
                Cv2.ImShow($"DebugImg({i})", partImages[i]);
            }
        }

        // Debug.WriteLine("---------- step: crnnNet getTextLines ----------");
        List<TextLine> textLines = crnnNet.GetTextLines(partImages);
        //textLines.ForEach(x => Debug.WriteLine(x));

        List<TextBlock> textBlocks = new List<TextBlock>();
        for (int i = 0; i < textLines.Count; ++i)
        {
            TextBlock textBlock = new()
            {
                BoxPoints = textBoxes[i].Points,
                BoxScore = textBoxes[i].Score,
                AngleIndex = angles[i].Index,
                AngleScore = angles[i].Score,
                AngleTime = angles[i].Time,
                Text = textLines[i].Text,
                CharScores = textLines[i].CharScores,
                CrnnTime = textLines[i].Time,
                BlockTime = angles[i].Time + textLines[i].Time
            };
            textBlocks.Add(textBlock);
        }
        //textBlocks.ForEach(x => Debug.WriteLine(x));

        var endTicks = DateTime.Now.Ticks;
        var fullDetectTime = (endTicks - startTicks) / 10000F;
        //Debug.WriteLine($"fullDetectTime({fullDetectTime}ms)");

        //cropped to original size
        // Mat boxImg = new Mat(textBoxPaddingImg, originRect);

        StringBuilder strRes = new StringBuilder();
        textBlocks.ForEach(x => strRes.AppendLine(x.Text));

        var ocrResult = new RapidOcrResult
        {
            TextBlocks = textBlocks,
            DbNetTime = dbNetTime,
            // ocrResult.BoxImg = boxImg;
            DetectTime = fullDetectTime,
            StrRes = strRes.ToString()
        };

        return ocrResult;
    }
}
