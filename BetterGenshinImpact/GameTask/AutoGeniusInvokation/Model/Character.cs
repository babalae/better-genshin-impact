using BetterGenshinImpact.Core.Recognition.OpenCv;
using BetterGenshinImpact.Helpers.Extensions;
using OpenCvSharp;
using System.Collections.Generic;
using System.Text;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.GameTask.AutoGeniusInvokation.Model
{
    public class Character
    {
        private readonly ILogger _logger = App.GetLogger<Character>();

        /// <summary>
        /// 1-3 所在数组下标一致
        /// </summary>
        public int Index { get; set; }

        public string Name { get; set; }
        public ElementalType Element { get; set; }
        public Skill[] Skills { get; set; }


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
        public List<CharacterStatusEnum> StatusList { get; set; } = new List<CharacterStatusEnum>();

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
            StringBuilder sb = new StringBuilder();
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

        public void ChooseFirst()
        {
            ClickExtension.Move(GeniusInvokationControl.GetInstance().MakeOffset(Area.GetCenterPoint()))
                .LeftButtonClick()
                .Sleep(200)
                .LeftButtonClick();
        }

        public bool SwitchLater()
        {
            var p = GeniusInvokationControl.GetInstance().MakeOffset(Area.GetCenterPoint());
            // 选择角色
            p.Click();

            // 点击切人按钮
            GeniusInvokationControl.GetInstance().ActionPhasePressSwitchButton();
            return true;
        }

        /// <summary>
        /// 角色被打败的时候双击角色牌重新出战
        /// </summary>
        /// <returns></returns>
        public void SwitchWhenTakenOut()
        {
            _logger.LogInformation("有角色被打败,当前选择{Name}出战", Name);
            var p = GeniusInvokationControl.GetInstance().MakeOffset(Area.GetCenterPoint());
            // 选择角色
            p.Click();
            // 双击切人
            GeniusInvokationControl.GetInstance().Sleep(500);
            p.Click();
            GeniusInvokationControl.GetInstance().Sleep(300);
        }

        public bool UseSkill(int skillIndex, Duel duel)
        {
            var res = GeniusInvokationControl.GetInstance().ActionPhaseAutoUseSkill(skillIndex, Skills[skillIndex].SpecificElementCost, Skills[skillIndex].Type, duel);
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