using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Service.HoYoLab;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.UnitTest.ServiceTests;

/// <summary>
/// HoYoLAB 地图标记接口的请求格式与返回码处理测试
/// </summary>
public sealed class HoYoLabMarkApiServiceTests
{
    /// <summary>测试用的会话 Cookie</summary>
    private const string Cookie = "ltoken_v2=a; ltuid_v2=1";

    /// <summary>记录发出的请求并返回固定响应的处理器</summary>
    private sealed class Handler(string responseJson) : HttpMessageHandler
    {
        /// <summary>最后一次请求</summary>
        public HttpRequestMessage? Request { get; private set; }

        /// <summary>最后一次请求体</summary>
        public string? Body { get; private set; }

        /// <summary>记录请求并返回固定 JSON</summary>
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Request = request;
            Body = request.Content == null ? null : await request.Content.ReadAsStringAsync(token);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
            };
        }
    }

    /// <summary>用给定响应创建服务</summary>
    private static (HoYoLabMarkApiService Service, Handler Handler) Create(string json)
    {
        var handler = new Handler(json);
        return (new HoYoLabMarkApiService(new HttpClient(handler)), handler);
    }

    /// <summary>读取账号标记列表：解析点位 ID，并带上会话与站点请求头</summary>
    [Fact]
    public async Task GetMarked_ParsesPointIds()
    {
        var (service, handler) = Create("""{"retcode":0,"message":"OK","data":{"list":[{"point_id":323},{"point_id":88512}]}}""");

        var ids = await service.GetMarkedPointIdsAsync(Cookie, 2, "pt-pt", CancellationToken.None);

        Assert.Equal(new long[] { 323, 88512 }, ids.OrderBy(x => x));
        var request = handler.Request!;
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("sg-public-api.hoyolab.com", request.RequestUri!.Host);
        Assert.Contains("mark_map_point_list?map_id=2&app_sn=ys_obc&lang=pt-pt", request.RequestUri.AbsoluteUri);
        Assert.Equal(Cookie, request.Headers.GetValues("Cookie").Single());
        Assert.Equal("4.5", request.Headers.GetValues("x-rpc-map_version").Single());
        Assert.Equal("https://act.hoyolab.com/", request.Headers.Referrer!.AbsoluteUri);
    }

    /// <summary>标记点位：POST 到 add_mark_map_point，请求体与网页一致</summary>
    [Fact]
    public async Task AddMark_PostsExpectedBody()
    {
        var (service, handler) = Create("""{"retcode":0,"message":"OK","data":{"id":47251667}}""");

        await service.AddMarkAsync(Cookie, 2, 88512, "pt-pt", CancellationToken.None);

        Assert.Equal(HttpMethod.Post, handler.Request!.Method);
        Assert.EndsWith("/v1/map/point/add_mark_map_point", handler.Request.RequestUri!.AbsolutePath);
        var expected = JObject.Parse("""{"map_id":2,"point_id":88512,"app_sn":"ys_obc","lang":"pt-pt"}""");
        Assert.True(JToken.DeepEquals(expected, JObject.Parse(handler.Body!)));
    }

    /// <summary>取消标记：使用 new_del_mark_map_point</summary>
    [Fact]
    public async Task DeleteMark_UsesNewDelEndpoint()
    {
        var (service, handler) = Create("""{"retcode":0,"message":"OK","data":{}}""");

        await service.DeleteMarkAsync(Cookie, 2, 88512, "pt-pt", CancellationToken.None);

        Assert.Equal(HttpMethod.Post, handler.Request!.Method);
        Assert.EndsWith("/v1/map/point/new_del_mark_map_point", handler.Request.RequestUri!.AbsolutePath);
    }

    /// <summary>登录失效的返回码抛出会话过期异常</summary>
    [Theory]
    [InlineData(-100)]
    [InlineData(10001)]
    public async Task ThrowsSessionExpired(int retcode)
    {
        var (service, _) = Create($$"""{"retcode":{{retcode}},"message":"x"}""");

        await Assert.ThrowsAsync<HoYoLabSessionExpiredException>(
            () => service.GetMarkedPointIdsAsync(Cookie, 2, "pt-pt", CancellationToken.None));
    }

    /// <summary>其他非 0 返回码抛出接口异常并保留返回码</summary>
    [Fact]
    public async Task ThrowsApiException_OnOtherRetcode()
    {
        var (service, _) = Create("""{"retcode":-1,"message":"busy"}""");

        var ex = await Assert.ThrowsAsync<HoYoLabApiException>(
            () => service.AddMarkAsync(Cookie, 2, 1, "pt-pt", CancellationToken.None));
        Assert.Equal(-1, ex.Retcode);
    }
}
