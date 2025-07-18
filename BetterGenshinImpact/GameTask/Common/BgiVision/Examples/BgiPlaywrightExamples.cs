using System;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using Microsoft.Extensions.Logging;
using OpenCvSharp;

namespace BetterGenshinImpact.GameTask.Common.BgiVision.Examples;

/// <summary>
/// Example usage of the Playwright-inspired BGI API
/// Demonstrates common patterns and best practices
/// </summary>
public class BgiPlaywrightExamples
{
    private static readonly ILogger Logger = App.GetLogger<BgiPlaywrightExamples>();

    /// <summary>
    /// Example 1: Basic button clicking with automatic retry
    /// </summary>
    public static async Task<bool> ClickConfirmButtonExample(CancellationToken ct)
    {
        using var page = BgiUI.NewPage(ct);
        
        // Wait for and click any confirm button (black, white, or online)
        var confirmButton = BgiUI.WhiteConfirmButton(page);
        
        // Option 1: Simple wait and click
        if (await confirmButton.WaitAndClick(timeout: 5000))
        {
            Logger.LogInformation("Confirm button clicked successfully");
            return true;
        }
        
        // Option 2: Try multiple button types
        return await BgiUI.WaitForAndClickConfirm(page, timeout: 10000);
    }

    /// <summary>
    /// Example 2: OCR-based text finding and interaction
    /// </summary>
    public static async Task<bool> FindAndClickTextExample(CancellationToken ct)
    {
        using var page = BgiUI.NewPage(ct);
        
        // Find button by text content
        var startButton = page.GetByText("开始");
        
        // Wait for the button to appear and click it
        if (await startButton.WaitAndClick(timeout: 10000))
        {
            Logger.LogInformation("Start button found and clicked");
            return true;
        }
        
        Logger.LogWarning("Start button not found");
        return false;
    }

    /// <summary>
    /// Example 3: Complex interaction flow with multiple steps
    /// </summary>
    public static async Task<bool> ComplexInteractionExample(CancellationToken ct)
    {
        using var page = BgiUI.NewPage(ct);
        
        try
        {
            // Step 1: Wait for Paimon menu to be visible (game ready)
            var paimonMenu = BgiUI.PaimonMenu(page);
            await paimonMenu.WaitFor(timeout: 30000);
            Logger.LogInformation("Game is ready - Paimon menu visible");
            
            // Step 2: Open ESC menu
            page.PressKey(VirtualKeyCode.ESCAPE);
            await page.Wait(1000);
            
            // Step 3: Look for specific menu items
            var settingsButton = page.GetByText("设置");
            if (await settingsButton.WaitAndClick(timeout: 5000))
            {
                Logger.LogInformation("Settings menu opened");
                
                // Step 4: Wait for settings to load
                await page.Wait(2000);
                
                // Step 5: Close settings with ESC
                page.PressKey(VirtualKeyCode.ESCAPE);
                await page.Wait(1000);
                
                return true;
            }
            
            return false;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error in complex interaction example");
            return false;
        }
    }

    /// <summary>
    /// Example 4: Inventory management with item counting
    /// </summary>
    public static async Task<bool> InventoryManagementExample(CancellationToken ct)
    {
        using var page = BgiUI.NewPage(ct);
        
        try
        {
            // Open inventory
            page.PressKey(VirtualKeyCode.B);
            await page.Wait(2000);
            
            // Wait for inventory to load
            var artifactTab = page.GetByText("圣遗物");
            await artifactTab.WaitAndClick(timeout: 5000);
            
            // Look for salvage button
            var salvageButton = BgiUI.ArtifactSalvageButton(page);
            
            if (await salvageButton.WaitAndClick(timeout: 5000))
            {
                Logger.LogInformation("Artifact salvage opened");
                
                // Wait for salvage confirm button
                var confirmSalvage = BgiUI.ArtifactSalvageConfirmButton(page);
                
                if (await confirmSalvage.WaitAndClick(timeout: 5000))
                {
                    Logger.LogInformation("Artifact salvage confirmed");
                    
                    // Wait for completion
                    await page.Wait(3000);
                    
                    // Close inventory
                    page.PressKey(VirtualKeyCode.ESCAPE);
                    await page.Wait(1000);
                    
                    return true;
                }
            }
            
            return false;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error in inventory management example");
            return false;
        }
    }

    /// <summary>
    /// Example 5: Element interaction with custom actions
    /// </summary>
    public static async Task<bool> ElementInteractionExample(CancellationToken ct)
    {
        using var page = BgiUI.NewPage(ct);
        
        try
        {
            // Find F key interaction prompt
            var fKeyPrompt = BgiUI.FKey(page);
            
            // Wait for interaction to be available
            var element = await fKeyPrompt.WaitFor(timeout: 10000);
            
            if (element != null)
            {
                // Get the text of the interaction
                var interactionText = await element.GetTextAsync();
                Logger.LogInformation($"Interaction available: {interactionText}");
                
                // Press F to interact
                page.PressKey(VirtualKeyCode.F);
                await page.Wait(1000);
                
                // Wait for interaction to complete (F key disappears)
                await element.WaitForHidden(timeout: 10000);
                Logger.LogInformation("Interaction completed");
                
                return true;
            }
            
            Logger.LogWarning("No interaction available");
            return false;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error in element interaction example");
            return false;
        }
    }

