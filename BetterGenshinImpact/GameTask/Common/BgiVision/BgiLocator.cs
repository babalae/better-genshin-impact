using System;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.Core.Recognition.OCR;
using BetterGenshinImpact.GameTask.Model.Area;
using Microsoft.Extensions.Logging;
using OpenCvSharp;

namespace BetterGenshinImpact.GameTask.Common.BgiVision;

/// <summary>
/// Playwright-inspired locator class for finding and interacting with elements
/// </summary>
public class BgiLocator
{
    private static readonly ILogger Logger = App.GetLogger<BgiLocator>();
    private readonly BgiPage _page;
    private readonly CancellationToken _cancellationToken;

    public RecognitionObject RecognitionObject { get; }

    /// <summary>
    /// Default timeout for operations in milliseconds
    /// </summary>
    public int DefaultTimeout { get; set; } = 10000;

    /// <summary>
    /// Default retry interval in milliseconds
    /// </summary>
    public int DefaultRetryInterval { get; set; } = 1000;

    internal BgiLocator(RecognitionObject recognitionObject, BgiPage page, CancellationToken cancellationToken)
    {
        RecognitionObject = recognitionObject;
        _page = page;
        _cancellationToken = cancellationToken;
    }

    /// <summary>
    /// Find the element in the current screen
    /// </summary>
    /// <returns>BgiElement if found, null if not found</returns>
    public BgiElement? Find()
    {
        using var screen = _page.Screenshot();
        using var result = screen.Find(RecognitionObject);
        
        if (result.IsExist())
        {
            return new BgiElement(result, _page, _cancellationToken);
        }
        
        return null;
    }

    /// <summary>
    /// Find all matching elements in the current screen
    /// </summary>
    /// <returns>Array of BgiElement objects</returns>
    public BgiElement[] FindAll()
    {
        using var screen = _page.Screenshot();
        var results = screen.FindMulti(RecognitionObject);
        
        var elements = new BgiElement[results.Count];
        for (int i = 0; i < results.Count; i++)
        {
            elements[i] = new BgiElement(results[i], _page, _cancellationToken);
        }
        
        return elements;
    }

    /// <summary>
    /// Wait for element to be visible and return it
    /// </summary>
    /// <param name="timeout">Timeout in milliseconds</param>
    /// <returns>BgiElement if found within timeout</returns>
    /// <exception cref="TimeoutException">Thrown when element is not found within timeout</exception>
    public async Task<BgiElement> WaitFor(int? timeout = null)
    {
        var actualTimeout = timeout ?? DefaultTimeout;
        var retryCount = actualTimeout / DefaultRetryInterval;
        
        for (int i = 0; i < retryCount; i++)
        {
            var element = Find();
            if (element != null)
            {
                return element;
            }
            
            if (i < retryCount - 1) // Don't wait on the last iteration
            {
                await TaskControl.Delay(DefaultRetryInterval, _cancellationToken);
            }
        }
        
        throw new TimeoutException($"Element '{RecognitionObject.Name}' not found within {actualTimeout}ms");
    }

    /// <summary>
    /// Wait for element to be visible and click it
    /// </summary>
    /// <param name="timeout">Timeout in milliseconds</param>
    /// <param name="delay">Delay after click in milliseconds</param>
    /// <returns>True if clicked successfully</returns>
    public async Task<bool> WaitAndClick(int? timeout = null, int delay = 100)
    {
        try
        {
            var element = await WaitFor(timeout);
            await element.ClickAsync(delay);
            return true;
        }
        catch (TimeoutException)
        {
            Logger.LogWarning($"Failed to find and click element '{RecognitionObject.Name}' within timeout");
            return false;
        }
    }

    /// <summary>
    /// Wait for element to be visible and get its text content
    /// </summary>
    /// <param name="timeout">Timeout in milliseconds</param>
    /// <returns>Text content of the element</returns>
    public async Task<string> WaitAndGetText(int? timeout = null)
    {
        var element = await WaitFor(timeout);
        return await element.GetTextAsync();
    }

    /// <summary>
    /// Check if element is currently visible
    /// </summary>
    /// <returns>True if visible, false otherwise</returns>
    public bool IsVisible()
    {
        return Find() != null;
    }

