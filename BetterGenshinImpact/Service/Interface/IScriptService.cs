using System.Collections.Generic;
using System.Threading.Tasks;

namespace BetterGenshinImpact.Service.Interface;

public interface IScriptService
{
    Task RunMulti(List<string> folderNameList, string? groupName = null);
}
