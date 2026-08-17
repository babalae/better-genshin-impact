using System.Diagnostics;
using BetterGenshinImpact.GameTask;

namespace BetterGenshinImpact.UnitTest.GameTaskTests;

public class SystemControlStartInfoTests
{
    [Fact]
    public void CmdWrapperUsesHiddenConsoleWithoutStart()
    {
        const string path = @"C:\BetterGI scripts\Start-Genshin.cmd";
        const string workdir = @"C:\BetterGI scripts";

        var startInfo = SystemControl.BuildLocalGameStartInfo(
            path,
            workdir,
            "-popupwindow",
            startGameWithCmd: true);

        Assert.Equal("cmd.exe", startInfo.FileName);
        Assert.False(startInfo.UseShellExecute);
        Assert.True(startInfo.CreateNoWindow);
        Assert.Equal(ProcessWindowStyle.Hidden, startInfo.WindowStyle);
        Assert.Empty(startInfo.ArgumentList);
        Assert.Equal($"/d /s /c \"\"{path}\" -popupwindow\"", startInfo.Arguments);
        Assert.DoesNotContain("start ", startInfo.Arguments);
    }

    [Fact]
    public void ExecutableKeepsCmdStartCompatibility()
    {
        const string path = @"C:\Genshin Impact\YuanShen.exe";
        const string workdir = @"C:\Genshin Impact";

        var startInfo = SystemControl.BuildLocalGameStartInfo(
            path,
            workdir,
            "-popupwindow",
            startGameWithCmd: true);

        Assert.Empty(startInfo.ArgumentList);
        Assert.Equal(
            $"/d /s /c \"start \"\" /d \"{workdir}\" \"{path}\" -popupwindow\"",
            startInfo.Arguments);
    }
}
