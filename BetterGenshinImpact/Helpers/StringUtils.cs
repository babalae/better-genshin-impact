using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
    }
}
