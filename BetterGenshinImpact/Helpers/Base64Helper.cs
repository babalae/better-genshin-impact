using System;

namespace BetterGenshinImpact.Helpers;

public static class Base64Helper
{
    public static string DecodeToString(string base64String)
    {
        byte[] bytes = Convert.FromBase64String(base64String);
        return System.Text.Encoding.UTF8.GetString(bytes);
    }

    public static byte[] DecodeToBytes(string base64String)
    {
        return Convert.FromBase64String(base64String); 
    }
}