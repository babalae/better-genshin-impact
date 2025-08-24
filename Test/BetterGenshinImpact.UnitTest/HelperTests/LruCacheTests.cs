using BetterGenshinImpact.Helpers;

namespace BetterGenshinImpact.UnitTest.HelperTests;

public class LruCacheTests
{
    [Fact]
    public void BasicSetGetTest()
    {
        // 基本读写测试
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
        // LRU淘汰测试
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
        // 访问后应更新为最新
        var cache = new CacheHelper.LruCache<string, string>(2);
        cache.Set("a", "1");
        cache.Set("b", "2");
        cache.TryGet("a", out _); // a变为最新
        cache.Set("c", "3"); // b应被淘汰
        Assert.True(cache.TryGet("a", out _));
        Assert.False(cache.TryGet("b", out _));
        Assert.True(cache.TryGet("c", out _));
    }

    [Fact]
    public void ExpireTest()
    {
        // 过期测试
        var cache = new CacheHelper.LruCache<string, string>(2, TimeSpan.FromMilliseconds(500));
        cache.Set("a", "1");
        Assert.True(cache.TryGet("a", out var v) && v == "1");
        Thread.Sleep(650);
        Assert.False(cache.TryGet("a", out _));
    }

    [Fact]
    public void RemoveTest()
    {
        // 移除测试
        var cache = new CacheHelper.LruCache<string, string>(2);
        cache.Set("a", "1");
        Assert.True(cache.Remove("a"));
        Assert.False(cache.TryGet("a", out _));
    }

    [Fact]
    public void ClearTest()
    {
        // 清空测试
        var cache = new CacheHelper.LruCache<string, string>(2);
        Assert.Equal(0, cache.Count);
        cache.Set("a", "1");
        cache.Set("b", "2");
        Assert.Equal(2, cache.Count);
        cache.Clear();
        Assert.Equal(0, cache.Count);
    }

    [Fact]
    public void BuilderTest()
    {
        // Builder 构建测试
        var cache = new CacheHelper.LruCacheBuilder<string, string>()
            .Capacity(2)
            .ExpireAfter(TimeSpan.FromMilliseconds(100))
            .Build();
        cache.Set("a", "1");
        Assert.True(cache.TryGet("a", out var v) && v == "1");
        Thread.Sleep(120);
        Assert.False(cache.TryGet("a", out _));
    }

    [Fact]
    public void WeakValueTest()
    {
        // 弱引用Value测试
        var cache = new CacheHelper.LruCacheBuilder<string, object>()
            .Capacity(2)
            .WeakValue()
            .Build();
        object obj = new object();
        cache.Set("a", obj);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        // 由于GC不可预测，弱引用value有可能被回收
        // var found =
        cache.TryGet("a", out var v);
        // 只要不抛异常即可
    }
}