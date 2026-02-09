using BetterGenshinImpact.Helpers;
﻿using System;
using System.ComponentModel;

namespace BetterGenshinImpact.GameTask.Common.Map.Maps.Base;

public enum MapTypes
{
    [Description(Lang.S["GameTask_11673_32269b"])]
    Teyvat,

    [Description(Lang.S["GameTask_11672_94e546"])]
    TheChasm,

    [Description(Lang.S["GameTask_11397_9e13be"])]
    Enkanomiya,

    [Description(Lang.S["GameTask_11671_9778f1"])]
    SeaOfBygoneEras,

    [Description(Lang.S["GameTask_11670_c37935"])]
    AncientSacredMountain
}
public static class MapTypesExtensions
{
    public static MapTypes ParseFromDescription(string description)
    {
        foreach (var field in typeof(MapTypes).GetFields())
        {
            if (Attribute.GetCustomAttribute(field, typeof(DescriptionAttribute)) is DescriptionAttribute attribute)
            {
                if (attribute.Description == description)
                {
                    return (MapTypes)field.GetValue(null)!;
                }
            }
        }
        throw new ArgumentException($"{Lang.S["GameTask_11675_626db4"]}, nameof(description));
    }
    
    public static MapTypes ParseFromName(string name)
    {
        if (Enum.TryParse<MapTypes>(name, true, out var result))
        {
            return result;
        }
        throw new ArgumentException($"{Lang.S["GameTask_11674_8fef31"]}, nameof(name));
    }
}