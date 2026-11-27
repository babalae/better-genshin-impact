using BetterGenshinImpact.Core.Script;
using BetterGenshinImpact.Core.Script.Dependence;
using BetterGenshinImpact.GameTask.Common.Job;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.Service.AccountRecords;
using Microsoft.ClearScript.V8;
using Newtonsoft.Json.Linq;
using OpenCvSharp;

namespace BetterGenshinImpact.UnitTest.ScriptTests;

/// <summary>离线验证图标入口、像素归一化、会话复用与退出清理。</summary>
[Collection("ScriptExecution")]
public sealed class ScriptInventoryIconRecognizerTests
{
    /// <summary>不加载模型的识别替身，同时断言模型输入契约。</summary>
    private sealed class Recognizer : IItemIconRecognizer
    {
        /// <summary>下一次识别结果。</summary>
        public string? Name { get; set; } = "摩拉";
        /// <summary>释放次数。</summary>
        public int Disposals { get; private set; }
        /// <summary>检查尺寸与 BGR 通道后返回预设结果。</summary>
        public string? Recognize(Mat icon)
        {
            Assert.Equal(new Size(125, 125), icon.Size());
            Assert.Equal(3, icon.Channels());
            Assert.Equal((byte)23, icon.At<Vec3b>(0, 0).Item0);
            return Name;
        }
        /// <summary>记录会话释放。</summary>
        public void Dispose() => Disposals++;
    }

    /// <summary>真实 JS 接收普通对象，空或目录外结果返回 null，正常退出释放一次。</summary>
    [Fact]
    public async Task NativeIconRecognitionReusesSessionAndDisposesOnExit()
    {
        var directory = Path.Combine(Path.GetTempPath(), "BetterGI.IconTest", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var csv = Path.Combine(directory, "items.csv");
            File.WriteAllText(csv, "item_name,material_type,page\n摩拉,货币\n");
            var catalog = new InventoryCatalog(csv);
            var model = new Recognizer();
            var loads = 0;
            using var region = new ImageRegion(new Mat(48, 64, MatType.CV_8UC4, new Scalar(23, 40, 60, 255)), 0, 0);
            await ScriptExecution.ExecuteAsync(ct =>
            {
                var engine = new V8ScriptEngine(V8ScriptEngineFlags.EnableTaskPromiseConversion);
                var service = new ScriptInventoryIconRecognizer(catalog, ct, () => { loads++; return model; });
                var host = new ScriptInventory(new InventoryStore(catalog, directory),
                    new ScriptInventoryContext("test/icons", "图标测试"),
                    () => throw new Exception("不应读取 UID"), ct, iconRecognizer: service);
                ScriptInventory.Register(engine, host);
                engine.AddHostObject("sample", region);
                engine.AddHostObject("setUnknown", new Action(() => model.Name = "未知物品"));
                return engine;
            }, engine => engine.Evaluate("""
                (async () => {
                    const first = inventory.identifyIcon(sample);
                    if (first.name !== '摩拉' || first.category !== '货币' || Object.getPrototypeOf(first) !== Object.prototype)
                        throw new Error('invalid result');
                    if ((await inventory.identifyIcon(sample)).name !== '摩拉') throw new Error('await failed');
                    setUnknown();
                    if (inventory.identifyIcon(sample) !== null) throw new Error('unknown accepted');
                })()
                """), default);
            Assert.Equal(1, loads);
            Assert.Equal(1, model.Disposals);
            Assert.False(region.SrcMat.Empty());
        }
        finally { Directory.Delete(directory, true); }
    }

    /// <summary>取消即释放模型且禁止重建；主动重复释放安全。</summary>
    [Fact]
    public void CancellationReleasesModelAndRejectsFurtherCalls()
    {
        var directory = Path.Combine(Path.GetTempPath(), "BetterGI.IconTest", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "items.csv");
            File.WriteAllText(path, "item_name,material_type,page\n摩拉,货币\n");
            using var ct = new CancellationTokenSource();
            var model = new Recognizer();
            using var service = new ScriptInventoryIconRecognizer(new InventoryCatalog(path), ct.Token, () => model);
            using var region = new ImageRegion(new Mat(32, 32, MatType.CV_8UC3, new Scalar(23, 40, 60)), 0, 0);
            Assert.Equal("摩拉", (string?)JObject.Parse(service.Identify(region))["name"]);
            ct.Cancel();
            Assert.Equal(1, model.Disposals);
            Assert.Throws<OperationCanceledException>(() => service.Identify(region));
            service.Dispose();
            Assert.Equal(1, model.Disposals);
        }
        finally { Directory.Delete(directory, true); }
    }
}
