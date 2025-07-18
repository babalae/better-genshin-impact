using System;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Recognition.OCR;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask.Model.Area;
using Microsoft.Extensions.Logging;
using OpenCvSharp;

namespace BetterGenshinImpact.GameTask.Common.BgiVision;

/// <summary>
/// Playwright-inspired element class representing a found element
/// </summary>
public class BgiElement : IDisposable
{
    private static readonly ILogger Logger = App.GetLogger<BgiElement>();
    private readonly BgiPage _page;
    private readonly CancellationToken _cancellationToken;
    private bool _disposed = false;

    /// <summary>
    /// The result area of the found element
    /// </summary>
    public ResultArea ResultArea { get; }

    /// <summary>
    /// X coordinate of the element center
    /// </summary>
    public int X => ResultArea.X + ResultArea.Width / 2;

    /// <summary>
    /// Y coordinate of the element center
    /// </summary>
    public int Y => ResultArea.Y + ResultArea.Height / 2;

    /// <summary>
    /// Width of the element
    /// </summary>
    public int Width => ResultArea.Width;

    /// <summary>
    /// Height of the element
    /// </summary>
    public int Height => ResultArea.Height;

    /// <summary>
    /// Bounding rectangle of the element
    /// </summary>
    public Rect BoundingBox => new Rect(ResultArea.X, ResultArea.Y, ResultArea.Width, ResultArea.Height);

    internal BgiElement(ResultArea resultArea, BgiPage page, CancellationToken cancellationToken)
    {
        ResultArea = resultArea;
        _page = page;
        _cancellationToken = cancellationToken;
    }

    /// <summary>
    /// Click the element at its center
    /// </summary>
    public void Click()
    {
        ResultArea.Click();
    }

    /// <summary>
    /// Click the element at its center with delay
    /// </summary>
    /// <param name="delay">Delay after click in milliseconds</param>
    public async Task ClickAsync(int delay = 100)
    {
        Click();
        if (delay > 0)
        {
            await _page.Wait(delay);
        }
    }

    /// <summary>
    /// Right-click the element at its center
    /// </summary>
    public void RightClick()
    {
        Simulation.SendInput.Mouse.MoveMouseTo(X, Y).RightButtonClick();
    }

    /// <summary>
    /// Right-click the element at its center with delay
    /// </summary>
    /// <param name="delay">Delay after click in milliseconds</param>
    public async Task RightClickAsync(int delay = 100)
    {
        RightClick();
        if (delay > 0)
        {
            await _page.Wait(delay);
        }
    }

    /// <summary>
    /// Double-click the element at its center
    /// </summary>
    public void DoubleClick()
    {
        Simulation.SendInput.Mouse.MoveMouseTo(X, Y).LeftButtonDoubleClick();
    }

    /// <summary>
    /// Double-click the element at its center with delay
    /// </summary>
    /// <param name="delay">Delay after double-click in milliseconds</param>
    public async Task DoubleClickAsync(int delay = 100)
    {
        DoubleClick();
        if (delay > 0)
        {
            await _page.Wait(delay);
        }
    }

    /// <summary>
    /// Hover over the element
    /// </summary>
    public void Hover()
    {
        Simulation.SendInput.Mouse.MoveMouseTo(X, Y);
    }

    /// <summary>
    /// Hover over the element with delay
    /// </summary>
    /// <param name="delay">Delay after hover in milliseconds</param>
    public async Task HoverAsync(int delay = 100)
    {
        Hover();
        if (delay > 0)
        {
            await _page.Wait(delay);
        }
    }

    /// <summary>
    /// Click and drag from this element to target coordinates
    /// </summary>
    /// <param name="targetX">Target X coordinate</param>
    /// <param name="targetY">Target Y coordinate</param>
    /// <param name="duration">Duration of drag in milliseconds</param>
    public async Task DragTo(int targetX, int targetY, int duration = 1000)
    {
        var steps = Math.Max(10, duration / 50); // At least 10 steps, or one step per 50ms
        var deltaX = (targetX - X) / (double)steps;
        var deltaY = (targetY - Y) / (double)steps;
        var stepDelay = duration / steps;

        // Start drag
        Simulation.SendInput.Mouse.MoveMouseTo(X, Y).LeftButtonDown();
        await _page.Wait(50);

        // Perform drag steps
        for (int i = 1; i <= steps; i++)
        {
            var currentX = X + (int)(deltaX * i);
            var currentY = Y + (int)(deltaY * i);
            Simulation.SendInput.Mouse.MoveMouseTo(currentX, currentY);
            await _page.Wait(stepDelay);
        }

        // End drag
        Simulation.SendInput.Mouse.LeftButtonUp();
        await _page.Wait(50);
    }

    /// <summary>
    /// Click and drag from this element to another element
    /// </summary>
    /// <param name="targetElement">Target element</param>
    /// <param name="duration">Duration of drag in milliseconds</param>
    public async Task DragTo(BgiElement targetElement, int duration = 1000)
    {
        await DragTo(targetElement.X, targetElement.Y, duration);
    }

    /// <summary>
    /// Get text content of the element using OCR
    /// </summary>
    /// <returns>Text content</returns>
    public string GetText()
    {
        using var screen = _page.Screenshot();
        var croppedRegion = screen.DeriveCrop(BoundingBox);
        return OcrFactory.Paddle.Ocr(croppedRegion.SrcMat);
    }

