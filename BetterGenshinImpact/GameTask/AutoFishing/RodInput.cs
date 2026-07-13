using static TorchSharp.torch;

namespace BetterGenshinImpact.GameTask.AutoFishing;

public record RodInput
{
    public double rod_x1;
    public double rod_x2;
    public double rod_y1;
    public double rod_y2;
    public double fish_x1;
    public double fish_x2;
    public double fish_y1;
    public double fish_y2;
    public int fish_label;
}