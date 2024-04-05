using System.Text.RegularExpressions;

namespace BetterGenshinImpact.Helpers;

public class StringUtils
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

        return str.Replace(" ", "").Replace("\t", "");
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

        return str.Replace("\n", "").Replace("\r", "");
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

        return System.Text.RegularExpressions.Regex.IsMatch(str, @"[\u4e00-\u9fa5]");
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
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        try
        {
            return double.Parse(text);
        }
        catch
        {
            return 0;
        }
    }

    public static int TryParseInt(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        try
        {
            return int.Parse(text);
        }
        catch
        {
            return 0;
        }
    }

    public static int TryExtractPositiveInt(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return -1;
        }

        try
        {
            text = Regex.Replace(text, @"[^0-9]+", "");
            return int.Parse(text);
        }
        catch
        {
            return -1;
        }
    }
}
