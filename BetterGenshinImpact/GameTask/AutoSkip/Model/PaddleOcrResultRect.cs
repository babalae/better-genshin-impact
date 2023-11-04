using OpenCvSharp;

namespace BetterGenshinImpact.GameTask.AutoSkip.Model;

public record struct PaddleOcrResultRect(Rect Rect, string Text, float Score);