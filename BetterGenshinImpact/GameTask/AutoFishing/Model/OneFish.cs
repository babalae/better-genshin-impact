using OpenCvSharp;


namespace BetterGenshinImpact.GameTask.AutoFishing.Model;

public class OneFish
{

    public BigFishType FishType { get; set; }

    public Rect Rect { get; set; }

    public float Confidence { get; set; }

    public OneFish(string name, Rect rect, float confidence)
    {
        FishType = BigFishType.FromName(name);
        Rect = rect;
        Confidence = confidence;
    }
}