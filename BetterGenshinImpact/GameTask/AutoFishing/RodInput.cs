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

public static class RodInputHelper
{
    public static Tensor ToTensor(this RodInput input)
    {
        return tensor(new double[,] { { input.rod_x1, input.rod_x2, input.rod_y1, input.rod_y2, input.fish_x1, input.fish_x2, input.fish_y1, input.fish_y2, input.fish_label } }, dtype: ScalarType.Float64);
    }
}