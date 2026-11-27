using BetterGenshinImpact.Core.Recognition.ONNX;

namespace BetterGenshinImpact.UnitTest.CoreTests.RecognitionTests;

public sealed class InferenceDeviceDiscoveryTests
{
    private const string Amd = "1002:1638";
    private const string Nvidia = "10DE:25E0";
    private const string AmdName = "AMD Radeon(TM) Graphics";
    private const string NvidiaName = "NVIDIA GeForce RTX 3050 Ti Laptop GPU";

    private static DirectMlAdapterCandidate RealPci(string model, int index, string name, string pci)
        => new(model, PciAddressState.Confirmed, new($"DXGI:{model}:{pci}", name, index, index.ToString()));

    // 系统明确回复该入口没有 PCI 位置：查询成功但返回全 F，因此同型号入口的身份字符串完全相同。
    private static DirectMlAdapterCandidate NoPciAddress(string model, int index, string name)
        => new(model, PciAddressState.Absent, new($"DXGI:{model}:4294967295:65535:65535", name, index, index.ToString()));

    // 查询失败时改用型号与 LUID 拼身份，所以每个入口的身份都不同。
    private static DirectMlAdapterCandidate UnknownPci(string model, int index, string name, string luid)
        => new(model, PciAddressState.Unknown, new($"DXGI:{model}:3AAC17AA:{luid}", name, index, index.ToString()));

    private static IReadOnlyList<InferenceDeviceInfo> Resolve(DirectMlAdapterCandidate[] candidates)
        => InferenceDeviceDiscovery.NormalizeDirectMlDevices(
            InferenceDeviceDiscovery.FilterVirtualAdapterDuplicates(candidates));

    [Fact]
    public void VirtualDisplayDuplicates_AreDroppedWhenSameModelPhysicalAdapterIsListed()
    {
        // 实机数据：三个 IddCx 虚拟显示驱动让 DXGI 为同一块 AMD 核显多报了三个没有 PCI 位置的入口。
        var devices = Resolve(
        [
            RealPci(Amd, 0, AmdName, "6:0:0"),
            RealPci(Nvidia, 1, NvidiaName, "1:0:0"),
            NoPciAddress(Amd, 2, AmdName),
            NoPciAddress(Amd, 3, AmdName),
            NoPciAddress(Amd, 4, AmdName)
        ]);

        Assert.Equal(2, devices.Count);
        Assert.Equal(AmdName, devices[0].Name);
        Assert.Equal(0, devices[0].Index);
        Assert.Equal(NvidiaName, devices[1].Name);
        Assert.Equal(1, devices[1].Index);
    }

    [Fact]
    public void VirtualDuplicates_AreDroppedRegardlessOfEnumerationOrder()
    {
        var devices = Resolve(
        [
            NoPciAddress(Amd, 0, AmdName),
            RealPci(Amd, 1, AmdName, "6:0:0")
        ]);

        Assert.Single(devices);
        Assert.Equal(1, devices[0].Index);
        Assert.Equal(AmdName, devices[0].Name);
    }

    [Fact]
    public void Adapters_AreKeptWhenNoPhysicalAdapterExists()
    {
        // 虚拟机 GPU 直通场景：唯一的可用显卡就是没有 PCI 位置的入口，过滤后不能空掉。
        var devices = Resolve(
        [
            NoPciAddress(Amd, 0, AmdName),
            NoPciAddress(Nvidia, 1, "NVIDIA Virtual GPU")
        ]);

        Assert.Equal(2, devices.Count);
    }

    [Fact]
    public void NoPciAddressAdapterOfDifferentModel_IsKept()
    {
        var devices = Resolve(
        [
            RealPci(Amd, 0, AmdName, "6:0:0"),
            NoPciAddress(Nvidia, 1, "NVIDIA Virtual GPU")
        ]);

        Assert.Equal(2, devices.Count);
        Assert.Equal("NVIDIA Virtual GPU", devices[1].Name);
    }

    [Fact]
    public void QueryFailureAdapters_AreKeptEvenWhenSameModelPhysicalAdapterIsListed()
    {
        // 查询失败不等于该入口是虚拟入口，不能据此删除，否则同型号的第二块真显卡会被误删。
        var devices = Resolve(
        [
            RealPci(Amd, 0, AmdName, "6:0:0"),
            UnknownPci(Amd, 1, AmdName, "000000000000FC99"),
            UnknownPci(Amd, 2, AmdName, "000000000001B959")
        ]);

        Assert.Equal(3, devices.Count);
        Assert.Equal($"{AmdName}（设备 1）", devices[0].Name);
        Assert.Equal($"{AmdName}（设备 2）", devices[1].Name);
        Assert.Equal($"{AmdName}（设备 3）", devices[2].Name);
    }

    [Fact]
    public void TwoPhysicalAdaptersOfSameModel_KeepOriginalIndexInSuffix()
    {
        var devices = Resolve(
        [
            RealPci(Amd, 0, AmdName, "6:0:0"),
            RealPci(Nvidia, 1, NvidiaName, "1:0:0"),
            RealPci(Amd, 2, AmdName, "7:0:0")
        ]);

        Assert.Equal(3, devices.Count);
        Assert.Equal($"{AmdName}（设备 1）", devices[0].Name);
        Assert.Equal(NvidiaName, devices[1].Name);
        Assert.Equal($"{AmdName}（设备 3）", devices[2].Name);
    }
}