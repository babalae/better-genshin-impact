using BetterGenshinImpact.GameTask.AutoFight.Config;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.AutoFightTests
{
    public partial class DefaultAutoFightConfigTests
    {
        /// <summary>
        /// 测试默认战斗配置初始化，获取所有别名，结果应成功
        /// </summary>
        [Fact]
        public void DefaultAutoFightConfig_Init_AllAliasShouldSuccess()
        {
            //
            // Do nothing

            //
            var sut = DefaultAutoFightConfig.CombatAvatarAliasToNameMap;

            //
            Assert.All(sut.Keys, alias => DefaultAutoFightConfig.AvatarAliasToStandardName(alias)); // 顺便测试一下查询别名方法的速度
        }
    }
}
