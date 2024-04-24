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

    public string GetScriptContent()
    {
        return GetScriptContent(TaskContext.Instance().Config.MacroConfig.CombatMacroPriority);
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
