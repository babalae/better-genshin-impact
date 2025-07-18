using System;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.Core.Simulator;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.GameTask.Common.BgiVision.Examples;

/// <summary>
/// Comparison between the old API and the new Playwright-inspired API
/// Shows how the new API simplifies common automation tasks
/// </summary>
public class ApiComparisonExamples
{
    private static readonly ILogger Logger = App.GetLogger<ApiComparisonExamples>();

    /// <summary>
    /// Example: Clicking confirm button with retry
    /// </summary>
    public class ConfirmButtonClickExample
    {
        /// <summary>
        /// Old API approach - manual retry logic
        /// </summary>
        public static async Task<bool> OldApiApproach(CancellationToken ct)
        {
            const int maxRetries = 10;
            const int delayMs = 1000;
            
            for (int i = 0; i < maxRetries; i++)
            {
                try
                {
                    using var screen = TaskControl.CaptureToRectArea();
                    
                    // Try white confirm button
                    using var whiteConfirm = screen.Find(ElementAssets.Instance.BtnWhiteConfirm);
                    if (whiteConfirm.IsExist())
                    {
                        whiteConfirm.Click();
                        return true;
                    }
                    
                    // Try black confirm button
                    using var blackConfirm = screen.Find(ElementAssets.Instance.BtnBlackConfirm);
                    if (blackConfirm.IsExist())
                    {
                        blackConfirm.Click();
                        return true;
                    }
                    
                    // Try online yes button
                    using var onlineYes = screen.Find(ElementAssets.Instance.BtnOnlineYes);
                    if (onlineYes.IsExist())
                    {
                        onlineYes.Click();
                        return true;
                    }
                    
                    // Wait before retry
                    await TaskControl.Delay(delayMs, ct);
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, $"Error in retry {i + 1}");
                }
            }
            
            return false;
        }

