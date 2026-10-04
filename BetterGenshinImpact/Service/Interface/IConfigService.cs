using BetterGenshinImpact.Core.Config;

namespace BetterGenshinImpact.Service.Interface
{
    public interface IConfigService
    {
        AllConfig Get();

        /// <summary>
        /// 立即同步写盘
        /// </summary>
        void Save();

        /// <summary>
        /// 有未保存的改动时立即同步写盘（退出、重启、启动其他实例前调用）
        /// </summary>
        void Flush();

        AllConfig Read();

        void Write(AllConfig config);
    }
}
