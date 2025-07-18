using System;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask.Model.Area;
using OpenCvSharp;

namespace BetterGenshinImpact.GameTask.Common.BgiVision;

/// <summary>
/// Extension methods for BgiPage to provide additional utility functions
/// </summary>
public static class BgiPageExtensions
{
    /// <summary>
    /// Wait for any of the specified locators to appear
    /// </summary>
    /// <param name="page">BgiPage instance</param>
    /// <param name="locators">Array of locators to wait for</param>
    /// <param name="timeout">Timeout in milliseconds</param>
    /// <returns>Index of the first locator that appeared, or -1 if timeout</returns>
    public static async Task<int> WaitForAny(this BgiPage page, BgiLocator[] locators, int timeout = 10000)
    {
        var startTime = DateTime.Now;
        
        while ((DateTime.Now - startTime).TotalMilliseconds < timeout)
        {
            for (int i = 0; i < locators.Length; i++)
            {
                if (locators[i].IsVisible())
                {
                    return i;
                }
            }
            await page.Wait(500);
        }

        return -1;
    }

    /// <summary>
    /// Wait for all specified locators to appear
    /// </summary>
    /// <param name="page">BgiPage instance</param>
    /// <param name="locators">Array of locators to wait for</param>
    /// <param name="timeout">Timeout in milliseconds</param>
    /// <returns>True if all locators appeared within timeout</returns>
    public static async Task<bool> WaitForAll(this BgiPage page, BgiLocator[] locators, int timeout = 10000)
    {
        var startTime = DateTime.Now;
        
        while ((DateTime.Now - startTime).TotalMilliseconds < timeout)
        {
            bool allVisible = true;
            foreach (var locator in locators)
            {
                if (!locator.IsVisible())
                {
                    allVisible = false;
                    break;
                }
            }
            
            if (allVisible)
            {
                return true;
            }
            
            await page.Wait(500);
        }

        return false;
    }

    /// <summary>
    /// Click at specific screen coordinates
    /// </summary>
    /// <param name="page">BgiPage instance</param>
    /// <param name="x">X coordinate</param>
    /// <param name="y">Y coordinate</param>
    /// <param name="delay">Delay after click in milliseconds</param>
    public static async Task ClickAt(this BgiPage page, int x, int y, int delay = 100)
    {
        await page.ClickAsync(x, y, delay);
    }

    /// <summary>
    /// Press and hold a key for specified duration
    /// </summary>
    /// <param name="page">BgiPage instance</param>
    /// <param name="key">Key to press and hold</param>
    /// <param name="duration">Duration to hold in milliseconds</param>
    public static async Task HoldKey(this BgiPage page, VirtualKeyCode key, int duration)
    {
        page.KeyDown(key);
        await page.Wait(duration);
        page.KeyUp(key);
    }

    /// <summary>
    /// Press a key multiple times with intervals
    /// </summary>
    /// <param name="page">BgiPage instance</param>
    /// <param name="key">Key to press</param>
    /// <param name="count">Number of times to press</param>
    /// <param name="interval">Interval between presses in milliseconds</param>
    public static async Task PressKeyMultiple(this BgiPage page, VirtualKeyCode key, int count, int interval = 100)
    {
        for (int i = 0; i < count; i++)
        {
            page.PressKey(key);
            if (i < count - 1) // Don't wait after the last press
            {
                await page.Wait(interval);
            }
        }
    }

    /// <summary>
    /// Scroll at specific coordinates
    /// </summary>
    /// <param name="page">BgiPage instance</param>
    /// <param name="x">X coordinate</param>
    /// <param name="y">Y coordinate</param>
    /// <param name="scrollAmount">Amount to scroll (positive for up, negative for down)</param>
    /// <param name="delay">Delay after scroll in milliseconds</param>
    public static async Task ScrollAt(this BgiPage page, int x, int y, int scrollAmount, int delay = 100)
    {
        Simulation.SendInput.Mouse.MoveMouseTo(x, y);
        
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

        if (delay > 0)
        {
            await page.Wait(delay);
        }
    }

