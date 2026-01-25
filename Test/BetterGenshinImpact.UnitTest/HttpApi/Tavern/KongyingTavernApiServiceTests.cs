using BetterGenshinImpact.Service.Tavern;
using Newtonsoft.Json;
using Xunit.Abstractions;

namespace BetterGenshinImpact.UnitTest.HttpApi.Tavern;

public class KongyingTavernApiServiceTests
{
    private readonly ITestOutputHelper _output;

    public KongyingTavernApiServiceTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
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

    [Fact]
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

    [Fact]
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
