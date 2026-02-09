using BetterGenshinImpact.Helpers;
﻿using System;

namespace BetterGenshinImpact.GameTask.AutoGeniusInvokation.Model
{
    public enum ActionEnum
    {
        ChooseFirst, SwitchLater, UseSkill
    }

    public static class ActionEnumExtension
    {
        public static ActionEnum ChineseToActionEnum(this string type)
        {
            type = type.ToLower();
            return type switch
            {
                Lang.S["GameTask_10936_8cbf72"] => /* ActionEnum.ChooseFirst, */ throw new ArgumentOutOfRangeException(nameof(type), type, null),
                Lang.S["GameTask_10935_bec7e4"] => /* ActionEnum.SwitchLater, */ throw new ArgumentOutOfRangeException(nameof(type), type, null),
                Lang.S["GameTask_10392_ecff77"] => ActionEnum.UseSkill,
                _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
            };
        }

        public static string ToChinese(this ActionEnum type)
        {
            return type switch
            {
                ActionEnum.ChooseFirst => Lang.S["GameTask_10936_8cbf72"],
                ActionEnum.SwitchLater => Lang.S["GameTask_10935_bec7e4"],
                ActionEnum.UseSkill => Lang.S["GameTask_10392_ecff77"],
                _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
            };
        }
    }
}
