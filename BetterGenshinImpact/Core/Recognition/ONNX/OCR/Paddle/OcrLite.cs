using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace BetterGenshinImpact.Core.Recognition.ONNX.OCR.Paddle;

public class OcrLite
{
    public bool isPartImg { get; set; }
    public bool isDebugImg { get; set; }
    private DbNet dbNet;
    private AngleNet angleNet;
    private CrnnNet crnnNet;

    public OcrLite()
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
            throw ex;
        }
    }

    public OcrResult Detect(string img, int padding, int maxSideLen, float boxScoreThresh, float boxThresh,
        float unClipRatio, bool doAngle, bool mostAngle)
    {
        Mat originSrc = Cv2.ImRead(img, ImreadModes.Color);//default : BGR
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

        return DetectOnce(paddingSrc, paddingRect, scale, boxScoreThresh, boxThresh, unClipRatio, doAngle, mostAngle);
    }

    private OcrResult DetectOnce(Mat src, Rect originRect, ScaleParam scale, float boxScoreThresh, float boxThresh,
        float unClipRatio, bool doAngle, bool mostAngle)
    {
        Mat textBoxPaddingImg = src.Clone();
        int thickness = OcrUtils.GetThickness(src);
        Debug.WriteLine("=====Start detect=====");
        var startTicks = DateTime.Now.Ticks;

        Debug.WriteLine("---------- step: dbNet getTextBoxes ----------");
        var textBoxes = dbNet.GetTextBoxes(src, scale, boxScoreThresh, boxThresh, unClipRatio);
        var dbNetTime = (DateTime.Now.Ticks - startTicks) / 10000F;

        Debug.WriteLine($"TextBoxesSize({textBoxes.Count})");
        textBoxes.ForEach(x => Debug.WriteLine(x));
        //Debug.WriteLine($"dbNetTime({dbNetTime}ms)");

        Debug.WriteLine("---------- step: drawTextBoxes ----------");
        OcrUtils.DrawTextBoxes(textBoxPaddingImg, textBoxes, thickness);
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

        Debug.WriteLine("---------- step: angleNet getAngles ----------");
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

        Debug.WriteLine("---------- step: crnnNet getTextLines ----------");
        List<TextLine> textLines = crnnNet.GetTextLines(partImages);
        //textLines.ForEach(x => Debug.WriteLine(x));

        List<TextBlock> textBlocks = new List<TextBlock>();
        for (int i = 0; i < textLines.Count; ++i)
        {
            TextBlock textBlock = new TextBlock();
            textBlock.BoxPoints = textBoxes[i].Points;
            textBlock.BoxScore = textBoxes[i].Score;
            textBlock.AngleIndex = angles[i].Index;
            textBlock.AngleScore = angles[i].Score;
            textBlock.AngleTime = angles[i].Time;
            textBlock.Text = textLines[i].Text;
            textBlock.CharScores = textLines[i].CharScores;
            textBlock.CrnnTime = textLines[i].Time;
            textBlock.BlockTime = angles[i].Time + textLines[i].Time;
            textBlocks.Add(textBlock);
        }
        //textBlocks.ForEach(x => Debug.WriteLine(x));

        var endTicks = DateTime.Now.Ticks;
        var fullDetectTime = (endTicks - startTicks) / 10000F;
        //Debug.WriteLine($"fullDetectTime({fullDetectTime}ms)");

        //cropped to original size
        Mat boxImg = new Mat(textBoxPaddingImg, originRect);

        StringBuilder strRes = new StringBuilder();
        textBlocks.ForEach(x => strRes.AppendLine(x.Text));

        OcrResult ocrResult = new OcrResult();
        ocrResult.TextBlocks = textBlocks;
        ocrResult.DbNetTime = dbNetTime;
        ocrResult.BoxImg = boxImg;
        ocrResult.DetectTime = fullDetectTime;
        ocrResult.StrRes = strRes.ToString();

        return ocrResult;
    }
}
