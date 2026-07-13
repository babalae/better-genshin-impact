using System.Security.Cryptography;
using System.Text;

namespace BetterGenshinImpact.Helpers.Security;

public static class MD5Helper
{
    /// <summary>
    /// 计算字符串的MD5哈希值
    /// </summary>
    /// <param name="input">输入字符串</param>
    /// <param name="encoding">字符编码，默认为UTF-8</param>
    /// <returns>32位小写MD5哈希值</returns>
    public static string ComputeMD5(string input, Encoding encoding = null)
    {
        if (string.IsNullOrEmpty(input))
            return string.Empty;

        encoding = encoding ?? Encoding.UTF8;
        
        using (var md5 = MD5.Create())
        {
            byte[] inputBytes = encoding.GetBytes(input);
            byte[] hashBytes = md5.ComputeHash(inputBytes);
            
            // 将字节数组转换为十六进制字符串
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < hashBytes.Length; i++)
            {
                sb.Append(hashBytes[i].ToString("x2"));
            }
            return sb.ToString();
        }
    }
}