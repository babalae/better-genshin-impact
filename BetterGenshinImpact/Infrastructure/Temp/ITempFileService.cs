using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Infrastructure.Storage;

namespace BetterGenshinImpact.Infrastructure.Temp;

/// <summary>
/// 临时文件服务。临时文件位于 Data/Temp 下本进程的目录中，进程退出时删除，进程异常结束后由下次启动回收
/// </summary>
public interface ITempFileService
{
    /// <summary>
    /// 创建一个独占的临时目录，释放时删除。用于解压、克隆等一次性操作
    /// </summary>
    TempScope CreateScope(TempCategory category);

    /// <summary>
    /// 返回一个尚未创建的临时文件路径，文件随进程退出删除。用于交给外部组件读取的文件
    /// </summary>
    /// <param name="category">所属分类</param>
    /// <param name="extension">扩展名，例如 .png</param>
    string CreateFilePath(TempCategory category, string extension);

    /// <summary>
    /// 清理某个分类：删除已结束进程的残留和本进程未在使用的文件，跳过其他运行中实例的目录
    /// </summary>
    Task<StorageCleanupResult> CleanupAsync(TempCategory category, CancellationToken ct);
}
