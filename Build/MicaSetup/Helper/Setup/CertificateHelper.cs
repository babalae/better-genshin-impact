using System.Security.Cryptography.X509Certificates;

namespace MicaSetup.Helper;

public static class CertificateHelper
{
    public static void Install(byte[] cer, string password = null!, StoreName storeName = StoreName.Root, StoreLocation storeLocation = StoreLocation.LocalMachine)
    {
        using X509Certificate2 cert = password == null ? new(cer) : new(cer, password);
        using X509Store store = new(storeName, storeLocation);
        store.Open(OpenFlags.ReadWrite);
        store.Add(cert);
        store.Close();
    }

    public static void Install(string cerFilePath, string password = null!, StoreName storeName = StoreName.Root, StoreLocation storeLocation = StoreLocation.LocalMachine)
    {
        using X509Certificate2 cert = password == null ? new(cerFilePath) : new(cerFilePath, password);
        using X509Store store = new(storeName, storeLocation);
        store.Open(OpenFlags.ReadWrite);
        store.Add(cert);
        store.Close();
    }

    public static void Uninstall(byte[] cer, string password = null!, StoreName storeName = StoreName.Root, StoreLocation storeLocation = StoreLocation.LocalMachine)
    {
        using X509Certificate2 cert = password == null ? new(cer) : new(cer, password);
        using X509Store store = new(storeName, storeLocation);
        store.Open(OpenFlags.ReadWrite);
        store.Remove(cert);
        store.Close();
    }

    public static void Uninstall(string cerFilePath, string password = null!, StoreName storeName = StoreName.Root, StoreLocation storeLocation = StoreLocation.LocalMachine)
    {
        using X509Certificate2 cert = password == null ? new(cerFilePath) : new(cerFilePath, password);
        using X509Store store = new(storeName, storeLocation);
        store.Open(OpenFlags.ReadWrite);
        store.Remove(cert);
        store.Close();
    }
}
