using OpenCvSharp;
namespace BetterGenshinImpact.Core.Recognition.OpenCv.TemplateMatch;

public static class TemplateMatchHelper
{
    public static (Point, double) MatchTemplate(Mat image, Mat template, TemplateMatchModes method, Mat? mask = null)
    {
        using var result = new Mat();
        Cv2.MatchTemplate(image,template, result, method, mask);
        Point loc;
        double val;
        if (method is TemplateMatchModes.SqDiff or TemplateMatchModes.SqDiffNormed)
        {
            Cv2.MinMaxLoc(result, out val, out _, out loc, out _);
        }
        else
        {
            Cv2.MinMaxLoc(result, out _, out val, out _, out loc);
        }
        return (loc, val);
    }
    
    public static (Point, double) MatchTemplateNormalized(Mat image, Mat template, TemplateMatchModes method, Mat? mask =  null)
    {
        var normalizer = new TemplateMatchNormalizer(template, mask, method);
        (var loc, normalizer.Value) = MatchTemplate(image, template, method, mask);
        return (loc, normalizer.Confidence());
    }

    public static (Point2f, double) MatchTemplateSubPix(Mat image, Mat template, TemplateMatchModes method, Mat? mask)
    {
        using var result = new Mat();
        Cv2.MatchTemplate(image,template, result, method, mask);
        Point loc;
        double val;
        if (method is TemplateMatchModes.SqDiff or TemplateMatchModes.SqDiffNormed)
        {
            Cv2.MinMaxLoc(result, out val, out _, out loc, out _);
        }
        else
        {
            Cv2.MinMaxLoc(result, out _, out val, out _, out loc);
        }
        return (SubPixMatch.Fit(result, loc), val);
    }
}