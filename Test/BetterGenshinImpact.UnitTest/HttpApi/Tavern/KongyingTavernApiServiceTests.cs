using BetterGenshinImpact.Service.Tavern;
using Newtonsoft.Json;
using Xunit.Abstractions;

namespace BetterGenshinImpact.UnitTest.HttpApi.Tavern;

public class KongyingTavernApiServiceTests
{
    /// <summary>
    /// 这些用例访问空荧酒馆真实网络接口，只在手动调试接口时去掉跳过后执行。
    /// </summary>
    private const string SkipReason = "依赖空荧酒馆真实网络接口，仅手动调试时执行。";

    private readonly ITestOutputHelper _output;

    public KongyingTavernApiServiceTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact(Skip = SkipReason)]
    public async Task GetItemTypeListAsync_ShouldFetchAllLatestMd5Pages_AndPrint()
    {
        var service = new KongyingTavernApiService();
        try
        {
            var list = await service.GetItemTypeListAsync();

            _output.WriteLine($"ItemTypeVo count: {list.Count}");
            var preview = list.Take(10).ToList();
            var previewJson = JsonConvert.SerializeObject(preview, Formatting.Indented);
            _output.WriteLine(previewJson);

            Assert.NotEmpty(list);
        }
        catch (HttpRequestException ex)
        {
            _output.WriteLine("HttpRequestException: " + ex.Message);
        }
        catch (TaskCanceledException ex)
        {
            _output.WriteLine("TaskCanceledException: " + ex.Message);
        }
        catch (Exception ex)
        {
            _output.WriteLine(ex.ToString());
            throw;
        }
    }

    [Fact(Skip = SkipReason)]
    public async Task GetMarkerListAsync_ShouldFetchAllLatestMd5Pages_AndPrint()
    {
        var service = new KongyingTavernApiService();
        try
        {
            var list = await service.GetMarkerListAsync();

            _output.WriteLine($"MarkerVo count: {list.Count}");
            var preview = list.Take(10).ToList();
            var previewJson = JsonConvert.SerializeObject(preview, Formatting.Indented);
            _output.WriteLine(previewJson);

            Assert.NotEmpty(list);
        }
        catch (HttpRequestException ex)
        {
            _output.WriteLine("HttpRequestException: " + ex.Message);
        }
        catch (TaskCanceledException ex)
        {
            _output.WriteLine("TaskCanceledException: " + ex.Message);
        }
        catch (Exception ex)
        {
            _output.WriteLine(ex.ToString());
            throw;
        }
    }

    [Fact(Skip = SkipReason)]
    public async Task GetIconListAsync_AndPrint()
    {
        var service = new KongyingTavernApiService();
        try
        {
            var list = await service.GetIconListAsync();

            _output.WriteLine($"IconVo count: {list.Count}");
            var preview = list.Take(10).Select(x => new { x.Id, x.Tag, x.Url }).ToList();
            var previewJson = JsonConvert.SerializeObject(preview, Formatting.Indented);
            _output.WriteLine(previewJson);

            Assert.NotEmpty(list);
        }
        catch (HttpRequestException ex)
        {
            _output.WriteLine("HttpRequestException: " + ex.Message);
        }
        catch (TaskCanceledException ex)
        {
            _output.WriteLine("TaskCanceledException: " + ex.Message);
        }
        catch (Exception ex)
        {
            _output.WriteLine(ex.ToString());
            throw;
        }
    }
}
