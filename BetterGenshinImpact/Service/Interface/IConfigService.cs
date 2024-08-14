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
