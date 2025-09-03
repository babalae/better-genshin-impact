using BetterGenshinImpact.Core.Recognition.OCR;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Text;

namespace BetterGenshinImpact.GameTask.Model.GameUI
{
    public static class GridExtensions
    {
        /// <summary>
        /// 获取GridItem图标底部的文字
        /// </summary>
        /// <param name="mat"></param>
        /// <returns></returns>
        public static string GetGridItemIconText(this Mat mat, IOcrService ocrService)
        {
            Mat subMat = mat.SubMat(mat.Height * 128 / 153, mat.Height * 150 / 153, mat.Width * 5 / 125, mat.Width * 120 / 125);
            using Mat resize = subMat.Resize(new Size(subMat.Width * 2, subMat.Height * 2));
            return ocrService.Ocr(resize);
        }
    }
}
