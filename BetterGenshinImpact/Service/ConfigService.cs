using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Service.Interface;
using System;
using System.IO;
using System.Text.Json;
using System.Threading;

namespace BetterGenshinImpact.Service;

public class ConfigService : IConfigService
{
    private readonly object _locker = new(); // 只有UI线程会调用这个方法，lock好像意义不大，而且浪费了下面的读写锁hhh
    private readonly ReaderWriterLockSlim _rwLock = new();

    private readonly JsonSerializerOptions _options = new()
    {
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    /// <summary>
    /// 写入只有UI线程会调用
    /// 多线程只会读，放心用static，不会丢失数据
    /// </summary>
    public static AllConfig? Config { get; private set; }

    public AllConfig Get()
    {
        lock (_locker)
        {
            if (Config == null)
            {
                Config = Read();
                Config.OnAnyChangedAction = Save; // 略微影响性能
                Config.InitEvent();
            }

            return Config;
        }
    }

    public void Save()
    {
        if (Config != null)
        {
            Write(Config);
        }
    }

    public AllConfig Read()
    {
        _rwLock.EnterReadLock();
        try
        {
            var filePath = Global.Absolute(@"User/config.json");
            if (!File.Exists(filePath))
            {
                return new AllConfig();
            }

            var json = File.ReadAllText(filePath);
            var config = JsonSerializer.Deserialize<AllConfig>(json, _options);
            if (config == null)
            {
                return new AllConfig();
            }

            Config = config;
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
            var path = Global.Absolute("User");
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