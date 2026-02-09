using BetterGenshinImpact.Helpers;
﻿using System;
using System.Collections.Generic;

namespace BetterGenshinImpact.GameTask.AutoFishing.Model;

/// <summary>
/// 模仿Java实现的多属性枚举类
/// </summary>
[Obsolete]
public class FishType
{
    public static readonly FishType AizenMedaka = new("aizen medaka", "fruit paste bait", Lang.S["GameTask_10847_7ad1a9"]);
    public static readonly FishType Crystalfish = new("crystalfish", "fruit paste bait", Lang.S["GameTask_10846_e70a45"]);
    public static readonly FishType Dawncatcher = new("dawncatcher", "fruit paste bait", Lang.S["GameTask_10845_f6b557"]);
    public static readonly FishType GlazeMedaka = new("glaze medaka", "fruit paste bait", Lang.S["GameTask_10844_96867e"]);
    public static readonly FishType Medaka = new("medaka", "fruit paste bait", Lang.S["GameTask_10812_547c2c"]);
    public static readonly FishType SweetFlowerMedaka = new("sweet-flower medaka", "fruit paste bait", Lang.S["GameTask_10843_1705e6"]);
    public static readonly FishType AkaiMaou = new("akai maou", "redrot bait", Lang.S["GameTask_10842_459957"]);
    public static readonly FishType Betta = new("betta", "redrot bait", Lang.S["GameTask_10841_199a53"]);
    public static readonly FishType LungedStickleback = new("lunged stickleback", "redrot bait", Lang.S["GameTask_10840_ab62c7"]);
    public static readonly FishType Snowstrider = new("snowstrider", "redrot bait", Lang.S["GameTask_10839_92f964"]);
    public static readonly FishType VenomspineFish = new("venomspine fish", "redrot bait", Lang.S["GameTask_10838_ca272f"]);
    public static readonly FishType AbidingAngelfish = new("abiding angelfish", "false worm bait", Lang.S["GameTask_10837_2566a9"]);
    public static readonly FishType BrownShirakodai = new("brown shirakodai", "false worm bait", Lang.S["GameTask_10836_47bcc3"]);
    public static readonly FishType PurpleShirakodai = new("purple shirakodai", "false worm bait", Lang.S["GameTask_10835_c678e6"]);
    public static readonly FishType RaimeiAngelfish = new("raimei angelfish", "false worm bait", Lang.S["GameTask_10834_765aac"]);
    public static readonly FishType TeaColoredShirakodai = new("tea-colored shirakodai", "false worm bait", Lang.S["GameTask_10833_acd951"]);
    public static readonly FishType BitterPufferfish = new("bitter pufferfish", "fake fly bait", Lang.S["GameTask_10832_bca626"]);
    public static readonly FishType DivdaRay = new("divda ray", "fake fly bait", Lang.S["GameTask_10831_33ee3d"]);
    public static readonly FishType FormaloRay = new("formalo ray", "fake fly bait", Lang.S["GameTask_10830_c44d13"]);
    public static readonly FishType GoldenKoi = new("golden koi", "fake fly bait", Lang.S["GameTask_10829_758e38"]);
    public static readonly FishType Pufferfish = new("pufferfish", "fake fly bait", Lang.S["GameTask_10806_9b3a18"]);
    public static readonly FishType RustyKoi = new("rusty koi", "fake fly bait", Lang.S["GameTask_10828_4e2f42"]);
    public static readonly FishType HalcyonJadeAxeMarlin = new("halcyon jade axe marlin", "sugardew bait", Lang.S["GameTask_10827_f68780"]);
    public static readonly FishType LazuriteAxeMarlin = new("lazurite axe marlin", "sugardew bait", Lang.S["GameTask_10826_0ff4d2"]);
    public static readonly FishType PeachOfTheDeepWaves = new("peach of the deep waves", "sugardew bait", Lang.S["GameTask_10825_064d31"]);
    public static readonly FishType SandstormAngler = new("sandstorm angler", "sugardew bait", Lang.S["GameTask_10824_e6d960"]);
    public static readonly FishType StreamingAxeMarlin = new("streaming axe marlin", "sugardew bait", Lang.S["GameTask_10823_47c6ab"]);
    public static readonly FishType SunsetCloudAngler = new("sunset cloud angler", "sugardew bait", Lang.S["GameTask_10822_08b919"]);
    public static readonly FishType TrueFruitAngler = new("true fruit angler", "sugardew bait", Lang.S["GameTask_10821_e3af7d"]);
    public static readonly FishType BlazingHeartfeatherBass = new("blazing heartfeather bass", "sour bait", Lang.S["GameTask_10820_1e406b"]);
    public static readonly FishType RipplingHeartfeatherBass = new("rippling heartfeather bass", "sour bait", Lang.S["GameTask_10819_df835b"]);
    public static readonly FishType MaintenanceMekInitialConfiguration = new("maintenance mek- initial configuration", "flashing maintenance mek bait", Lang.S["GameTask_10818_fa1d64"]);
    public static readonly FishType MaintenanceMekPlatinumCollection = new("maintenance mek- platinum collection", "flashing maintenance mek bait", Lang.S["GameTask_10817_d19149"]);
    public static readonly FishType MaintenanceMekSituationController = new("maintenance mek- situation controller", "flashing maintenance mek bait", Lang.S["GameTask_10816_ad32db"]);
    public static readonly FishType MaintenanceMekWaterBodyCleaner = new("maintenance mek- water body cleaner", "flashing maintenance mek bait", Lang.S["GameTask_10815_b2768c"]);
    public static readonly FishType MaintenanceMekWaterGoldLeader = new("maintenance mek- gold leader", "flashing maintenance mek bait", Lang.S["GameTask_10814_d7711a"]);


