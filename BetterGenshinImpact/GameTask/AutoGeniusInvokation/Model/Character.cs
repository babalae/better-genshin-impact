using BetterGenshinImpact.Core.Recognition.OpenCv;
using BetterGenshinImpact.Helpers.Extensions;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using System.Collections.Generic;
using System.Text;

namespace BetterGenshinImpact.GameTask.AutoGeniusInvokation.Model
{
    public class Character
    {
        private readonly ILogger _logger = App.GetLogger<Character>();

        /// <summary>
        /// 1-3 所在数组下标一致
        /// </summary>
        public int Index { get; set; }

        public string Name { get; set; } = default!;
        public ElementalType Element { get; set; }
        public Skill[] Skills { get; set; } = default!;


        /// <summary>
        /// 是否被打败
        /// </summary>
        public bool IsDefeated { get; set; }

        /// <summary>
        /// 充能点
        /// </summary>
        public int Energy { get; set; }

        /// <summary>
        /// hp
        /// -2 未识别到
        /// </summary>
        public int Hp { get; set; } = -2;


        /// <summary>
        /// 充能点来自于图像识别
        /// </summary>
        public int EnergyByRecognition { get; set; }

        /// <summary>
        /// 角色身上的负面状态
        /// </summary>
        public List<CharacterStatusEnum> StatusList { get; set; } = [];

        /// <summary>
        /// 角色区域
        /// </summary>
        public Rect Area { get; set; }

        /// <summary>
        /// 血量上方区域，用于判断是否出战
        /// </summary>
        public Rect HpUpperArea { get; set; }

        public override string ToString()
        {
            StringBuilder sb = new();
            sb.Append($"角色{Index}，");
            if (Hp != -2)
            {
                sb.Append($"HP={Hp}，");
            }
            sb.Append($"充能={EnergyByRecognition}，");
            if (StatusList?.Count > 0)
            {
                sb.Append($"状态：{string.Join(",", StatusList)}");
            }

            return sb.ToString();
        }

        /// <summary>
        /// 选择该角色作为首个出战角色
        /// </summary>
        /// <param name="control">本次对局的游戏操控对象</param>
        public void ChooseFirst(GeniusInvokationControl control)
        {
            ClickExtension.Move(control.MakeOffset(Area.GetCenterPoint()))
                .LeftButtonClick()
                .Sleep(200)
                .LeftButtonClick();
        }

        /// <summary>
        /// 切换到该角色
        /// </summary>
        /// <param name="control">本次对局的游戏操控对象</param>
        public bool SwitchLater(GeniusInvokationControl control)
        {
            control.ClickGameWindowCenter();
            control.Sleep(800);
            var p = control.MakeOffset(Area.GetCenterPoint());
            // 选择角色
            p.Click();

            // 点击切人按钮
            control.ActionPhasePressSwitchButton();
            return true;
        }

        /// <summary>
        /// 角色被打败的时候双击角色牌重新出战
        /// </summary>
        /// <param name="control">本次对局的游戏操控对象</param>
        /// <returns></returns>
        public void SwitchWhenTakenOut(GeniusInvokationControl control)
        {
            _logger.LogInformation("有角色被打败,当前选择{Name}出战", Name);
            var p = control.MakeOffset(Area.GetCenterPoint());
            // 选择角色
            p.Click();
            // 双击切人
            control.Sleep(500);
            p.Click();
            control.Sleep(300);
        }

        /// <summary>
        /// 使用技能，元素骰子不足时自动烧牌
        /// </summary>
        /// <param name="control">本次对局的游戏操控对象</param>
        /// <param name="skillIndex">技能编号</param>
        /// <param name="duel">对局对象</param>
        public bool UseSkill(GeniusInvokationControl control, int skillIndex, Duel duel)
        {
            var res = control.ActionPhaseAutoUseSkill(skillIndex, Skills[skillIndex].SpecificElementCost, Skills[skillIndex].Type, duel);
            if (res)
            {
                return true;
            }
            else
            {
                _logger.LogWarning("没有足够的手牌或元素骰子释放技能");
                return false;
            }
        }
    }
}