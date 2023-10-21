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
            switch (type)
            {
                case "出战":
                    //return ActionEnum.ChooseFirst;
                    throw new ArgumentOutOfRangeException(nameof(type), type, null);
                case "切换":
                    //return ActionEnum.SwitchLater;
                    throw new ArgumentOutOfRangeException(nameof(type), type, null);
                case "使用":
                    return ActionEnum.UseSkill;
                default:
                    throw new ArgumentOutOfRangeException(nameof(type), type, null);
            }
        }

        public static string ToChinese(this ActionEnum type)
        {
            switch (type)
            {
                case ActionEnum.ChooseFirst:
                    return "出战";
                case ActionEnum.SwitchLater:
                    return "切换";
                case ActionEnum.UseSkill:
                    return "使用";
                default:
                    throw new ArgumentOutOfRangeException(nameof(type), type, null);
            }
        }
    }
}
