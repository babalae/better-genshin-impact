using System;

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
                "出战" => /* ActionEnum.ChooseFirst, */ throw new ArgumentOutOfRangeException(nameof(type), type, null),
                "切换" => /* ActionEnum.SwitchLater, */ throw new ArgumentOutOfRangeException(nameof(type), type, null),
                "使用" => ActionEnum.UseSkill,
                _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
            };
        }

        public static string ToChinese(this ActionEnum type)
        {
            return type switch
            {
                ActionEnum.ChooseFirst => "出战",
                ActionEnum.SwitchLater => "切换",
                ActionEnum.UseSkill => "使用",
                _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
            };
        }
    }
}
