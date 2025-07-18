using System;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.Core.Recognition.OCR;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.Core.Simulator;
using Microsoft.Extensions.Logging;
using OpenCvSharp;

namespace BetterGenshinImpact.GameTask.Common.BgiVision;

/// <summary>
/// Playwright-inspired page class for Better Genshin Impact
/// Provides a simple and elegant API for game automation tasks
/// </summary>
public class BgiPage : IDisposable
{
    private static readonly ILogger Logger = App.GetLogger<BgiPage>();
    private readonly CancellationToken _cancellationToken;
    private bool _disposed = false;

    /// <summary>
    /// Default timeout for operations in milliseconds
    /// </summary>
    public int DefaultTimeout { get; set; } = 10000;

    /// <summary>
    /// Default retry interval in milliseconds
    /// </summary>
    public int DefaultRetryInterval { get; set; } = 1000;

    public BgiPage(CancellationToken cancellationToken = default)
    {
        _cancellationToken = cancellationToken;
    }

    /// <summary>
    /// Capture a screenshot of the current game screen
    /// </summary>
    /// <returns>ImageRegion containing the screenshot</returns>
    public ImageRegion Screenshot()
    {
        return TaskControl.CaptureToRectArea();
    }

    /// <summary>
    /// Wait for specified time
    /// </summary>
    /// <param name="milliseconds">Time to wait in milliseconds</param>
    public async Task Wait(int milliseconds)
    {
        await TaskControl.Delay(milliseconds, _cancellationToken);
    }

    /// <summary>
    /// Create a locator for finding elements by template image
    /// </summary>
    /// <param name="templatePath">Path to template image</param>
    /// <param name="threshold">Recognition threshold (0.0 - 1.0)</param>
    /// <returns>BgiLocator for the element</returns>
    public BgiLocator Locator(string templatePath, double threshold = 0.8)
    {
        var recognitionObject = new RecognitionObject
        {
            Name = templatePath,
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage("", templatePath),
            Threshold = threshold
        }.InitTemplate();

        return new BgiLocator(recognitionObject, this, _cancellationToken);
    }

    /// <summary>
    /// Create a locator for finding elements by recognition object
    /// </summary>
    /// <param name="recognitionObject">Pre-configured recognition object</param>
    /// <returns>BgiLocator for the element</returns>
    public BgiLocator Locator(RecognitionObject recognitionObject)
    {
        return new BgiLocator(recognitionObject, this, _cancellationToken);
    }

    /// <summary>
    /// Create a locator for finding elements by OCR text
    /// </summary>
    /// <param name="text">Text to search for</param>
    /// <param name="region">Optional region to search within</param>
    /// <returns>BgiLocator for the element</returns>
    public BgiLocator GetByText(string text, Rect? region = null)
    {
        var recognitionObject = new RecognitionObject
        {
            Name = $"OCR:{text}",
            RecognitionType = RecognitionTypes.Ocr,
            Text = text,
            RegionOfInterest = region ?? new Rect(0, 0, 0, 0)
        };

        return new BgiLocator(recognitionObject, this, _cancellationToken);
    }

    /// <summary>
    /// Wait for an element to be visible
    /// </summary>
    /// <param name="locator">Element locator</param>
    /// <param name="timeout">Timeout in milliseconds</param>
    /// <returns>True if element becomes visible, false if timeout</returns>
    public async Task<bool> WaitForSelector(BgiLocator locator, int? timeout = null)
    {
        var actualTimeout = timeout ?? DefaultTimeout;
        var retryCount = actualTimeout / DefaultRetryInterval;
        
        return await NewRetry.WaitForAction(() =>
        {
            using var screen = Screenshot();
            using var result = screen.Find(locator.RecognitionObject);
            return result.IsExist();
        }, _cancellationToken, retryCount, DefaultRetryInterval);
    }

    /// <summary>
    /// Wait for an element to be hidden
    /// </summary>
    /// <param name="locator">Element locator</param>
    /// <param name="timeout">Timeout in milliseconds</param>
    /// <returns>True if element becomes hidden, false if timeout</returns>
    public async Task<bool> WaitForSelectorHidden(BgiLocator locator, int? timeout = null)
    {
        var actualTimeout = timeout ?? DefaultTimeout;
        var retryCount = actualTimeout / DefaultRetryInterval;
        
        return await NewRetry.WaitForAction(() =>
        {
            using var screen = Screenshot();
            using var result = screen.Find(locator.RecognitionObject);
            return !result.IsExist();
        }, _cancellationToken, retryCount, DefaultRetryInterval);
    }

    /// <summary>
    /// Perform a keyboard key press
    /// </summary>
    /// <param name="key">Key to press</param>
    public void PressKey(VirtualKeyCode key)
    {
        Simulation.SendInput.Keyboard.KeyPress(key);
    }

    /// <summary>
    /// Perform a keyboard key down
    /// </summary>
    /// <param name="key">Key to press down</param>
    public void KeyDown(VirtualKeyCode key)
    {
        Simulation.SendInput.Keyboard.KeyDown(key);
    }

    /// <summary>
    /// Perform a keyboard key up
    /// </summary>
    /// <param name="key">Key to release</param>
    public void KeyUp(VirtualKeyCode key)
    {
        Simulation.SendInput.Keyboard.KeyUp(key);
    }

    /// <summary>
    /// Type text using keyboard simulation
    /// </summary>
    /// <param name="text">Text to type</param>
    public void Type(string text)
    {
        Simulation.SendInput.Keyboard.TextEntry(text);
    }

    /// <summary>
    /// Perform a mouse click at specific coordinates
    /// </summary>
    /// <param name="x">X coordinate</param>
    /// <param name="y">Y coordinate</param>
    public void Click(int x, int y)
    {
        Simulation.SendInput.Mouse.MoveMouseTo(x, y).LeftButtonClick();
    }

    /// <summary>
    /// Perform a mouse click at specific coordinates with delay
    /// </summary>
    /// <param name="x">X coordinate</param>
    /// <param name="y">Y coordinate</param>
    /// <param name="delay">Delay after click in milliseconds</param>
    public async Task ClickAsync(int x, int y, int delay = 100)
    {
        Click(x, y);
        if (delay > 0)
        {
            await Wait(delay);
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
        }
    }
}