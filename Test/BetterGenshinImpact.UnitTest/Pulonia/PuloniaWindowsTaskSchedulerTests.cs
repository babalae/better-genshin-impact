using System.Runtime.InteropServices;
using BetterGenshinImpact.Pulonia.Services;

namespace BetterGenshinImpact.UnitTest.Pulonia;

/// <summary>Windows 唤起任务撤销的异常映射回归；不调用真实 COM 服务或修改系统任务。</summary>
public sealed class PuloniaWindowsTaskSchedulerTests
{
    /// <summary>任务存在时正常调用一次删除操作，不执行额外动作。</summary>
    [Fact]
    public void DeleteTaskIfPresent_DeletesExistingTaskOnce()
    {
        var calls = 0;
        PuloniaWindowsTaskScheduler.DeleteTaskIfPresent(() => calls++);
        Assert.Equal(1, calls);
    }

    /// <summary>动态 COM 映射的文件不存在异常不再造成撤销失败，重复撤销仍幂等。</summary>
    [Fact]
    public void DeleteTaskIfPresent_IgnoresMappedFileNotFound()
    {
        var calls = 0;
        var exception = new FileNotFoundException("系统找不到指定的文件。");
        Assert.Equal(0x80070002u, (uint)exception.HResult);
        for (var attempt = 0; attempt < 2; attempt++)
            PuloniaWindowsTaskScheduler.DeleteTaskIfPresent(() =>
            {
                calls++;
                throw exception;
            });
        Assert.Equal(2, calls);
    }

    /// <summary>未映射的 COM 任务不存在错误仍按幂等撤销处理。</summary>
    [Fact]
    public void DeleteTaskIfPresent_IgnoresComFileNotFound()
    {
        PuloniaWindowsTaskScheduler.DeleteTaskIfPresent(() =>
            throw new COMException("系统找不到指定的文件。", unchecked((int)0x80070002)));
    }

    /// <summary>拒绝访问、服务未运行或路径错误必须继续抛出，不能伪装成撤销成功。</summary>
    [Theory]
    [InlineData(unchecked((int)0x80070005))]
    [InlineData(unchecked((int)0x80041315))]
    [InlineData(unchecked((int)0x80070003))]
    public void DeleteTaskIfPresent_PreservesOtherComFailures(int hresult)
    {
        var exception = new COMException("删除任务失败。", hresult);
        var actual = Assert.Throws<COMException>(() =>
            PuloniaWindowsTaskScheduler.DeleteTaskIfPresent(() => throw exception));
        Assert.Same(exception, actual);
    }

    /// <summary>即使错误码相同，也不吞掉非预期异常类型，避免掩盖其他 I/O 故障。</summary>
    [Fact]
    public void DeleteTaskIfPresent_PreservesUnexpectedExceptionType()
    {
        var exception = new IOException("其他 I/O 故障。", unchecked((int)0x80070002));
        var actual = Assert.Throws<IOException>(() =>
            PuloniaWindowsTaskScheduler.DeleteTaskIfPresent(() => throw exception));
        Assert.Same(exception, actual);
    }
}
