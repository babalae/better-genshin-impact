using System.Linq;
using OpenCvSharp;

namespace BetterGenshinImpact.Core.Recognition.OCR;

/// <summary>
///     Represents a region detected in an OCR result using Paddle OCR.
///     Sdcb.PaddleOCR
/// </summary>
public record struct OcrResultRegion(RotatedRect Rect, string Text, float Score);

public readonly record struct OcrRecognizerResult
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="OcrRecognizerResult" /> struct.
    /// </summary>
    /// <param name="text">The recognized text from the image.</param>
    /// <param name="score">The confidence score of the text recognition.</param>
    public OcrRecognizerResult(string text, float score)
    {
        Text = text;
        Score = score;
    }

    /// <summary>
    ///     The recognized text from the image.
    /// </summary>
    public string Text { get; init; }

    /// <summary>
    ///     The confidence score of the text recognition.
    /// </summary>
    public float Score { get; init; }
}

/// <summary>
///     Represents the OCR result of a paddle object detection model.
/// </summary>
public record OcrResult
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="OcrResult" /> class with the specified <paramref name="Regions" />.
    /// </summary>
    /// <param name="Regions">An array of <see cref="OcrResultRegion" /> objects representing the detected text regions.</param>
    public OcrResult(OcrResultRegion[] Regions)
    {
        this.Regions = Regions;
    }

    /// <summary>
    ///     Gets an array of <see cref="OcrResultRegion" /> objects representing the detected text regions.
    /// </summary>
    /// <value>An array of <see cref="OcrResultRegion" /> objects representing the detected text regions.</value>
    public OcrResultRegion[] Regions { get; }

    /// <summary>
    ///     Concatenates the text from each <see cref="OcrResultRegion" /> object in <see cref="Regions" />
    ///     and returns the resulting string, ordered by the region's center positions.
    /// </summary>
    /// <value>
    ///     A string containing the concatenated text from each <see cref="OcrResultRegion" /> object
    ///     in <see cref="Regions" />, ordered by the region's center positions.
    /// </value>
    public string Text => string.Join("\n", Regions
        .OrderBy(x => x.Rect.Center.Y)
        .ThenBy(x => x.Rect.Center.X)
        .Select(x => x.Text));
}