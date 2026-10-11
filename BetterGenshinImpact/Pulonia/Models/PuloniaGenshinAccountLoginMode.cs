namespace BetterGenshinImpact.Pulonia.Models;

/// <summary>PC 官服账号的登录选择方式。</summary>
public enum PuloniaGenshinAccountLoginMode
{
    /// <summary>依次尝试图片、列表文字及可选登录凭据。</summary>
    Automatic,
    /// <summary>匹配账号标识图片。</summary>
    Image,
    /// <summary>通过 OCR 唯一匹配列表文字。</summary>
    ListText,
    /// <summary>通过 Windows 凭据输入账号和密码。</summary>
    Password
}
