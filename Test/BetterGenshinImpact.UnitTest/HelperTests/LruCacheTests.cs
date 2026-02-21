using BetterGenshinImpact.Helpers;

namespace BetterGenshinImpact.UnitTest.HelperTests;

public class LruCacheTests
{
    [Fact]
    public void BasicSetGetTest()
    {
        var cache = new CacheHelper.LruCache<string, string>(3);
        cache.Set("a", "1");
        cache.Set("b", "2");
        cache.Set("c", "3");
        Assert.True(cache.TryGet("a", out var v1) && v1 == "1");
        Assert.True(cache.TryGet("b", out var v2) && v2 == "2");
        Assert.True(cache.TryGet("c", out var v3) && v3 == "3");
    }

    [Fact]
    public void LruEvictionTest()
    {
        var cache = new CacheHelper.LruCache<string, string>(2);
        cache.Set("a", "1");
        cache.Set("b", "2");
        cache.Set("c", "3"); // "a" 应被淘汰
        Assert.False(cache.TryGet("a", out _));
        Assert.True(cache.TryGet("b", out var v2) && v2 == "2");
        Assert.True(cache.TryGet("c", out var v3) && v3 == "3");
    }

    [Fact]
    public void UpdateMovesToHeadTest()
    {
        var cache = new CacheHelper.LruCache<string, string>(2);
        cache.Set("a", "1");
        cache.Set("b", "2");
        cache.TryGet("a", out _); // a 变为最新
        cache.Set("c", "3"); // b 应被淘汰
        Assert.True(cache.TryGet("a", out _));
        Assert.False(cache.TryGet("b", out _));
        Assert.True(cache.TryGet("c", out _));
    }

    [Fact]
    public void ExpireTest()
    {
        var cache = new CacheHelper.LruCache<string, string>(2, TimeSpan.FromMilliseconds(500));
        cache.Set("a", "1");
        Assert.True(cache.TryGet("a", out var v) && v == "1");
        Thread.Sleep(650);
        Assert.False(cache.TryGet("a", out _));
    }

    [Fact]
    public void RemoveTest()
    {
        var cache = new CacheHelper.LruCache<string, string>(2);
        cache.Set("a", "1");
        Assert.True(cache.Remove("a"));
        Assert.False(cache.TryGet("a", out _));
    }

    [Fact]
    public void ClearTest()
    {
        var cache = new CacheHelper.LruCache<string, string>(2);
        Assert.Equal(0, cache.Count);
        cache.Set("a", "1");
        cache.Set("b", "2");
        Assert.Equal(2, cache.Count);
        cache.Clear();
        Assert.Equal(0, cache.Count);
    }
}
