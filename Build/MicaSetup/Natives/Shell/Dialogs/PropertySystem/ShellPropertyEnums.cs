using System;

namespace MicaSetup.Shell.Dialogs;

public enum PropEnumType
{
    DiscreteValue = 0,
    RangedValue = 1,
    DefaultValue = 2,
    EndRange = 3,
};

public enum PropertyAggregationType
{
    Default = 0,
    First = 1,
    Sum = 2,
    Average = 3,
    DateRange = 4,
    Union = 5,
    Max = 6,
    Min = 7,
}

[Flags]
public enum PropertyColumnStateOptions
{
    None = 0x00000000,
    StringType = 0x00000001,
    IntegerType = 0x00000002,
    DateType = 0x00000003,
    TypeMask = 0x0000000f,
    OnByDefault = 0x00000010,
    Slow = 0x00000020,
    Extended = 0x00000040,
    SecondaryUI = 0x00000080,
    Hidden = 0x00000100,
    PreferVariantCompare = 0x00000200,
    PreferFormatForDisplay = 0x00000400,
    NoSortByFolders = 0x00000800,
    ViewOnly = 0x00010000,
    BatchRead = 0x00020000,
    NoGroupBy = 0x00040000,
    FixedWidth = 0x00001000,
    NoDpiScale = 0x00002000,
    FixedRatio = 0x00004000,
    DisplayMask = 0x0000F000,
}

public enum PropertyConditionOperation
{
    Implicit,
    Equal,
    NotEqual,
    LessThan,
    GreaterThan,
    LessThanOrEqual,
    GreaterThanOrEqual,
    ValueStartsWith,
    ValueEndsWith,
    ValueContains,
    ValueNotContains,
    DOSWildCards,
    WordEqual,
    WordStartsWith,
    ApplicationSpecific,
}

public enum PropertyConditionType
{
    None = 0,
    String = 1,
    Size = 2,
    DateTime = 3,
    Boolean = 4,
    Number = 5,
}

[Flags]
public enum PropertyDescriptionFormatOptions
{
    None = 0,
    PrefixName = 0x1,
    FileName = 0x2,
    AlwaysKB = 0x4,
    RightToLeft = 0x8,
    ShortTime = 0x10,
    LongTime = 0x20,
    HideTime = 64,
    ShortDate = 0x80,
    LongDate = 0x100,
    HideDate = 0x200,
    RelativeDate = 0x400,
    UseEditInvitation = 0x800,
    ReadOnly = 0x1000,
    NoAutoReadingOrder = 0x2000,
    SmartDateTime = 0x4000,
}

public enum PropertyDisplayType
{
    String = 0,
    Number = 1,
    Boolean = 2,
    DateTime = 3,
    Enumerated = 4,
}

public enum PropertyGroupingRange
{
    Discrete = 0,
    Alphanumeric = 1,
    Size = 2,
    Dynamic = 3,
    Date = 4,
    Percent = 5,
    Enumerated = 6,
}

public enum PropertySortDescription
{
    General,
    AToZ,
    LowestToHighest,
    SmallestToBiggest,
    OldestToNewest,
}

public enum PropertyStoreCacheState
{
    Normal = 0,
    NotInSource = 1,
    Dirty = 2
}

[Flags]
public enum PropertyTypeOptions
{
    None = 0x00000000,
    MultipleValues = 0x00000001,
    IsInnate = 0x00000002,
    IsGroup = 0x00000004,
    CanGroupBy = 0x00000008,
    CanStackBy = 0x00000010,
    IsTreeProperty = 0x00000020,
    IncludeInFullTextQuery = 0x00000040,
    IsViewable = 0x00000080,
    IsQueryable = 0x00000100,
    CanBePurged = 0x00000200,
    IsSystemProperty = unchecked((int)0x80000000),
    MaskAll = unchecked((int)0x800001FF),
}

[Flags]
public enum PropertyViewOptions
{
    None = 0x00000000,
    CenterAlign = 0x00000001,
    RightAlign = 0x00000002,
    BeginNewGroup = 0x00000004,
    FillArea = 0x00000008,
    SortDescending = 0x00000010,
    ShowOnlyIfPresent = 0x00000020,
    ShowByDefault = 0x00000040,
    ShowInPrimaryList = 0x00000080,
    ShowInSecondaryList = 0x00000100,
    HideLabel = 0x00000200,
    Hidden = 0x00000800,
    CanWrap = 0x00001000,
    MaskAll = 0x000003ff,
}
