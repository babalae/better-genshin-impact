using BetterGenshinImpact.GameTask.AutoFight.Model;
using BetterGenshinImpact.Helpers;
using System;
using System.Collections.Generic;
using TimeSpan = System.TimeSpan;

namespace BetterGenshinImpact.GameTask.AutoFight.Script;

public class CombatCommand
{
    public string Name { get; set; }

    public Method Method { get; set; }

    public List<string>? Args { get; set; }

    public CombatCommand(string name, string command)
    {
        Name = name.Trim();
        command = command.Trim();
        var startIndex = command.IndexOf('(');
        if (startIndex > 0)
        {
            var endIndex = command.IndexOf(')');
            var method = command[..startIndex];
            method = method.Trim();
            Method = Method.GetEnumByCode(method);

            var parameters = command.Substring(startIndex + 1, endIndex - startIndex - 1);
            Args = [..parameters.Split(',', StringSplitOptions.TrimEntries)];
        }
        else
        {
            Method = Method.GetEnumByCode(command);
            Args = [];
        }

        // 校验参数
        if (Method == Method.Walk)
        {
            AssertUtils.IsTrue(Args.Count == 2, "walk方法必须有两个入参，第一个参数是方向，第二个参数是行走时间。例：walk(s, 0.2)");
            var s = double.Parse(Args[1]);
            AssertUtils.IsTrue(s > 0, "行走时间必须大于0");
        }
        else if (Method == Method.W || Method == Method.A || Method == Method.S || Method == Method.D)
        {
            AssertUtils.IsTrue(Args.Count == 1, "w/a/s/d方法必须有一个入参，代表行走时间。例：d(0.5)");
        }
        else if (Method == Method.MoveBy)
        {
            AssertUtils.IsTrue(Args.Count == 2, "moveby方法必须有两个入参，分别是x和y。例：moveby(100, 100))");
        }
        else if (Method == Method.KeyDown || Method == Method.KeyUp || Method == Method.KeyPress)
        {
            AssertUtils.IsTrue(Args.Count == 1, $"{Method.Alias[0]}方法必须有一个入参，代表按键");
            try
            {
                User32Helper.ToVk(Args[0]);
            }
            catch
            {
                throw new ArgumentException($"{Method.Alias[0]}方法的入参必须是VirtualKeyCodes枚举中的值，当前入参 {Args[0]} 不合法");
            }
        }
    }

    public void Execute(CombatScenes combatScenes)
    {
        Avatar? avatar;
        if (Name == CombatScriptParser.CurrentAvatarName)
        {
            avatar = combatScenes.SelectAvatar(1);
        }
        else
        {
            // 其余情况要进行角色切换
            avatar = combatScenes.SelectAvatar(Name);
            if (avatar == null)
            {
                return;
            }
            // 非宏类脚本，等待切换角色成功
            if (Method != Method.Wait
                && Method != Method.MouseDown
                && Method != Method.MouseUp
                && Method != Method.Click
                && Method != Method.MoveBy
                && Method != Method.KeyDown
                && Method != Method.KeyUp
                && Method != Method.KeyPress)
            {
                avatar.Switch();
            }
        }
        Execute(avatar);
    }

    public void Execute(Avatar avatar)
    {
        if (Method == Method.Skill)
        {
            var hold = Args != null && Args.Contains("hold");
            var wait = Args != null && Args.Contains("wait");
            var fast = Args != null && Args.Contains("fast");
            if (fast)
            {
                // 快速跳过e
                if (!avatar.IsSkillReady(true))
                {
                    return;
                }
            }
            else if (wait)
            {
                // 等待e结束,同步等待
                avatar.WaitSkillCd().Wait();
            }

            avatar.UseSkill(hold);
        }
        else if (Method == Method.Burst)
        {
            avatar.UseBurst();
        }
        else if (Method == Method.Attack)
        {
            if (Args is { Count: > 0 })
            {
                var s = double.Parse(Args![0]);
                avatar.Attack((int)TimeSpan.FromSeconds(s).TotalMilliseconds);
            }
            else
            {
                avatar.Attack();
            }
        }
        else if (Method == Method.Charge)
        {
            if (Args is { Count: > 0 })
            {
                var s = double.Parse(Args![0]);
                avatar.Charge((int)TimeSpan.FromSeconds(s).TotalMilliseconds);
            }
            else
            {
                avatar.Charge();
            }
        }
        else if (Method == Method.Walk)
        {
            var s = double.Parse(Args![1]);
            avatar.Walk(Args![0], (int)TimeSpan.FromSeconds(s).TotalMilliseconds);
        }
        else if (Method == Method.W)
        {
            var s = double.Parse(Args![0]);
            avatar.Walk("w", (int)TimeSpan.FromSeconds(s).TotalMilliseconds);
        }
        else if (Method == Method.A)
        {
            var s = double.Parse(Args![0]);
            avatar.Walk("a", (int)TimeSpan.FromSeconds(s).TotalMilliseconds);
        }
        else if (Method == Method.S)
        {
            var s = double.Parse(Args![0]);
            avatar.Walk("s", (int)TimeSpan.FromSeconds(s).TotalMilliseconds);
        }
        else if (Method == Method.D)
        {
            var s = double.Parse(Args![0]);
            avatar.Walk("d", (int)TimeSpan.FromSeconds(s).TotalMilliseconds);
        }
        else if (Method == Method.Wait)
        {
            var s = double.Parse(Args![0]);
            avatar.Wait((int)TimeSpan.FromSeconds(s).TotalMilliseconds);
        }
        else if (Method == Method.Aim)
        {
            throw new NotImplementedException();
        }
        else if (Method == Method.Dash)
        {
            if (Args is { Count: > 0 })
            {
                var s = double.Parse(Args![0]);
                avatar.Dash((int)TimeSpan.FromSeconds(s).TotalMilliseconds);
            }
            else
            {
                avatar.Dash();
            }
        }
        else if (Method == Method.Jump)
        {
            avatar.Jump();
        }
        // 宏
        else if (Method == Method.MouseDown)
        {
            if (Args is { Count: > 0 })
            {
                avatar.MouseDown(Args![0]);
            }
            else
            {
                avatar.MouseDown();
            }
        }
        else if (Method == Method.MouseUp)
        {
            if (Args is { Count: > 0 })
            {
                avatar.MouseUp(Args![0]);
            }
            else
            {
                avatar.MouseUp();
            }
        }
        else if (Method == Method.Click)
        {
            if (Args is { Count: > 0 })
            {
                avatar.Click(Args![0]);
            }
            else
            {
                avatar.Click();
            }
        }
        else if (Method == Method.MoveBy)
        {
            if (Args is { Count: 2 })
            {
                var x = int.Parse(Args![0]);
                var y = int.Parse(Args[1]);
                avatar.MoveBy(x, y);
            }
            else
            {
                throw new ArgumentException("moveby方法必须有两个入参，分别是x和y。例：moveby(100, 100)");
            }
        }
        else if (Method == Method.KeyDown)
        {
            avatar.KeyDown(Args![0]);
        }
        else if (Method == Method.KeyUp)
        {
            avatar.KeyUp(Args![0]);
        }
        else if (Method == Method.KeyPress)
        {
            avatar.KeyPress(Args![0]);
        }
        else
        {
            throw new NotImplementedException();
        }
    }
}