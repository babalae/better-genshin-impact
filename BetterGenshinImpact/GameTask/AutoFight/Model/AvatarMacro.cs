using BetterGenshinImpact.GameTask.AutoFight.Script;
using System.Collections.Generic;

namespace BetterGenshinImpact.GameTask.AutoFight.Model;

public class AvatarMacro
{
    public string Name { get; set; } = string.Empty;
    public string ScriptContent1 { get; set; } = string.Empty;
    public string ScriptContent2 { get; set; } = string.Empty;
    public string ScriptContent3 { get; set; } = string.Empty;
    public string ScriptContent4 { get; set; } = string.Empty;
    public string ScriptContent5 { get; set; } = string.Empty;

    /// <summary>
    /// 角色当前使用的战斗宏编号 (1-5)，如果为0则使用默认宏1
    /// </summary>
    public int MacroPriority { get; set; } = 0;

    public string GetScriptContent(int index)
    {
        return index switch
        {
            1 => ScriptContent1,
            2 => ScriptContent2,
            3 => ScriptContent3,
            4 => ScriptContent4,
            5 => ScriptContent5,
            _ => string.Empty
        };
    }

    /// <summary>
    /// 验证宏优先级是否有效
    /// </summary>
    /// <returns>如果优先级有效返回true，否则返回false</returns>
    public bool IsValidMacroPriority()
    {
        return MacroPriority >= 0 && MacroPriority <= 5;
    }

    public string GetScriptContent()
    {
        // 验证宏优先级的有效性
        if (!IsValidMacroPriority())
        {
            MacroPriority = 0; // 重置为默认值
        }

        // 如果角色设置了自己的宏优先级，使用角色的；否则使用全局配置
        var priority = MacroPriority > 0 ? MacroPriority :
                      TaskContext.Instance().Config.MacroConfig.CombatMacroPriority;

        // 确保最终优先级在有效范围内
        if (priority < 1 || priority > 5)
        {
            priority = 1; // 默认使用宏1
        }

        return GetScriptContent(priority);
    }

    public List<CombatCommand>? LoadCommands()
    {
        var content = GetScriptContent();
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }
        return CombatScriptParser.ParseLineCommands(content, Name);
    }
}
