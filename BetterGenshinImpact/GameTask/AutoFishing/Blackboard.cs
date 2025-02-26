using System;
using System.Collections.Generic;
using System.Text;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Recognition.ONNX;
using BetterGenshinImpact.GameTask.AutoFishing.Model;
using Compunet.YoloV8;
using OpenCvSharp;

namespace BetterGenshinImpact.GameTask.AutoFishing
{
    /// <summary>
    /// 用于在钓鱼行为之间传递数据
    /// </summary>
    public class Blackboard
    {
        /// <summary>
        /// 已选择的鱼饵名
        /// </summary>
        internal string selectedBaitName = string.Empty;

        /// <summary>
        /// 鱼塘
        /// </summary>
        public Fishpond fishpond;

        /// <summary>
        /// 是否没有目标鱼
        /// </summary>
        internal bool noTargetFish;

        /// <summary>
        /// 拉条位置的识别框
        /// </summary>
        internal Rect fishBoxRect = Rect.Empty;

        /// <summary>
        /// 是否正在选鱼饵界面
        /// 此时有阴影遮罩，OpenCv的图像匹配会受干扰
        /// </summary>
        internal bool chooseBaitUIOpening = false;

        /// <summary>
        /// 镜头俯仰是否被行为重置
        /// 进入钓鱼模式后、以及提竿后，镜头的俯仰会被重置。进行相关动作前须优化俯仰角，避免鱼塘被脚下的悬崖遮挡。
        /// </summary>
        internal bool pitchReset = false;

        #region 分层暂放
        public YoloV8Predictor predictor;
        public Action<int> Sleep;

        public Blackboard(YoloV8Predictor predictor, Action<int> sleep)
        {
            this.predictor = predictor;
            Sleep = sleep;
        }
        #endregion

        internal virtual void Reset()
        {
            noTargetFish = false;
            fishBoxRect = Rect.Empty;
            chooseBaitUIOpening = false;
            pitchReset = false;
        }
    }
}
