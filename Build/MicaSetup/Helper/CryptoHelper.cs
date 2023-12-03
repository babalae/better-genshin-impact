using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace MicaSetup.Helper;

public static class SHA1CryptoHelper
{
    public static string ComputeHash(string str, Encoding? encoding = null!)
    {
        using HashAlgorithm hashAlgorithm = SHA1.Create();
        return hashAlgorithm.ToString(str, encoding);
    }

    public static string ComputeHash(byte[] buffer)
    {
        using HashAlgorithm hashAlgorithm = SHA1.Create();
        return hashAlgorithm.ToString(buffer);
    }

    public static string ComputeHash(Stream inputStream)
    {
        using HashAlgorithm hashAlgorithm = SHA1.Create();
        return hashAlgorithm.ToString(inputStream);
    }
}

public static class MD5CryptoHelper
{
    public static string ComputeHash(string str, Encoding? encoding = null!)
    {
        using HashAlgorithm hashAlgorithm = new MD5CryptoServiceProvider();
        return hashAlgorithm.ToString(str, encoding);
    }

    public static string ComputeHash(byte[] buffer)
    {
        using HashAlgorithm hashAlgorithm = new MD5CryptoServiceProvider();
        return hashAlgorithm.ToString(buffer);
    }

    public static string ComputeHash(Stream inputStream)
    {
        using HashAlgorithm hashAlgorithm = new MD5CryptoServiceProvider();
        return hashAlgorithm.ToString(inputStream);
    }
}

file static class HashAlgorithmExtension
{
    public static string ToString(this HashAlgorithm self, byte[] buffer)
    {
        byte[] output = self.ComputeHash(buffer);

        return output.ToHexString();
    }

    public static string ToString(this HashAlgorithm self, Stream inputStream)
    {
        byte[] output = self.ComputeHash(inputStream);
        return output.ToHexString();
    }

    public static string ToString(this HashAlgorithm self, string str, Encoding? encoding = null!)
    {
        return self.ToString((encoding ?? Encoding.UTF8).GetBytes(str));
    }

    public static string ToHexString(this byte[] self)
    {
        if (self == null)
        {
            return null!;
        }
        if (self.Length == 0)
        {
            return string.Empty;
        }

        static char GetHexValue(int i) => i < 10 ? (char)(i + 48) : (char)(i - 10 + 65);
        int length = self.Length * 2;
        char[] array = new char[length];

        for (int srcIndex = 0, tarIndex = 0; srcIndex < length; srcIndex += 2)
        {
            byte b = self[tarIndex++];
            array[srcIndex] = GetHexValue(b / 16);
            array[srcIndex + 1] = GetHexValue(b % 16);
        }
        return new string(array, 0, array.Length);
    }
}
