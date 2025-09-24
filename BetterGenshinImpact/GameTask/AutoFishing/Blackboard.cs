using System;
using System.Collections.Generic;
using BetterGenshinImpact.Core.Recognition.ONNX;
using BetterGenshinImpact.GameTask.AutoFishing.Assets;
using BetterGenshinImpact.GameTask.AutoFishing.Model;
using OpenCvSharp;

namespace BetterGenshinImpact.GameTask.AutoFishing
{
    /// <summary>
    /// 用于在钓鱼行为之间传递数据
    /// </summary>
    public class Blackboard
    {
        /// <summary>
        /// 不钓啦
        /// </summary>
        public bool abort = false;

        /// <summary>
        /// 已选择的鱼饵类型
        /// </summary>
        public BaitType? selectedBait = null;

        /// <summary>
        /// 鱼塘
        /// </summary>
        public Fishpond fishpond;

        /// <summary>
        /// 是否没有抛竿落点
        /// </summary>
        public bool throwRodNoTarget;

        /// <summary>
        /// 没有抛竿落点的次数
        /// </summary>
        public int throwRodNoTargetTimes;

        /// <summary>
        /// 是否没有鱼饵适用的鱼
        /// </summary>
        public bool throwRodNoBaitFish;

        /// <summary>
        /// 抛竿无目标鱼失败列表
        /// 失败一次就加入一次鱼饵类型，列表中同名鱼饵的数量代表该种失败了几次
        /// </summary>
        public List<BaitType> throwRodNoBaitFishFailures = new List<BaitType>();

        /// <summary>
        /// 拉条位置的识别框
        /// </summary>
        internal Rect fishBoxRect;

        /// <summary>
        /// 是否正在选鱼饵界面
        /// 此时有阴影遮罩，OpenCv的图像匹配会受干扰
        /// </summary>
        public bool chooseBaitUIOpening = false;

        /// <summary>
        /// 选鱼饵失败列表
        /// 失败一次就加入一次鱼饵类型，列表中同名鱼饵的数量代表该种失败了几次
        /// </summary>
        public List<BaitType> chooseBaitFailures = new List<BaitType>();

        /// <summary>
        /// 镜头俯仰是否被行为重置
        /// 进入钓鱼模式后、以及提竿后，镜头的俯仰会被重置。进行相关动作前须优化俯仰角，避免鱼塘被脚下的悬崖遮挡。
        /// </summary>
        internal bool pitchReset = true;

        #region 分层暂放
        private readonly BgiYoloPredictor? predictor;
        internal BgiYoloPredictor Predictor
        {
            get
            {
                return predictor ?? throw new MissingMemberException();
            }
        }
        internal Action<int> Sleep { get; set; }

        private readonly AutoFishingAssets? autoFishingAssets;
        internal AutoFishingAssets AutoFishingAssets
        {
            get
            {
                return autoFishingAssets ?? throw new MissingMemberException();
            }
        }


        public Blackboard(BgiYoloPredictor? predictor = null, Action<int>? sleep = null, AutoFishingAssets? autoFishingAssets = null)
        {
            this.predictor = predictor;
            this.Sleep = sleep ?? (_ => throw new NotImplementedException());
            this.autoFishingAssets = autoFishingAssets;
        }
        #endregion

        internal virtual void Reset()
        {
            abort = false;
            throwRodNoTargetTimes = 0;
            throwRodNoBaitFishFailures = new List<BaitType>();
            fishBoxRect = default;
            chooseBaitUIOpening = false;
            chooseBaitFailures = new List<BaitType>();
            pitchReset = true;
            selectedBait = null;
        }
    }
}
