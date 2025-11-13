using BetterGenshinImpact.Core.Recognition.OpenCv;
using OpenCvSharp;

namespace BetterGenshinImpact.Test.Cv;

public class ImageDifferenceDetectorTest
{
    public static void Test()
    {
        int i = ImageDifferenceDetector.FindMostDifferentImage([
            Cv2.ImRead(@"E:\1.png", ImreadModes.Grayscale),
            Cv2.ImRead(@"E:\2.png", ImreadModes.Grayscale),
            Cv2.ImRead(@"E:\3.png", ImreadModes.Grayscale),
            Cv2.ImRead(@"E:\4.png", ImreadModes.Grayscale)
        ]);
        
        Console.WriteLine($"差异最大的图片索引是: {i}");
    }
}