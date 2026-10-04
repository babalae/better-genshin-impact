using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Pulonia.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.UnitTest.Pulonia;

/// <summary>
/// 计划用途元数据的旧版兼容、序列化往返与深拷贝回归，不启动应用或游戏。
/// </summary>
public sealed class PuloniaTaskPlanPurposeTests
{
    /// <summary>
    /// 缺少 purpose 的旧版计划 JSON 按普通计划读取，格式版本保持不变。
    /// </summary>
    [Fact]
    public void ReadPlan_MissingPurposeDefaultsToGeneralAndKeepsSchemaVersion()
    {
        // 旧版 JSON 就是当前写出结果去掉 purpose 字段，其余内容完全一致。
        var written = JObject.Parse(PuloniaTaskJson.WritePlan(new PuloniaTaskPlan { Name = "旧版计划" }));
        written.Remove("purpose");

        var loaded = PuloniaTaskJson.ReadPlan(written.ToString());

        Assert.Equal(PuloniaTaskPlanPurpose.General, loaded.Purpose);
        Assert.Equal(PuloniaTaskPlan.CurrentSchemaVersion, loaded.SchemaVersion);
        Assert.Equal("旧版计划", loaded.Name);
    }

    /// <summary>
    /// purpose 显式为 null 违反“可选但不能为 null”的约定，读取必须失败。
    /// </summary>
    [Fact]
    public void ReadPlan_NullPurposeIsRejected()
    {
        var written = JObject.Parse(PuloniaTaskJson.WritePlan(new PuloniaTaskPlan { Name = "空用途计划" }));
        written["purpose"]!.Replace(JValue.CreateNull());

        Assert.ThrowsAny<JsonException>(() => PuloniaTaskJson.ReadPlan(written.ToString()));
    }

    /// <summary>
    /// 两种用途都以整数写出并原样读回，默认普通计划也必须写出 purpose 字段。
    /// </summary>
    [Fact]
    public void WriteAndReadPlan_RoundTripsBothPurposes()
    {
        var general = new PuloniaTaskPlan { Name = "普通计划往返" };
        var writtenGeneral = JObject.Parse(PuloniaTaskJson.WritePlan(general));
        Assert.Equal(0, writtenGeneral["purpose"]!.Value<int>());
        Assert.Equal(PuloniaTaskPlanPurpose.General, PuloniaTaskJson.ReadPlan(writtenGeneral.ToString()).Purpose);

        var oneDragon = new PuloniaTaskPlan { Name = "一条龙计划往返", Purpose = PuloniaTaskPlanPurpose.OneDragon };
        var writtenOneDragon = JObject.Parse(PuloniaTaskJson.WritePlan(oneDragon));
        Assert.Equal(1, writtenOneDragon["purpose"]!.Value<int>());
        Assert.Equal(PuloniaTaskPlanPurpose.OneDragon, PuloniaTaskJson.ReadPlan(writtenOneDragon.ToString()).Purpose);
    }

    /// <summary>
    /// 深拷贝与整份复制都保留用途标记，副本重建身份不影响该元数据。
    /// </summary>
    [Fact]
    public void CloneAndCopyPlan_PreservePurpose()
    {
        var oneDragon = PuloniaTaskJson.ClonePlan(
            new PuloniaTaskPlan { Name = "一条龙拷贝", Purpose = PuloniaTaskPlanPurpose.OneDragon });
        Assert.Equal(PuloniaTaskPlanPurpose.OneDragon, oneDragon.Purpose);

        var copy = PuloniaTaskJson.CopyPlan(oneDragon);
        Assert.Equal(PuloniaTaskPlanPurpose.OneDragon, copy.Purpose);
        Assert.NotEqual(oneDragon.Id, copy.Id);
        Assert.Equal(0, copy.Revision);

        var general = PuloniaTaskJson.ClonePlan(new PuloniaTaskPlan { Name = "普通拷贝" });
        Assert.Equal(PuloniaTaskPlanPurpose.General, general.Purpose);
    }
}
