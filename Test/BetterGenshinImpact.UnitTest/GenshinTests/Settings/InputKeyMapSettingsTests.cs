using BetterGenshinImpact.Genshin.Settings;
using BetterGenshinImpact.Genshin.Settings2;

namespace BetterGenshinImpact.UnitTest.GenshinTests.Settings;

public class InputKeyMapSettingsTests
{
    private const string TpsKeyMap =
        "1.0|100:70001,211 70003,476 70004,477 70005,138 70007,226 70008,240 70009,139 70010,150 70012,249 70013,250 70014,251";

    [Fact]
    public void Parse_ShouldReadTpsKeyMouseSettings()
    {
        InputKeyMapSettings settings = InputKeyMapSettings.Parse([70001], [TpsKeyMap]);

        InputKeyMapProfile profile = Assert.Single(settings.Profiles);
        Assert.True(profile.IsValid);
        Assert.Equal(70001, profile.ProfileId);
        Assert.Equal("1.0", profile.FormatVersion);
        Assert.Equal(100, profile.InputGroupId);
        Assert.Equal(11, profile.Bindings.Count);

        InputKeyBinding fire = profile.Bindings[0];
        Assert.Equal(TpsInputBindingId.Fire, fire.TpsBindingId);
        Assert.Equal(GenshinInputCode.MouseLeftButton, fire.KnownInputCode);

        InputKeyBinding secondaryAim = profile.Bindings[2];
        Assert.Equal(TpsInputBindingId.ToggleAimSecondary, secondaryAim.TpsBindingId);
        Assert.Equal(GenshinInputCode.MouseWheelDown, secondaryAim.KnownInputCode);
    }

    [Fact]
    public void GameSettingsParse_ShouldReadInputKeyMapAndTpsPreset()
    {
        const string json = """
                            {
                              "overrideInputKeyMapKeyList": [70001],
                              "overrideInputKeyMapValueList": ["1.0|100:70001,211 70003,476"],
                              "pcTpsAimControlType": 0
                            }
                            """;

        GenshinGameSettings? settings = GenshinGameSettings.Parse(json);

        Assert.NotNull(settings);
        Assert.Equal(TpsAimControlType.PresetTwo, settings.PcTpsAimControlType);
        Assert.Equal(2, Assert.Single(settings.InputKeyMap.Profiles).Bindings.Count);
    }

    [Fact]
    public void GameSettingsParse_ShouldKeepMissingTpsPresetUnknown()
    {
        GenshinGameSettings? settings = GenshinGameSettings.Parse("{}");

        Assert.NotNull(settings);
        Assert.Null(settings.PcTpsAimControlType);
        Assert.Empty(settings.InputKeyMap.Profiles);
    }

    [Fact]
    public void Parse_ShouldPreserveInvalidRawValue()
    {
        InputKeyMapSettings settings = InputKeyMapSettings.Parse([70001], ["invalid"]);

        InputKeyMapProfile profile = Assert.Single(settings.Profiles);
        Assert.False(profile.IsValid);
        Assert.Equal("invalid", profile.RawValue);
        Assert.Empty(profile.Bindings);
    }
}