    /// <summary>
    /// Find element by color in HSV range
    /// </summary>
    /// <param name="page">BgiPage instance</param>
    /// <param name="lowerHsv">Lower HSV bound</param>
    /// <param name="upperHsv">Upper HSV bound</param>
    /// <param name="region">Optional region to search within</param>
    /// <returns>BgiElement if found, null otherwise</returns>
    public static BgiElement? FindByColor(this BgiPage page, Scalar lowerHsv, Scalar upperHsv, Rect? region = null)
    {
        using var screen = page.Screenshot();
        var searchRegion = region.HasValue ? screen.DeriveCrop(region.Value) : screen;
        
        using var hsv = new Mat();
        Cv2.CvtColor(searchRegion.SrcMat, hsv, ColorConversionCodes.BGR2HSV);
        
        using var mask = new Mat();
        Cv2.InRange(hsv, lowerHsv, upperHsv, mask);
        
        var contours = Cv2.FindContoursAsArray(mask, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
        
        if (contours.Length > 0)
        {
            var largestContour = contours[0];
            var largestArea = Cv2.ContourArea(largestContour);
            
            foreach (var contour in contours)
            {
                var area = Cv2.ContourArea(contour);
                if (area > largestArea)
                {
                    largestArea = area;
                    largestContour = contour;
                }
            }
            
            var boundingRect = Cv2.BoundingRect(largestContour);
            
            // Adjust coordinates if we searched within a region
            if (region.HasValue)
            {
                boundingRect.X += region.Value.X;
                boundingRect.Y += region.Value.Y;
            }
            
            var resultArea = new ResultArea(boundingRect.X, boundingRect.Y, boundingRect.Width, boundingRect.Height);
            return new BgiElement(resultArea, page, CancellationToken.None);
        }
        
        return null;
    }

    /// <summary>
    /// Check if a specific color exists in the screen
    /// </summary>
    /// <param name="page">BgiPage instance</param>
    /// <param name="lowerHsv">Lower HSV bound</param>
    /// <param name="upperHsv">Upper HSV bound</param>
    /// <param name="region">Optional region to search within</param>
    /// <returns>True if color is found, false otherwise</returns>
    public static bool HasColor(this BgiPage page, Scalar lowerHsv, Scalar upperHsv, Rect? region = null)
    {
        using var element = page.FindByColor(lowerHsv, upperHsv, region);
        return element != null;
    }

    /// <summary>
    /// Wait for a specific color to appear
    /// </summary>
    /// <param name="page">BgiPage instance</param>
    /// <param name="lowerHsv">Lower HSV bound</param>
    /// <param name="upperHsv">Upper HSV bound</param>
    /// <param name="region">Optional region to search within</param>
    /// <param name="timeout">Timeout in milliseconds</param>
    /// <returns>True if color appears within timeout</returns>
    public static async Task<bool> WaitForColor(this BgiPage page, Scalar lowerHsv, Scalar upperHsv, Rect? region = null, int timeout = 10000)
    {
        var startTime = DateTime.Now;
        
        while ((DateTime.Now - startTime).TotalMilliseconds < timeout)
        {
            if (page.HasColor(lowerHsv, upperHsv, region))
            {
                return true;
            }
            await page.Wait(500);
        }

        return false;
    }

    /// <summary>
    /// Wait for a specific color to disappear
    /// </summary>
    /// <param name="page">BgiPage instance</param>
    /// <param name="lowerHsv">Lower HSV bound</param>
    /// <param name="upperHsv">Upper HSV bound</param>
    /// <param name="region">Optional region to search within</param>
    /// <param name="timeout">Timeout in milliseconds</param>
    /// <returns>True if color disappears within timeout</returns>
    public static async Task<bool> WaitForColorGone(this BgiPage page, Scalar lowerHsv, Scalar upperHsv, Rect? region = null, int timeout = 10000)
    {
        var startTime = DateTime.Now;
        
        while ((DateTime.Now - startTime).TotalMilliseconds < timeout)
        {
            if (!page.HasColor(lowerHsv, upperHsv, region))
            {
                return true;
            }
            await page.Wait(500);
        }

        return false;
    }

    /// <summary>
    /// Take a screenshot and save it to file
    /// </summary>
    /// <param name="page">BgiPage instance</param>
    /// <param name="fileName">File name to save to</param>
    /// <param name="region">Optional region to capture</param>
    public static void SaveScreenshot(this BgiPage page, string fileName, Rect? region = null)
    {
        using var screen = page.Screenshot();
        var imageToSave = region.HasValue ? screen.DeriveCrop(region.Value) : screen;
        
        Cv2.ImWrite(fileName, imageToSave.SrcMat);
    }

    /// <summary>
    /// Wait for the game to be ready (no loading screens, etc.)
    /// </summary>
    /// <param name="page">BgiPage instance</param>
    /// <param name="timeout">Timeout in milliseconds</param>
    /// <returns>True if game is ready within timeout</returns>
    public static async Task<bool> WaitForGameReady(this BgiPage page, int timeout = 30000)
    {
        // Wait for Paimon menu to be visible (indicates game is loaded)
        var paimonMenu = BgiUI.PaimonMenu(page);
        return await paimonMenu.WaitFor(timeout) != null;
    }

    /// <summary>
    /// Perform a drag operation from source to destination coordinates
    /// </summary>
    /// <param name="page">BgiPage instance</param>
    /// <param name="sourceX">Source X coordinate</param>
    /// <param name="sourceY">Source Y coordinate</param>
    /// <param name="destX">Destination X coordinate</param>
    /// <param name="destY">Destination Y coordinate</param>
    /// <param name="duration">Duration of drag in milliseconds</param>
    public static async Task DragFromTo(this BgiPage page, int sourceX, int sourceY, int destX, int destY, int duration = 1000)
    {
        var steps = Math.Max(10, duration / 50); // At least 10 steps, or one step per 50ms
        var deltaX = (destX - sourceX) / (double)steps;
        var deltaY = (destY - sourceY) / (double)steps;
        var stepDelay = duration / steps;

        // Start drag
        Simulation.SendInput.Mouse.MoveMouseTo(sourceX, sourceY).LeftButtonDown();
        await page.Wait(50);

        // Perform drag steps
        for (int i = 1; i <= steps; i++)
        {
            var currentX = sourceX + (int)(deltaX * i);
            var currentY = sourceY + (int)(deltaY * i);
            Simulation.SendInput.Mouse.MoveMouseTo(currentX, currentY);
            await page.Wait(stepDelay);
        }

        // End drag
        Simulation.SendInput.Mouse.LeftButtonUp();
        await page.Wait(50);
    }

    /// <summary>
    /// Move mouse to specific coordinates
    /// </summary>
    /// <param name="page">BgiPage instance</param>
    /// <param name="x">X coordinate</param>
    /// <param name="y">Y coordinate</param>
    /// <param name="delay">Delay after move in milliseconds</param>
    public static async Task MoveMouse(this BgiPage page, int x, int y, int delay = 0)
    {
        Simulation.SendInput.Mouse.MoveMouseTo(x, y);
        if (delay > 0)
        {
            await page.Wait(delay);
        }
    }

    /// <summary>
    /// Perform a right-click at specific coordinates
    /// </summary>
    /// <param name="page">BgiPage instance</param>
    /// <param name="x">X coordinate</param>
    /// <param name="y">Y coordinate</param>
    /// <param name="delay">Delay after right-click in milliseconds</param>
    public static async Task RightClickAt(this BgiPage page, int x, int y, int delay = 100)
    {
        Simulation.SendInput.Mouse.MoveMouseTo(x, y).RightButtonClick();
        if (delay > 0)
        {
            await page.Wait(delay);
        }
    }

    /// <summary>
    /// Perform a double-click at specific coordinates
    /// </summary>
    /// <param name="page">BgiPage instance</param>
    /// <param name="x">X coordinate</param>
    /// <param name="y">Y coordinate</param>
    /// <param name="delay">Delay after double-click in milliseconds</param>
    public static async Task DoubleClickAt(this BgiPage page, int x, int y, int delay = 100)
    {
        Simulation.SendInput.Mouse.MoveMouseTo(x, y).LeftButtonDoubleClick();
        if (delay > 0)
        {
            await page.Wait(delay);
        }
    }
}