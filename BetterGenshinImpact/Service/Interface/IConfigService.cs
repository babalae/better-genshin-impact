using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Config;

namespace BetterGenshinImpact.Service.Interface
{
    public interface IConfigService
    {
        AllConfig Get();

        void Save();

        AllConfig Read();

        void Write(AllConfig config);
    }
}
