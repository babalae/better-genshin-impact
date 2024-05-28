using System;
using OpenCvSharp;

namespace BetterGenshinImpact.Core.Recognition.ONNX.OCR.Paddle;

class ScaleParam
{
    private int srcWidth;
    private int srcHeight;
    private int dstWidth;
    private int dstHeight;
    private float scaleWidth;
    private float scaleHeight;

    public int SrcWidth { get => srcWidth; set => srcWidth = value; }
    public int SrcHeight { get => srcHeight; set => srcHeight = value; }
    public int DstWidth { get => dstWidth; set => dstWidth = value; }
    public int DstHeight { get => dstHeight; set => dstHeight = value; }
    public float ScaleWidth { get => scaleWidth; set => scaleWidth = value; }
    public float ScaleHeight { get => scaleHeight; set => scaleHeight = value; }

    public ScaleParam(int srcWidth, int srcHeight, int dstWidth, int dstHeight, float scaleWidth, float scaleHeight)
    {
        this.srcWidth = srcWidth;
        this.srcHeight = srcHeight;
        this.dstWidth = dstWidth;
        this.dstHeight = dstHeight;
        this.scaleWidth = scaleWidth;
        this.scaleHeight = scaleHeight;
    }

    public override string ToString()
    {
        return $"sw:{this.srcWidth},sh:{this.srcHeight},dw:{this.dstWidth},dh:{this.dstHeight},{this.scaleWidth},{this.scaleHeight}";
    }

    public static ScaleParam GetScaleParam(Mat src, int dstSize)
    {
        int srcWidth, srcHeight, dstWidth, dstHeight;
        srcWidth = src.Cols;
        dstWidth = src.Cols;
        srcHeight = src.Rows;
        dstHeight = src.Rows;

        float scale = 1.0F;
        if (dstWidth > dstHeight)
        {
            scale = (float)dstSize / (float)dstWidth;
            dstWidth = dstSize;
            dstHeight = (int)((float)dstHeight * scale);
        }
        else
        {
            scale = (float)dstSize / (float)dstHeight;
            dstHeight = dstSize;
            dstWidth = (int)((float)dstWidth * scale);
        }
        if (dstWidth % 32 != 0)
        {
            dstWidth = (dstWidth / 32 - 1) * 32;
            dstWidth = Math.Max(dstWidth, 32);
        }
        if (dstHeight % 32 != 0)
        {
            dstHeight = (dstHeight / 32 - 1) * 32;
            dstHeight = Math.Max(dstHeight, 32);
        }
        float scaleWidth = (float)dstWidth / (float)srcWidth;
        float scaleHeight = (float)dstHeight / (float)srcHeight;
        return new ScaleParam(srcWidth, srcHeight, dstWidth, dstHeight, scaleWidth, scaleHeight);
    }
}