    /// <summary>
    /// Get text content of the element using OCR (async version)
    /// </summary>
    /// <returns>Text content</returns>
    public async Task<string> GetTextAsync()
    {
        return await Task.Run(() => GetText());
    }

    /// <summary>
    /// Check if element is still visible in current screen
    /// </summary>
    /// <returns>True if visible, false otherwise</returns>
    public bool IsVisible()
    {
        try
        {
            using var screen = _page.Screenshot();
            // Create a simple template match to check if element is still there
            var template = new Mat(screen.SrcMat, BoundingBox);
            var result = new Mat();
            Cv2.MatchTemplate(screen.SrcMat, template, result, TemplateMatchModes.CCoeffNormed);
            
            Cv2.MinMaxLoc(result, out _, out var maxVal, out _, out _);
            template.Dispose();
            result.Dispose();
            
            return maxVal > 0.8; // Consider element visible if similarity > 80%
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Wait for element to disappear
    /// </summary>
    /// <param name="timeout">Timeout in milliseconds</param>
    /// <returns>True if element disappears, false if timeout</returns>
    public async Task<bool> WaitForHidden(int timeout = 10000)
    {
        var retryCount = timeout / 1000;
        return await NewRetry.WaitForAction(() => !IsVisible(), _cancellationToken, retryCount, 1000);
    }

    /// <summary>
    /// Take a screenshot of just this element
    /// </summary>
    /// <returns>ImageRegion containing element screenshot</returns>
    public ImageRegion Screenshot()
    {
        using var screen = _page.Screenshot();
        return screen.DeriveCrop(BoundingBox);
    }

    /// <summary>
    /// Scroll the mouse wheel while hovering over this element
    /// </summary>
    /// <param name="scrollAmount">Amount to scroll (positive for up, negative for down)</param>
    public void Scroll(int scrollAmount)
    {
        Simulation.SendInput.Mouse.MoveMouseTo(X, Y);
        if (scrollAmount > 0)
        {
            for (int i = 0; i < scrollAmount; i++)
            {
                Simulation.SendInput.Mouse.VerticalScroll(1);
            }
        }
        else
        {
            for (int i = 0; i < -scrollAmount; i++)
            {
                Simulation.SendInput.Mouse.VerticalScroll(-1);
            }
        }
    }

    /// <summary>
    /// Scroll the mouse wheel while hovering over this element with delay
    /// </summary>
    /// <param name="scrollAmount">Amount to scroll (positive for up, negative for down)</param>
    /// <param name="delay">Delay after scroll in milliseconds</param>
    public async Task ScrollAsync(int scrollAmount, int delay = 100)
    {
        Scroll(scrollAmount);
        if (delay > 0)
        {
            await _page.Wait(delay);
        }
    }

    /// <summary>
    /// Click at a specific offset from the element center
    /// </summary>
    /// <param name="offsetX">X offset from center</param>
    /// <param name="offsetY">Y offset from center</param>
    public void ClickAt(int offsetX, int offsetY)
    {
        Simulation.SendInput.Mouse.MoveMouseTo(X + offsetX, Y + offsetY).LeftButtonClick();
    }

    /// <summary>
    /// Click at a specific offset from the element center with delay
    /// </summary>
    /// <param name="offsetX">X offset from center</param>
    /// <param name="offsetY">Y offset from center</param>
    /// <param name="delay">Delay after click in milliseconds</param>
    public async Task ClickAtAsync(int offsetX, int offsetY, int delay = 100)
    {
        ClickAt(offsetX, offsetY);
        if (delay > 0)
        {
            await _page.Wait(delay);
        }
    }

    /// <summary>
    /// Get the center point of the element
    /// </summary>
    /// <returns>Point representing the center coordinates</returns>
    public Point GetCenter()
    {
        return new Point(X, Y);
    }

    /// <summary>
    /// Get a relative point within the element (0.0 to 1.0 coordinates)
    /// </summary>
    /// <param name="relativeX">Relative X position (0.0 = left, 1.0 = right)</param>
    /// <param name="relativeY">Relative Y position (0.0 = top, 1.0 = bottom)</param>
    /// <returns>Absolute point coordinates</returns>
    public Point GetRelativePoint(double relativeX, double relativeY)
    {
        var absoluteX = ResultArea.X + (int)(Width * relativeX);
        var absoluteY = ResultArea.Y + (int)(Height * relativeY);
        return new Point(absoluteX, absoluteY);
    }

    /// <summary>
    /// Click at a relative position within the element
    /// </summary>
    /// <param name="relativeX">Relative X position (0.0 = left, 1.0 = right)</param>
    /// <param name="relativeY">Relative Y position (0.0 = top, 1.0 = bottom)</param>
    public void ClickRelative(double relativeX, double relativeY)
    {
        var point = GetRelativePoint(relativeX, relativeY);
        Simulation.SendInput.Mouse.MoveMouseTo(point.X, point.Y).LeftButtonClick();
    }

    /// <summary>
    /// Click at a relative position within the element with delay
    /// </summary>
    /// <param name="relativeX">Relative X position (0.0 = left, 1.0 = right)</param>
    /// <param name="relativeY">Relative Y position (0.0 = top, 1.0 = bottom)</param>
    /// <param name="delay">Delay after click in milliseconds</param>
    public async Task ClickRelativeAsync(double relativeX, double relativeY, int delay = 100)
    {
        ClickRelative(relativeX, relativeY);
        if (delay > 0)
        {
            await _page.Wait(delay);
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            ResultArea?.Dispose();
            _disposed = true;
        }
    }
}