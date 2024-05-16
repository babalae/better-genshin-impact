using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.GameTask.Model;
using Microsoft.VisualBasic;
using OpenCvSharp;

namespace BetterGenshinImpact.GameTask.AutoWindtrace.Assets;

public class AutoWindtraceAssets : BaseAssets<AutoWindtraceAssets>
{
    public RecognitionObject Conversation;

    public RecognitionObject EnterButton;

    public RecognitionObject JoinBututon;

    public RecognitionObject ConfirmJoinBututon;

    public RecognitionObject ConfirmStartBututon;

    private AutoWindtraceAssets()
    {
        Conversation = new RecognitionObject
        {
            Name = "Conversation",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage("AutoWindtrace", "conversation.png"),
            RegionOfInterest = new Rect(CaptureRect.Width / 2, CaptureRect.Height / 4,
                CaptureRect.Width / 2, CaptureRect.Height / 2),//[Help wanted] 我理解的方向：左上为原点，向右width增大，向下height增大。如果不对，请指出。
            DrawOnWindow = false
        }.InitTemplate();

        EnterButton = new RecognitionObject
        {
            Name = "EnterButton",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage("AutoWindtrace", "enter_button.png"),
            RegionOfInterest = new Rect(CaptureRect.Width / 2, 0, CaptureRect.Width / 2, CaptureRect.Height),
            DrawOnWindow = false
        }.InitTemplate();

        JoinBututon = new RecognitionObject
        {
            Name = "JoinBututon",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage("AutoWindtrace", "join_button.png"),
            RegionOfInterest = new Rect(CaptureRect.Width / 2, CaptureRect.Height / 2, CaptureRect.Width / 2, CaptureRect.Height / 2),
            DrawOnWindow = false
        }.InitTemplate();

        ConfirmJoinBututon = new RecognitionObject
        {
            Name = "ConfirmJoinBututon",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage("AutoWindtrace", "confirm_join_button.png"),
            RegionOfInterest = new Rect(0, CaptureRect.Height / 2, CaptureRect.Width, CaptureRect.Height / 2),
            DrawOnWindow = false
        }.InitTemplate();

        ConfirmStartBututon = new RecognitionObject
        {
            Name = "ConfirmStartBututon",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage("AutoWindtrace", "confirm_start_button.png"),
            RegionOfInterest = new Rect(0, CaptureRect.Height / 2, CaptureRect.Width, CaptureRect.Height - CaptureRect.Height / 2),
            DrawOnWindow = false
        }.InitTemplate();
    }
}
