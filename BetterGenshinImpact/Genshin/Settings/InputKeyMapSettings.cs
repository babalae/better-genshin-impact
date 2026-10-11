using System;
using System.Collections.Generic;
using System.Globalization;

namespace BetterGenshinImpact.Genshin.Settings;

/// <summary>
/// 原神新版键鼠映射配置。
/// </summary>
public sealed class InputKeyMapSettings
{
    public static InputKeyMapSettings Empty { get; } = new([]);

    public IReadOnlyList<InputKeyMapProfile> Profiles { get; }

    private InputKeyMapSettings(IReadOnlyList<InputKeyMapProfile> profiles)
    {
        Profiles = profiles;
    }

    public static InputKeyMapSettings Parse(IReadOnlyList<int>? profileKeys, IReadOnlyList<string>? profileValues)
    {
        if (profileKeys is null || profileValues is null)
        {
            return Empty;
        }

        int count = Math.Min(profileKeys.Count, profileValues.Count);
        if (count == 0)
        {
            return Empty;
        }

        List<InputKeyMapProfile> profiles = new(count);
        for (int i = 0; i < count; i++)
        {
            profiles.Add(InputKeyMapProfile.Parse(profileKeys[i], profileValues[i]));
        }

        return new InputKeyMapSettings(profiles);
    }
}

/// <summary>
/// 单个键鼠映射预设。当前已知格式示例：1.0|100:70001,211 70003,476。
/// </summary>
public sealed class InputKeyMapProfile
{
    public int ProfileId { get; }
    public string RawValue { get; }
    public string? FormatVersion { get; }
    public int? InputGroupId { get; }
    public IReadOnlyList<InputKeyBinding> Bindings { get; }
    public bool IsValid { get; }

    private InputKeyMapProfile(
        int profileId,
        string rawValue,
        string? formatVersion,
        int? inputGroupId,
        IReadOnlyList<InputKeyBinding> bindings,
        bool isValid)
    {
        ProfileId = profileId;
        RawValue = rawValue;
        FormatVersion = formatVersion;
        InputGroupId = inputGroupId;
        Bindings = bindings;
        IsValid = isValid;
    }

    internal static InputKeyMapProfile Parse(int profileId, string? rawValue)
    {
        string raw = rawValue ?? string.Empty;
        int versionSeparatorIndex = raw.IndexOf('|');
        int groupSeparatorIndex = raw.IndexOf(':', versionSeparatorIndex + 1);
        if (versionSeparatorIndex <= 0 || groupSeparatorIndex <= versionSeparatorIndex + 1)
        {
            return Invalid(profileId, raw);
        }

        string formatVersion = raw[..versionSeparatorIndex];
        if (!int.TryParse(
                raw.AsSpan(versionSeparatorIndex + 1, groupSeparatorIndex - versionSeparatorIndex - 1),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int inputGroupId))
        {
            return Invalid(profileId, raw);
        }

        string bindingsRaw = raw[(groupSeparatorIndex + 1)..];
        string[] bindingItems = bindingsRaw.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        List<InputKeyBinding> bindings = new(bindingItems.Length);
        foreach (string bindingItem in bindingItems)
        {
            int valueSeparatorIndex = bindingItem.IndexOf(',');
            if (valueSeparatorIndex <= 0
                || valueSeparatorIndex == bindingItem.Length - 1
                || !int.TryParse(bindingItem.AsSpan(0, valueSeparatorIndex), NumberStyles.Integer, CultureInfo.InvariantCulture, out int bindingId)
                || !int.TryParse(bindingItem.AsSpan(valueSeparatorIndex + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int inputCode))
            {
                return Invalid(profileId, raw);
            }

            bindings.Add(new InputKeyBinding(bindingId, inputCode));
        }

        return new InputKeyMapProfile(profileId, raw, formatVersion, inputGroupId, bindings, true);
    }

    private static InputKeyMapProfile Invalid(int profileId, string rawValue)
    {
        return new InputKeyMapProfile(profileId, rawValue, null, null, [], false);
    }
}

public readonly record struct InputKeyBinding(int BindingId, int InputCode)
{
    public TpsInputBindingId? TpsBindingId => Enum.IsDefined((TpsInputBindingId)BindingId)
        ? (TpsInputBindingId)BindingId
        : null;

    public GenshinInputCode? KnownInputCode => Enum.IsDefined((GenshinInputCode)InputCode)
        ? (GenshinInputCode)InputCode
        : null;
}

/// <summary>
/// 至冬第三人称射击玩法中已确认的键位绑定项。
/// </summary>
public enum TpsInputBindingId
{
    Fire = 70001,
    ToggleAimPrimary = 70003,
    ToggleAimSecondary = 70004,
    Reload = 70005,
    JumpOrVault = 70007,
    SprintOrSlidePrimary = 70008,
    SprintOrSlideSecondary = 70009,
    SwitchWeaponSet1 = 70010,
    SwitchWeaponSet2 = 70012,
    SwitchGrenade = 70013,
    WeaponSkill = 70014,
}

/// <summary>
/// 新版键鼠配置中已确认的输入码。
/// </summary>
public enum GenshinInputCode
{
    R = 138,
    MouseRightButton = 139,
    D1 = 150,
    MouseLeftButton = 211,
    Space = 226,
    LeftShift = 240,
    D2 = 249,
    D3 = 250,
    E = 251,
    MouseWheelUp = 476,
    MouseWheelDown = 477,
}

/// <summary>
/// 至冬第三人称射击操作预设。
/// </summary>
public enum TpsAimControlType
{
    PresetTwo = 0,
    PresetOne = 1,
}