        /// <summary>
        /// New API approach - clean and simple
        /// </summary>
        public static async Task<bool> NewApiApproach(CancellationToken ct)
        {
            using var page = BgiUI.NewPage(ct);
            
            // Simple one-liner that handles all the retry logic
            return await BgiUI.WaitForAndClickConfirm(page, timeout: 10000);
        }
    }

    /// <summary>
    /// Example: Finding and clicking text elements
    /// </summary>
    public class TextElementExample
    {
        /// <summary>
        /// Old API approach - manual OCR handling
        /// </summary>
        public static async Task<bool> OldApiApproach(CancellationToken ct)
        {
            const int maxRetries = 10;
            const int delayMs = 1000;
            
            for (int i = 0; i < maxRetries; i++)
            {
                try
                {
                    using var screen = TaskControl.CaptureToRectArea();
                    
                    // Manual OCR scanning
                    var ocrResults = screen.FindMulti(new Core.Recognition.RecognitionObject
                    {
                        RecognitionType = Core.Recognition.RecognitionTypes.Ocr
                    });
                    
                    foreach (var result in ocrResults)
                    {
                        if (result.Text.Contains("开始"))
                        {
                            result.Click();
                            return true;
                        }
                    }
                    
                    await TaskControl.Delay(delayMs, ct);
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, $"Error in retry {i + 1}");
                }
            }
            
            return false;
        }

        /// <summary>
        /// New API approach - intuitive text finding
        /// </summary>
        public static async Task<bool> NewApiApproach(CancellationToken ct)
        {
            using var page = BgiUI.NewPage(ct);
            
            var startButton = page.GetByText("开始");
            return await startButton.WaitAndClick(timeout: 10000);
        }
    }

    /// <summary>
    /// Example: Complex workflow with multiple steps
    /// </summary>
    public class ComplexWorkflowExample
    {
        /// <summary>
        /// Old API approach - verbose and error-prone
        /// </summary>
        public static async Task<bool> OldApiApproach(CancellationToken ct)
        {
            try
            {
                // Step 1: Wait for main UI
                bool mainUiReady = false;
                for (int i = 0; i < 30; i++)
                {
                    using var screen = TaskControl.CaptureToRectArea();
                    using var paimonMenu = screen.Find(ElementAssets.Instance.PaimonMenuRo);
                    if (paimonMenu.IsExist())
                    {
                        mainUiReady = true;
                        break;
                    }
                    await TaskControl.Delay(1000, ct);
                }
                
                if (!mainUiReady) return false;
                
                // Step 2: Open inventory
                Simulation.SendInput.Keyboard.KeyPress(VirtualKeyCode.B);
                await TaskControl.Delay(2000, ct);
                
                // Step 3: Find and click artifacts tab
                bool artifactTabClicked = false;
                for (int i = 0; i < 5; i++)
                {
                    using var screen = TaskControl.CaptureToRectArea();
                    var ocrResults = screen.FindMulti(new Core.Recognition.RecognitionObject
                    {
                        RecognitionType = Core.Recognition.RecognitionTypes.Ocr
                    });
                    
                    foreach (var result in ocrResults)
                    {
                        if (result.Text.Contains("圣遗物"))
                        {
                            result.Click();
                            artifactTabClicked = true;
                            break;
                        }
                    }
                    
                    if (artifactTabClicked) break;
                    await TaskControl.Delay(1000, ct);
                }
                
                if (!artifactTabClicked) return false;
                
                // Step 4: Find and click salvage button
                bool salvageClicked = false;
                for (int i = 0; i < 5; i++)
                {
                    using var screen = TaskControl.CaptureToRectArea();
                    using var salvageButton = screen.Find(ElementAssets.Instance.BtnArtifactSalvage);
                    if (salvageButton.IsExist())
                    {
                        salvageButton.Click();
                        salvageClicked = true;
                        break;
                    }
                    await TaskControl.Delay(1000, ct);
                }
                
                if (!salvageClicked) return false;
                
                // Step 5: Confirm salvage
                bool confirmClicked = false;
                for (int i = 0; i < 5; i++)
                {
                    using var screen = TaskControl.CaptureToRectArea();
                    using var confirmButton = screen.Find(ElementAssets.Instance.BtnArtifactSalvageConfirm);
                    if (confirmButton.IsExist())
                    {
                        confirmButton.Click();
                        confirmClicked = true;
                        break;
                    }
                    await TaskControl.Delay(1000, ct);
                }
                
                // Step 6: Close inventory
                Simulation.SendInput.Keyboard.KeyPress(VirtualKeyCode.ESCAPE);
                await TaskControl.Delay(1000, ct);
                
                return confirmClicked;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error in complex workflow");
                return false;
            }
        }

        /// <summary>
        /// New API approach - clean and readable
        /// </summary>
        public static async Task<bool> NewApiApproach(CancellationToken ct)
        {
            using var page = BgiUI.NewPage(ct);
            
            try
            {
                // Step 1: Wait for game ready
                await page.WaitForGameReady(timeout: 30000);
                
                // Step 2: Open inventory
                page.PressKey(VirtualKeyCode.B);
                await page.Wait(2000);
                
                // Step 3: Click artifacts tab
                var artifactTab = page.GetByText("圣遗物");
                await artifactTab.WaitAndClick(timeout: 5000);
                
                // Step 4: Click salvage button
                var salvageButton = BgiUI.ArtifactSalvageButton(page);
                await salvageButton.WaitAndClick(timeout: 5000);
                
                // Step 5: Confirm salvage
                var confirmButton = BgiUI.ArtifactSalvageConfirmButton(page);
                await confirmButton.WaitAndClick(timeout: 5000);
                
                // Step 6: Close inventory
                page.PressKey(VirtualKeyCode.ESCAPE);
                await page.Wait(1000);
                
                return true;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error in complex workflow");
                return false;
            }
        }
    }

    /// <summary>
    /// Example: Element interaction with custom actions
    /// </summary>
    public class ElementInteractionExample
    {
        /// <summary>
        /// Old API approach - complex state management
        /// </summary>
        public static async Task<bool> OldApiApproach(CancellationToken ct)
        {
            const int maxRetries = 10;
            const int delayMs = 1000;
            
            try
            {
                // Wait for F key to appear
                for (int i = 0; i < maxRetries; i++)
                {
                    using var screen = TaskControl.CaptureToRectArea();
                    using var fKey = screen.Find(Core.Recognition.RecognitionObject.Create(
                        "F键", Core.Recognition.RecognitionTypes.TemplateMatch));
                    
                    if (fKey.IsExist())
                    {
                        // Get text manually
                        var textRegion = screen.DeriveCrop(new OpenCvSharp.Rect(
                            fKey.X + 50, fKey.Y, 200, fKey.Height));
                        var text = Core.Recognition.OCR.OcrFactory.Paddle.Ocr(textRegion.SrcMat);
                        
                        Logger.LogInformation($"Found interaction: {text}");
                        
                        // Press F
                        Simulation.SendInput.Keyboard.KeyPress(VirtualKeyCode.F);
                        await TaskControl.Delay(1000, ct);
                        
                        // Wait for F key to disappear
                        for (int j = 0; j < 10; j++)
                        {
                            using var screen2 = TaskControl.CaptureToRectArea();
                            using var fKey2 = screen2.Find(Core.Recognition.RecognitionObject.Create(
                                "F键", Core.Recognition.RecognitionTypes.TemplateMatch));
                            
                            if (!fKey2.IsExist())
                            {
                                Logger.LogInformation("Interaction completed");
                                return true;
                            }
                            await TaskControl.Delay(1000, ct);
                        }
                        
                        return true;
                    }
                    
                    await TaskControl.Delay(delayMs, ct);
                }
                
                return false;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error in element interaction");
                return false;
            }
        }

        /// <summary>
        /// New API approach - elegant and simple
        /// </summary>
        public static async Task<bool> NewApiApproach(CancellationToken ct)
        {
            using var page = BgiUI.NewPage(ct);
            
            try
            {
                // Wait for F key interaction
                var fKeyPrompt = BgiUI.FKey(page);
                var element = await fKeyPrompt.WaitFor(timeout: 10000);
                
                if (element != null)
                {
                    // Get interaction text
                    var interactionText = await element.GetTextAsync();
                    Logger.LogInformation($"Found interaction: {interactionText}");
                    
                    // Press F
                    page.PressKey(VirtualKeyCode.F);
                    await page.Wait(1000);
                    
                    // Wait for interaction to complete
                    await element.WaitForHidden(timeout: 10000);
                    Logger.LogInformation("Interaction completed");
                    
                    return true;
                }
                
                return false;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error in element interaction");
                return false;
            }
        }
    }

    /// <summary>
    /// Summary of key differences
    /// </summary>
    public static class KeyDifferences
    {
        /// <summary>
        /// Old API characteristics:
        /// - Manual retry loops
        /// - Verbose error handling
        /// - Complex state management
        /// - Repetitive code patterns
        /// - Multiple using statements for resource management
        /// - Manual OCR result processing
        /// - Difficult to read and maintain
        /// </summary>
        public static void OldApiCharacteristics() { }

        /// <summary>
        /// New API characteristics:
        /// - Built-in retry mechanisms
        /// - Fluent API design
        /// - Automatic resource management
        /// - Readable method chaining
        /// - Integrated timeout handling
        /// - Simplified text finding
        /// - Easy to understand and maintain
        /// - Playwright-inspired patterns
        /// </summary>
        public static void NewApiCharacteristics() { }
    }
}