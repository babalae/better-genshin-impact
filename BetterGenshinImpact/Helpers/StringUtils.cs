using System.Text;
using System.Text.RegularExpressions;

namespace BetterGenshinImpact.Helpers;

public partial class StringUtils
{
    /// <summary>
    ///  删除所有空字符串
    /// </summary>
    /// <param name="str"></param>
    /// <returns></returns>
    public static string RemoveAllSpace(string str)
    {
        if (string.IsNullOrEmpty(str))
        {
            return str;
        }

        return new StringBuilder(str).Replace(" ", "").Replace("\t", "").ToString();
    }

    /// <summary>
    ///  删除所有换行符
    /// </summary>
    /// <param name="str"></param>
    /// <returns></returns>
    public static string RemoveAllEnter(string str)
    {
        if (string.IsNullOrEmpty(str))
        {
            return str;
        }

        return new StringBuilder(str).Replace("\n", "").Replace("\r", "").ToString();
    }

    /// <summary>
    /// 判断字符串是否是中文
    /// </summary>
    public static bool IsChinese(string str)
    {
        if (string.IsNullOrEmpty(str))
        {
            return false;
        }

        return ChineseRegex().IsMatch(str);
    }

    /// <summary>
    /// 保留中文字符
    /// </summary>
    /// <param name="str"></param>
    /// <returns></returns>
    public static string ExtractChinese(string str)
    {
        //声明存储结果的字符串
        string chineseString = "";

        //将传入参数中的中文字符添加到结果字符串中
        for (int i = 0; i < str.Length; i++)
        {
            if (str[i] >= 0x4E00 && str[i] <= 0x9FA5) //汉字
            {
                chineseString += str[i];
            }
        }

        //返回保留中文的处理结果
        return chineseString;
    }

    public static double TryParseDouble(string text)
    {
        _ = double.TryParse(text, out double result);
        return result;
    }

    public static int TryParseInt(string text)
    {
        _ = int.TryParse(text, out int result);
        return result;
    }
    
    public static int TryParseInt(string text, int defaultValue)
    {
        return int.TryParse(text, out int result) ? result : defaultValue;
    }

    public static int TryExtractPositiveInt(string text, int defaultValue = -1)
    {
        if (string.IsNullOrEmpty(text))
        {
            return defaultValue;
        }

        try
        {
            text = RegexHelper.ExcludeNumberRegex().Replace(text, "");
            return TryParseInt(text, defaultValue);
        }
        catch
        {
            return defaultValue;
        }
    }

    [GeneratedRegex(@"[\u4e00-\u9fa5]")]
    private static partial Regex ChineseRegex();
}
