using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask;
using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace BetterGenshinImpact.Core.Simulator.Extensions;

public static class SimulateKeyHelper
{

    private static KeyBindingsConfig KeyConfig => TaskContext.Instance().Config.KeyBindingsConfig;

    public static KeyId ToActionKey(this GIActions action)
    {
        return GetActionKey(action);
    }

    /// <summary>
    /// 通过ActionId取得实际的键盘绑定
    /// </summary>
    /// <param name="action"></param>
    /// <returns></returns>
    public static KeyId GetActionKey(GIActions action)
    {
        return action switch
        {
            GIActions.MoveForward => KeyConfig.MoveForward,
            GIActions.MoveBackward => KeyConfig.MoveBackward,
            GIActions.MoveLeft => KeyConfig.MoveLeft,
            GIActions.MoveRight => KeyConfig.MoveRight,
            GIActions.SwitchToWalkOrRun => KeyConfig.SwitchToWalkOrRun,
            GIActions.NormalAttack => KeyConfig.NormalAttack,
            GIActions.ElementalSkill => KeyConfig.ElementalSkill,
            GIActions.ElementalBurst => KeyConfig.ElementalBurst,
            GIActions.SprintKeyboard => KeyConfig.SprintKeyboard,
            GIActions.SprintMouse => KeyConfig.SprintMouse,
            GIActions.SwitchAimingMode => KeyConfig.SwitchAimingMode,
            GIActions.Jump => KeyConfig.Jump,
            GIActions.Drop => KeyConfig.Drop,
            GIActions.PickUpOrInteract => KeyConfig.PickUpOrInteract,
            GIActions.QuickUseGadget => KeyConfig.QuickUseGadget,
            GIActions.InteractionInSomeMode => KeyConfig.InteractionInSomeMode,
            GIActions.QuestNavigation => KeyConfig.QuestNavigation,
            GIActions.AbandonChallenge => KeyConfig.AbandonChallenge,
            GIActions.SwitchMember1 => KeyConfig.SwitchMember1,
            GIActions.SwitchMember2 => KeyConfig.SwitchMember2,
            GIActions.SwitchMember3 => KeyConfig.SwitchMember3,
            GIActions.SwitchMember4 => KeyConfig.SwitchMember4,
            GIActions.SwitchMember5 => KeyConfig.SwitchMember5,
            GIActions.ShortcutWheel => KeyConfig.ShortcutWheel,
            GIActions.OpenInventory => KeyConfig.OpenInventory,
            GIActions.OpenCharacterScreen => KeyConfig.OpenCharacterScreen,
            GIActions.OpenMap => KeyConfig.OpenMap,
            GIActions.OpenPaimonMenu => KeyConfig.OpenPaimonMenu,
            GIActions.OpenAdventurerHandbook => KeyConfig.OpenAdventurerHandbook,
            GIActions.OpenCoOpScreen => KeyConfig.OpenCoOpScreen,
            GIActions.OpenWishScreen => KeyConfig.OpenWishScreen,
            GIActions.OpenBattlePassScreen => KeyConfig.OpenBattlePassScreen,
            GIActions.OpenTheEventsMenu => KeyConfig.OpenTheEventsMenu,
            GIActions.OpenTheSettingsMenu => KeyConfig.OpenTheSettingsMenu,
            GIActions.OpenTheFurnishingScreen => KeyConfig.OpenTheFurnishingScreen,
            GIActions.OpenStellarReunion => KeyConfig.OpenStellarReunion,
            GIActions.OpenQuestMenu => KeyConfig.OpenQuestMenu,
            GIActions.OpenNotificationDetails => KeyConfig.OpenNotificationDetails,
            GIActions.OpenChatScreen => KeyConfig.OpenChatScreen,
            GIActions.OpenSpecialEnvironmentInformation => KeyConfig.OpenSpecialEnvironmentInformation,
            GIActions.CheckTutorialDetails => KeyConfig.CheckTutorialDetails,
            GIActions.ElementalSight => KeyConfig.ElementalSight,
            GIActions.ShowCursor => KeyConfig.ShowCursor,
            GIActions.OpenPartySetupScreen => KeyConfig.OpenPartySetupScreen,
            GIActions.OpenFriendsScreen => KeyConfig.OpenFriendsScreen,
            GIActions.HideUI => KeyConfig.HideUI,
            _ => default,
        };
    }

}
