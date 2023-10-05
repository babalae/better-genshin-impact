using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Service.Interface;
using System;
using System.IO;
using System.Text.Json;
using System.Threading;

namespace BetterGenshinImpact.Service;

public class ConfigService : IConfigService
{
    private readonly ReaderWriterLockSlim _rwLock = new();
    private readonly JsonSerializerOptions _options = new()
    {
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals,
        WriteIndented = true
    };

    /// <summary>
    /// 写入只有UI线程会调用
    /// 多线程只会读，放心用static，不会丢失数据
    /// </summary>
    public static AllConfig? DefaultConfig { get; private set; }

    public AllConfig Get()
    {
        if (DefaultConfig == null)
        {
            DefaultConfig =  Read();
        }

        return DefaultConfig;
    }

    public void Save()
    {
        if (DefaultConfig != null)
        {
            Write(DefaultConfig);
        }
    }

    public AllConfig Read()
    {
        _rwLock.EnterReadLock();
        try
        {
            var filePath = Global.Absolute(@"Config/config.json");
            if (!File.Exists(filePath))
            {
                return new AllConfig();
            }

            var json = File.ReadAllText(filePath);
            var config = JsonSerializer.Deserialize<AllConfig>(json);
            if (config == null)
            {
                return new AllConfig();
            }

            DefaultConfig = config;
            return config;
        }
        finally
        {
            _rwLock.ExitReadLock();
        }
    }

    public void Write(AllConfig config)
    {
        _rwLock.EnterWriteLock();
        try
        {
            var path = Global.Absolute("Config");
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }

            var file = Path.Combine(path, "config.json");
            File.WriteAllText(file, JsonSerializer.Serialize(config, _options));
        }
        catch (Exception e)
        {
            Console.WriteLine(e.Message);
            Console.WriteLine(e.StackTrace);
        }
        finally
        {
            _rwLock.ExitWriteLock();
        }
    }
}