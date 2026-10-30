using System;
using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using BetterGenshinImpact.GameTask.Model.Area;
using OpenCvSharp;

namespace BetterGenshinImpact.GameTask.QuestVoice;

/// <summary>“已离开对话”的正向证据：看到主界面派蒙菜单，且没有确认框、不在对话界面。</summary>
public static class DialogueExitDetector
{
    /// <summary>只在后台借用调用方复制的帧；模板失败表示未知，绝不推定退出。</summary>
    public static bool HasConfirmedExit(Mat frame)
    {
        try
        {
            // 区域仅拥有新的 Mat 头，不拥有外层帧的生命周期，也不再复制像素。
            using var capture = new GameCaptureRegion(new Mat(frame, new Rect(0, 0, frame.Width, frame.Height)), 0, 0);
            using var region = capture.DeriveTo1080P();
            // Bv.IsInMainUi包含复苏弹窗OCR；此处仅复用其正向HUD模板，避免引入另一条OCR路径。
            using var main = region.Find(ElementRecognition.Get("PaimonMenu", region));
            if (!main.IsExist()) return false;
            using var confirm = region.Find(RecognitionAssets.Get("AutoFight", "Confirm", region));
            return confirm.IsEmpty() && !Bv.IsInTalkUi(region);
        }
        catch (Exception) { return false; }
    }
}
