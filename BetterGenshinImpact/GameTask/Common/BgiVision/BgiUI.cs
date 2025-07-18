using System;
using System.Threading;
using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using BetterGenshinImpact.GameTask.AutoPick.Assets;
using OpenCvSharp;

namespace BetterGenshinImpact.GameTask.Common.BgiVision;

/// <summary>
/// Preset locators for common UI elements in Genshin Impact
/// Provides easy access to frequently used elements with Playwright-style API
/// </summary>
public static class BgiUI
{
    /// <summary>
    /// Create a new BgiPage instance
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for operations</param>
    /// <returns>New BgiPage instance</returns>
    public static BgiPage NewPage(CancellationToken cancellationToken = default)
    {
        return new BgiPage(cancellationToken);
    }

    /// <summary>
    /// Get locator for white confirm button
    /// </summary>
    /// <param name="page">BgiPage instance</param>
    /// <returns>Locator for white confirm button</returns>
    public static BgiLocator WhiteConfirmButton(BgiPage page)
    {
        return page.Locator(ElementAssets.Instance.BtnWhiteConfirm);
    }

    /// <summary>
    /// Get locator for white cancel button
    /// </summary>
    /// <param name="page">BgiPage instance</param>
    /// <returns>Locator for white cancel button</returns>
    public static BgiLocator WhiteCancelButton(BgiPage page)
    {
        return page.Locator(ElementAssets.Instance.BtnWhiteCancel);
    }

    /// <summary>
    /// Get locator for black confirm button
    /// </summary>
    /// <param name="page">BgiPage instance</param>
    /// <returns>Locator for black confirm button</returns>
    public static BgiLocator BlackConfirmButton(BgiPage page)
    {
        return page.Locator(ElementAssets.Instance.BtnBlackConfirm);
    }

    /// <summary>
    /// Get locator for black cancel button
    /// </summary>
    /// <param name="page">BgiPage instance</param>
    /// <returns>Locator for black cancel button</returns>
    public static BgiLocator BlackCancelButton(BgiPage page)
    {
        return page.Locator(ElementAssets.Instance.BtnBlackCancel);
    }

    /// <summary>
    /// Get locator for online yes button
    /// </summary>
    /// <param name="page">BgiPage instance</param>
    /// <returns>Locator for online yes button</returns>
    public static BgiLocator OnlineYesButton(BgiPage page)
    {
        return page.Locator(ElementAssets.Instance.BtnOnlineYes);
    }

    /// <summary>
    /// Get locator for online no button
    /// </summary>
    /// <param name="page">BgiPage instance</param>
    /// <returns>Locator for online no button</returns>
    public static BgiLocator OnlineNoButton(BgiPage page)
    {
        return page.Locator(ElementAssets.Instance.BtnOnlineNo);
    }

    /// <summary>
    /// Get locator for Paimon menu
    /// </summary>
    /// <param name="page">BgiPage instance</param>
    /// <returns>Locator for Paimon menu</returns>
    public static BgiLocator PaimonMenu(BgiPage page)
    {
        return page.Locator(ElementAssets.Instance.PaimonMenuRo);
    }

    /// <summary>
    /// Get locator for blue track point
    /// </summary>
    /// <param name="page">BgiPage instance</param>
    /// <returns>Locator for blue track point</returns>
    public static BgiLocator BlueTrackPoint(BgiPage page)
    {
        return page.Locator(ElementAssets.Instance.BlueTrackPoint);
    }

    /// <summary>
    /// Get locator for space key prompt
    /// </summary>
    /// <param name="page">BgiPage instance</param>
    /// <returns>Locator for space key prompt</returns>
    public static BgiLocator SpaceKey(BgiPage page)
    {
        return page.Locator(ElementAssets.Instance.SpaceKey);
    }

    /// <summary>
    /// Get locator for X key prompt
    /// </summary>
    /// <param name="page">BgiPage instance</param>
    /// <returns>Locator for X key prompt</returns>
    public static BgiLocator XKey(BgiPage page)
    {
        return page.Locator(ElementAssets.Instance.XKey);
    }

    /// <summary>
    /// Get locator for F key interaction prompt
    /// </summary>
    /// <param name="page">BgiPage instance</param>
    /// <returns>Locator for F key interaction prompt</returns>
    public static BgiLocator FKey(BgiPage page)
    {
        return page.Locator(AutoPickAssets.Instance.PickRo);
    }

    /// <summary>
    /// Get locator for increase button
    /// </summary>
    /// <param name="page">BgiPage instance</param>
    /// <returns>Locator for increase button</returns>
    public static BgiLocator IncreaseButton(BgiPage page)
    {
        return page.Locator(ElementAssets.Instance.Keyincrease);
    }

    /// <summary>
    /// Get locator for decrease button
    /// </summary>
    /// <param name="page">BgiPage instance</param>
    /// <returns>Locator for decrease button</returns>
    public static BgiLocator DecreaseButton(BgiPage page)
    {
        return page.Locator(ElementAssets.Instance.Keyreduce);
    }

    /// <summary>
    /// Get locator for page close button (white)
    /// </summary>
    /// <param name="page">BgiPage instance</param>
    /// <returns>Locator for page close button</returns>
    public static BgiLocator PageCloseButton(BgiPage page)
    {
        return page.Locator(ElementAssets.Instance.PageCloseWhiteRo);
    }

