using OpenCvSharp;
using System.Linq;

namespace BetterGenshinImpact.Core.Recognition.OCR;

// <summary>
/// Represents a region detected in an OCR result using Paddle OCR.
/// </summary>
public record struct OcrResultRegion(RotatedRect Rect, string Text, float Score);

/// <summary>
/// Represents the OCR result of a paddle object detection model.
/// </summary>
public record OcrResult
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PaddleOcrResult"/> class with the specified <paramref name="Regions"/>.
    /// </summary>
    /// <param name="Regions">An array of <see cref="PaddleOcrResultRegion"/> objects representing the detected text regions.</param>
    public OcrResult(OcrResultRegion[] Regions)
    {
        this.Regions = Regions;
    }

    /// <summary>
    /// Gets an array of <see cref="PaddleOcrResultRegion"/> objects representing the detected text regions.
    /// </summary>
    /// <value>An array of <see cref="PaddleOcrResultRegion"/> objects representing the detected text regions.</value>
    public OcrResultRegion[] Regions { get; }

    /// <summary>
    /// Concatenates the text from each <see cref="PaddleOcrResultRegion"/> object in <see cref="Regions"/>
    /// and returns the resulting string, ordered by the region's center positions.
    /// </summary>
    /// <value>A string containing the concatenated text from each <see cref="PaddleOcrResultRegion"/> object
    /// in <see cref="Regions"/>, ordered by the region's center positions.</value>
    public string Text => string.Join("\n", Regions
        .OrderBy(x => x.Rect.Center.Y)
        .ThenBy(x => x.Rect.Center.X)
        .Select(x => x.Text));
}
