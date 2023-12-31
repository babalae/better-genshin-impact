using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static System.Net.Mime.MediaTypeNames;

namespace BetterGenshinImpact.Helpers
{
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
            return str.Replace(" ", "");
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
    }
}