    /// <summary>
    /// Get locator for collect button
    /// </summary>
    /// <param name="page">BgiPage instance</param>
    /// <returns>Locator for collect button</returns>
    public static BgiLocator CollectButton(BgiPage page)
    {
        return page.Locator(ElementAssets.Instance.CollectRo);
    }

    /// <summary>
    /// Get locator for primogem icon
    /// </summary>
    /// <param name="page">BgiPage instance</param>
    /// <returns>Locator for primogem icon</returns>
    public static BgiLocator PrimogemIcon(BgiPage page)
    {
        return page.Locator(ElementAssets.Instance.PrimogemRo);
    }

    /// <summary>
    /// Get locator for artifact salvage button
    /// </summary>
    /// <param name="page">BgiPage instance</param>
    /// <returns>Locator for artifact salvage button</returns>
    public static BgiLocator ArtifactSalvageButton(BgiPage page)
    {
        return page.Locator(ElementAssets.Instance.BtnArtifactSalvage);
    }

    /// <summary>
    /// Get locator for artifact salvage confirm button
    /// </summary>
    /// <param name="page">BgiPage instance</param>
    /// <returns>Locator for artifact salvage confirm button</returns>
    public static BgiLocator ArtifactSalvageConfirmButton(BgiPage page)
    {
        return page.Locator(ElementAssets.Instance.BtnArtifactSalvageConfirm);
    }

    /// <summary>
    /// Get locator for encounter points rewards button
    /// </summary>
    /// <param name="page">BgiPage instance</param>
    /// <returns>Locator for encounter points rewards button</returns>
    public static BgiLocator EncounterPointsRewardsButton(BgiPage page)
    {
        return page.Locator(ElementAssets.Instance.BtnClaimEncounterPointsRewards);
    }

    /// <summary>
    /// Get locator for mail reward button
    /// </summary>
    /// <param name="page">BgiPage instance</param>
    /// <returns>Locator for mail reward button</returns>
    public static BgiLocator MailRewardButton(BgiPage page)
    {
        return page.Locator(ElementAssets.Instance.EscMailReward);
    }

    /// <summary>
    /// Get locator for Sereniteapot home icon
    /// </summary>
    /// <param name="page">BgiPage instance</param>
    /// <returns>Locator for Sereniteapot home icon</returns>
    public static BgiLocator SereniteapotHome(BgiPage page)
    {
        return page.Locator(ElementAssets.Instance.SereniteaPotHomeRo);
    }

    /// <summary>
    /// Get locator for A Yuan icon
    /// </summary>
    /// <param name="page">BgiPage instance</param>
    /// <returns>Locator for A Yuan icon</returns>
    public static BgiLocator AYuanIcon(BgiPage page)
    {
        return page.Locator(ElementAssets.Instance.AYuanIconRo);
    }

    /// <summary>
    /// Try to click any available confirm button (black, white, or online)
    /// </summary>
    /// <param name="page">BgiPage instance</param>
    /// <param name="timeout">Timeout in milliseconds</param>
    /// <returns>True if any confirm button was clicked</returns>
    public static async Task<bool> ClickAnyConfirmButton(BgiPage page, int timeout = 5000)
    {
        var buttons = new[]
        {
            BlackConfirmButton(page),
            WhiteConfirmButton(page),
            OnlineYesButton(page)
        };

        foreach (var button in buttons)
        {
            if (await button.WaitAndClick(timeout))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Try to click any available cancel button (black, white, or online)
    /// </summary>
    /// <param name="page">BgiPage instance</param>
    /// <param name="timeout">Timeout in milliseconds</param>
    /// <returns>True if any cancel button was clicked</returns>
    public static async Task<bool> ClickAnyCancelButton(BgiPage page, int timeout = 5000)
    {
        var buttons = new[]
        {
            BlackCancelButton(page),
            WhiteCancelButton(page),
            OnlineNoButton(page)
        };

        foreach (var button in buttons)
        {
            if (await button.WaitAndClick(timeout))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Wait for any confirm button to appear and click it
    /// </summary>
    /// <param name="page">BgiPage instance</param>
    /// <param name="timeout">Timeout in milliseconds</param>
    /// <returns>True if any confirm button was found and clicked</returns>
    public static async Task<bool> WaitForAndClickConfirm(BgiPage page, int timeout = 10000)
    {
        var startTime = DateTime.Now;
        
        while ((DateTime.Now - startTime).TotalMilliseconds < timeout)
        {
            if (await ClickAnyConfirmButton(page, 1000))
            {
                return true;
            }
            await page.Wait(500);
        }

        return false;
    }

    /// <summary>
    /// Wait for any cancel button to appear and click it
    /// </summary>
    /// <param name="page">BgiPage instance</param>
    /// <param name="timeout">Timeout in milliseconds</param>
    /// <returns>True if any cancel button was found and clicked</returns>
    public static async Task<bool> WaitForAndClickCancel(BgiPage page, int timeout = 10000)
    {
        var startTime = DateTime.Now;
        
        while ((DateTime.Now - startTime).TotalMilliseconds < timeout)
        {
            if (await ClickAnyCancelButton(page, 1000))
            {
                return true;
            }
            await page.Wait(500);
        }

        return false;
    }
}