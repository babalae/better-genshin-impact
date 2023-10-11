using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.GameTask.AutoFishing.Assets;

namespace BetterGenshinImpact.GameTask.Placeholder;

/// <summary>
/// 一个用于开发测试的识别、或者全局占位触发器
/// 这个触发器启动的时候，直接独占
/// </summary>
public class TestTrigger : ITaskTrigger
{
    public string Name => "自定义占位触发器";
    public bool IsEnabled { get; set; }
    public int Priority => 9999;
    public bool IsExclusive { get; private set; }

    private readonly RecognitionObject _optionButtonRo;

    private readonly AutoFishingAssets _autoFishingAssets;

    public TestTrigger()
    {
        var info = TaskContext.Instance().SystemInfo;
        _optionButtonRo = new RecognitionObject
        {
            Name = "OptionButton",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssertImage("AutoSkip", "option.png"),
            DrawOnWindow = true
        }.InitTemplate();
        _autoFishingAssets = new AutoFishingAssets();
    }

    public void Init()
    {
        IsEnabled = false;
        IsExclusive = true;
    }

    public void OnCapture(CaptureContent content)
    {
        //content.CaptureRectArea.Find(_optionButtonRo, (optionButtonRectArea) =>
        //{
        //});

        //content.CaptureRectArea.Find(_autoFishingAssets.BaitButtonRo, (rectArea) =>
        //{
        //});

        //content.CaptureRectArea.Find(_autoFishingAssets.WaitBiteButtonRo, (rectArea) =>
        //{
        //});
    }
}