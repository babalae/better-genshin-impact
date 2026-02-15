using System;

namespace BetterGenshinImpact.Service;

public static class MissingTranslationCollectionSettings
{
    public static readonly bool Enabled = true;
    public static readonly string SupabaseUrl = "https://obwddvnwzaolbdawduxg.supabase.co";
    public static readonly string SupabaseApiKey = "sb_publishable_PyvQSxxCi02aawC-G6vtgg_wzOctlgm";
    public static readonly string Table = "translation_missing";
    public static readonly int BatchSize = 200;
    public static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(5);

    public static bool IsValid =>
        Enabled
        && !string.IsNullOrWhiteSpace(SupabaseUrl)
        && !string.IsNullOrWhiteSpace(SupabaseApiKey)
        && !string.IsNullOrWhiteSpace(Table)
        && BatchSize > 0
        && FlushInterval > TimeSpan.Zero;
}