    /// <summary>
    /// Wait for element to disappear
    /// </summary>
    /// <param name="timeout">Timeout in milliseconds</param>
    /// <returns>True if element disappears within timeout, false otherwise</returns>
    public async Task<bool> WaitForHidden(int? timeout = null)
    {
        var actualTimeout = timeout ?? DefaultTimeout;
        var retryCount = actualTimeout / DefaultRetryInterval;
        
        return await NewRetry.WaitForAction(() => !IsVisible(), _cancellationToken, retryCount, DefaultRetryInterval);
    }

    /// <summary>
    /// Click the element if it's visible
    /// </summary>
    /// <param name="delay">Delay after click in milliseconds</param>
    /// <returns>True if clicked, false if element not found</returns>
    public async Task<bool> ClickIfVisible(int delay = 100)
    {
        var element = Find();
        if (element != null)
        {
            await element.ClickAsync(delay);
            return true;
        }
        return false;
    }

    /// <summary>
    /// Click the element with automatic retry until it disappears or timeout
    /// </summary>
    /// <param name="timeout">Timeout in milliseconds</param>
    /// <param name="clickInterval">Interval between clicks in milliseconds</param>
    /// <returns>True if element disappeared, false if timeout</returns>
    public async Task<bool> ClickUntilGone(int? timeout = null, int clickInterval = 1000)
    {
        var actualTimeout = timeout ?? DefaultTimeout;
        var retryCount = actualTimeout / clickInterval;
        
        return await NewRetry.WaitForElementDisappear(
            RecognitionObject,
            (screen) =>
            {
                using var result = screen.Find(RecognitionObject);
                if (result.IsExist())
                {
                    result.Click();
                }
            },
            _cancellationToken,
            retryCount,
            clickInterval
        );
    }

    /// <summary>
    /// Repeatedly click until element appears
    /// </summary>
    /// <param name="retryAction">Action to perform between checks (e.g., click something else)</param>
    /// <param name="timeout">Timeout in milliseconds</param>
    /// <param name="retryInterval">Interval between retries in milliseconds</param>
    /// <returns>True if element appeared, false if timeout</returns>
    public async Task<bool> ClickUntilAppear(Action? retryAction = null, int? timeout = null, int? retryInterval = null)
    {
        var actualTimeout = timeout ?? DefaultTimeout;
        var actualRetryInterval = retryInterval ?? DefaultRetryInterval;
        var retryCount = actualTimeout / actualRetryInterval;
        
        return await NewRetry.WaitForElementAppear(
            RecognitionObject,
            retryAction,
            _cancellationToken,
            retryCount,
            actualRetryInterval
        );
    }

    /// <summary>
    /// Create a new locator with additional constraints
    /// </summary>
    /// <param name="threshold">Recognition threshold</param>
    /// <returns>New BgiLocator with updated threshold</returns>
    public BgiLocator WithThreshold(double threshold)
    {
        var newRecognitionObject = new RecognitionObject
        {
            Name = RecognitionObject.Name,
            RecognitionType = RecognitionObject.RecognitionType,
            TemplateImageMat = RecognitionObject.TemplateImageMat,
            Threshold = threshold,
            RegionOfInterest = RecognitionObject.RegionOfInterest,
            Text = RecognitionObject.Text
        };

        if (RecognitionObject.RecognitionType == RecognitionTypes.TemplateMatch)
        {
            newRecognitionObject.InitTemplate();
        }

        return new BgiLocator(newRecognitionObject, _page, _cancellationToken);
    }

    /// <summary>
    /// Create a new locator with region of interest constraint
    /// </summary>
    /// <param name="region">Region to search within</param>
    /// <returns>New BgiLocator with updated region</returns>
    public BgiLocator WithRegion(Rect region)
    {
        var newRecognitionObject = new RecognitionObject
        {
            Name = RecognitionObject.Name,
            RecognitionType = RecognitionObject.RecognitionType,
            TemplateImageMat = RecognitionObject.TemplateImageMat,
            Threshold = RecognitionObject.Threshold,
            RegionOfInterest = region,
            Text = RecognitionObject.Text
        };

        if (RecognitionObject.RecognitionType == RecognitionTypes.TemplateMatch)
        {
            newRecognitionObject.InitTemplate();
        }

        return new BgiLocator(newRecognitionObject, _page, _cancellationToken);
    }
}