using System;
using System.IO;
using System.Text;
using BetterGenshinImpact.Service.HoYoLab;

namespace BetterGenshinImpact.UnitTest.ServiceTests;

/// <summary>
/// HoYoLAB 登录会话：Cookie 过滤、加密保存与退出登录测试
/// </summary>
public sealed class HoYoLabSessionTests : IDisposable
{
    /// <summary>每个测试独立的临时会话文件</summary>
    private readonly string _filePath = Path.Combine(Path.GetTempPath(), $"hoyolab-session-{Guid.NewGuid():N}.bin");

    /// <summary>清理临时文件</summary>
    public void Dispose()
    {
        if (File.Exists(_filePath))
        {
            File.Delete(_filePath);
        }
    }

    /// <summary>只保留 hoyolab.com 域名下的 Cookie</summary>
    [Fact]
    public void BuildCookieHeader_FiltersDomains()
    {
        var header = HoYoLabSessionStore.BuildCookieHeader([
            new HoYoLabCookie("ltoken_v2", "a", ".hoyolab.com"),
            new HoYoLabCookie("ltuid_v2", "1", "hoyolab.com"),
            new HoYoLabCookie("x", "y", ".google.com"),
            new HoYoLabCookie("evil", "z", "nothoyolab.com"),
        ]);

        Assert.Equal("ltoken_v2=a; ltuid_v2=1", header);
    }

    /// <summary>没有 ltoken 说明尚未登录，返回 null</summary>
    [Fact]
    public void BuildCookieHeader_NoLtoken_ReturnsNull()
    {
        var header = HoYoLabSessionStore.BuildCookieHeader([new HoYoLabCookie("mi18nLang", "pt-pt", ".hoyolab.com")]);

        Assert.Null(header);
    }

    /// <summary>保存后可读回，文件内容不是明文</summary>
    [Fact]
    public void Store_RoundTrip()
    {
        var store = new HoYoLabSessionStore(_filePath);

        store.Save(new HoYoLabSession("ltoken_v2=a; ltuid_v2=1", "Fryhh"));
        var loaded = store.Load();

        Assert.Equal("ltoken_v2=a; ltuid_v2=1", loaded!.CookieHeader);
        Assert.Equal("Fryhh", loaded.Nickname);
        Assert.DoesNotContain("ltoken", Encoding.UTF8.GetString(File.ReadAllBytes(_filePath)));
    }

    /// <summary>损坏的文件视为未登录，不抛异常</summary>
    [Fact]
    public void Load_CorruptFile_ReturnsNull()
    {
        File.WriteAllBytes(_filePath, [1, 2, 3, 4, 5]);

        Assert.Null(new HoYoLabSessionStore(_filePath).Load());
    }

    /// <summary>退出登录删除会话文件并通知</summary>
    [Fact]
    public void Account_Logout_DeletesFileAndRaisesEvent()
    {
        var account = new HoYoLabAccountService(new HoYoLabSessionStore(_filePath));
        account.SetSession("ltoken_v2=a", "Fryhh");
        Assert.True(account.IsLoggedIn);
        var raised = 0;
        account.SessionChanged += (_, _) => raised++;

        account.Logout();

        Assert.False(account.IsLoggedIn);
        Assert.Null(account.CookieHeader);
        Assert.False(File.Exists(_filePath));
        Assert.Equal(1, raised);
    }

    /// <summary>新实例从文件恢复登录状态</summary>
    [Fact]
    public void Account_RestoresFromFile()
    {
        new HoYoLabAccountService(new HoYoLabSessionStore(_filePath)).SetSession("ltoken_v2=a", "Fryhh");

        var restored = new HoYoLabAccountService(new HoYoLabSessionStore(_filePath));

        Assert.True(restored.IsLoggedIn);
        Assert.Equal("Fryhh", restored.Nickname);
    }
}