    public static IEnumerable<FishType> Values
    {
        get
        {
            yield return AizenMedaka;
            yield return Crystalfish;
            yield return Dawncatcher;
            yield return GlazeMedaka;
            yield return Medaka;
            yield return SweetFlowerMedaka;
            yield return AkaiMaou;
            yield return Betta;
            yield return LungedStickleback;
            yield return Snowstrider;
            yield return VenomspineFish;
            yield return AbidingAngelfish;
            yield return BrownShirakodai;
            yield return PurpleShirakodai;
            yield return RaimeiAngelfish;
            yield return TeaColoredShirakodai;
            yield return BitterPufferfish;
            yield return DivdaRay;
            yield return FormaloRay;
            yield return GoldenKoi;
            yield return Pufferfish;
            yield return RustyKoi;
            yield return HalcyonJadeAxeMarlin;
            yield return LazuriteAxeMarlin;
            yield return PeachOfTheDeepWaves;
            yield return SandstormAngler;
            yield return StreamingAxeMarlin;
            yield return SunsetCloudAngler;
            yield return TrueFruitAngler;
            yield return BlazingHeartfeatherBass;
            yield return RipplingHeartfeatherBass;
            yield return MaintenanceMekInitialConfiguration;
            yield return MaintenanceMekPlatinumCollection;
            yield return MaintenanceMekSituationController;
            yield return MaintenanceMekWaterBodyCleaner;
            yield return MaintenanceMekWaterGoldLeader;
        }
    }

    public string Name { get; private set; }
    public string BaitName { get; private set; }
    public string ChineseName { get; private set; }

    private FishType(string name, string baitName, string chineseName)
    {
        Name = name;
        BaitName = baitName;
        ChineseName = chineseName;
    }

    public static FishType FromName(string name)
    {
        foreach (var fishType in Values)
        {
            if (fishType.Name == name)
            {
                return fishType;
            }
        }

        throw new KeyNotFoundException($"FishType {name} not found");
    }
}