    /// <summary>
    /// Example 6: Advanced screenshot and region-based operations
    /// </summary>
    public static async Task<bool> ScreenshotAndRegionExample(CancellationToken ct)
    {
        using var page = BgiUI.NewPage(ct);
        
        try
        {
            // Take a full screenshot
            page.SaveScreenshot("full_screen.png");
            
            // Define a custom region for minimap area
            var minimapRegion = new Rect(0, 0, 300, 300);
            
            // Take a screenshot of just the minimap
            page.SaveScreenshot("minimap.png", minimapRegion);
            
            // Look for blue track point in the minimap area
            var trackPoint = BgiUI.BlueTrackPoint(page).WithRegion(minimapRegion);
            
            if (await trackPoint.WaitAndClick(timeout: 5000))
            {
                Logger.LogInformation("Track point clicked on minimap");
                return true;
            }
            
            return false;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error in screenshot and region example");
            return false;
        }
    }

    /// <summary>
    /// Example 7: Color-based detection
    /// </summary>
    public static async Task<bool> ColorDetectionExample(CancellationToken ct)
    {
        using var page = BgiUI.NewPage(ct);
        
        try
        {
            // Define color range for health bar (red color in HSV)
            var lowerRed = new Scalar(0, 120, 70);
            var upperRed = new Scalar(10, 255, 255);
            
            // Check if health is low (red health bar visible)
            if (page.HasColor(lowerRed, upperRed))
            {
                Logger.LogWarning("Health is low - red health bar detected");
                
                // Use healing item
                page.PressKey(VirtualKeyCode.Z);
                await page.Wait(1000);
                
                // Wait for health to recover (red color disappears)
                await page.WaitForColorGone(lowerRed, upperRed, timeout: 10000);
                Logger.LogInformation("Health recovered");
                
                return true;
            }
            
            Logger.LogInformation("Health is fine");
            return true;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error in color detection example");
            return false;
        }
    }

    /// <summary>
    /// Example 8: Drag and drop operations
    /// </summary>
    public static async Task<bool> DragDropExample(CancellationToken ct)
    {
        using var page = BgiUI.NewPage(ct);
        
        try
        {
            // Open inventory
            page.PressKey(VirtualKeyCode.B);
            await page.Wait(2000);
            
            // Find an item to drag (example: find an artifact)
            var sourceItem = page.GetByText("圣遗物");
            var sourceElement = await sourceItem.WaitFor(timeout: 5000);
            
            if (sourceElement != null)
            {
                // Define target coordinates (e.g., enhancement slot)
                var targetX = 800;
                var targetY = 400;
                
                // Perform drag operation
                await sourceElement.DragTo(targetX, targetY);
                Logger.LogInformation("Item dragged to enhancement slot");
                
                // Close inventory
                page.PressKey(VirtualKeyCode.ESCAPE);
                await page.Wait(1000);
                
                return true;
            }
            
            return false;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error in drag drop example");
            return false;
        }
    }

    /// <summary>
    /// Example 9: Multiple element waiting and selection
    /// </summary>
    public static async Task<bool> MultipleElementWaitingExample(CancellationToken ct)
    {
        using var page = BgiUI.NewPage(ct);
        
        try
        {
            // Define multiple buttons we might encounter
            var buttons = new[]
            {
                BgiUI.WhiteConfirmButton(page),
                BgiUI.BlackConfirmButton(page),
                BgiUI.OnlineYesButton(page),
                BgiUI.CollectButton(page)
            };
            
            // Wait for any of these buttons to appear
            var buttonIndex = await page.WaitForAny(buttons, timeout: 10000);
            
            if (buttonIndex >= 0)
            {
                Logger.LogInformation($"Button {buttonIndex} appeared first");
                
                // Click the button that appeared
                await buttons[buttonIndex].WaitAndClick(timeout: 1000);
                
                return true;
            }
            
            Logger.LogWarning("No buttons appeared within timeout");
            return false;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error in multiple element waiting example");
            return false;
        }
    }

    /// <summary>
    /// Example 10: Complete task automation workflow
    /// </summary>
    public static async Task<bool> CompleteTaskWorkflowExample(CancellationToken ct)
    {
        using var page = BgiUI.NewPage(ct);
        
        try
        {
            // Step 1: Wait for game to be ready
            await page.WaitForGameReady(timeout: 30000);
            Logger.LogInformation("Game is ready");
            
            // Step 2: Open quest menu
            page.PressKey(VirtualKeyCode.J);
            await page.Wait(2000);
            
            // Step 3: Look for quest start button
            var startQuestButton = page.GetByText("开始任务");
            
            if (await startQuestButton.WaitAndClick(timeout: 5000))
            {
                Logger.LogInformation("Quest started");
                
                // Step 4: Wait for quest dialog and confirm
                await BgiUI.WaitForAndClickConfirm(page, timeout: 10000);
                
                // Step 5: Wait for quest completion
                var completeButton = page.GetByText("完成");
                
                if (await completeButton.WaitAndClick(timeout: 60000))
                {
                    Logger.LogInformation("Quest completed");
                    
                    // Step 6: Collect rewards
                    var collectButton = BgiUI.CollectButton(page);
                    await collectButton.WaitAndClick(timeout: 5000);
                    
                    // Step 7: Close quest menu
                    page.PressKey(VirtualKeyCode.ESCAPE);
                    await page.Wait(1000);
                    
                    return true;
                }
            }
            
            return false;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error in complete task workflow example");
            return false;
        }
    }
}