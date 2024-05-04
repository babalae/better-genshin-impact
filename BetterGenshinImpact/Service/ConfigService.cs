using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Service.Interface;
using OpenCvSharp;
using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;

namespace BetterGenshinImpact.Service;

public class ConfigService : IConfigService
{
    private readonly object _locker = new(); // 只有UI线程会调用这个方法，lock好像意义不大，而且浪费了下面的读写锁hhh
    private readonly ReaderWriterLockSlim _rwLock = new();

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters =
        {
            new OpenCvPointJsonConverter(),
            new OpenCvRectJsonConverter(),
        },
        WriteIndented = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
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
            var config = JsonSerializer.Deserialize<AllConfig>(json, JsonOptions);
            if (config == null)
            {
                return new AllConfig();
            }

            Config = config;
            return config;
        }
        catch (Exception e)
        {
            Console.WriteLine(e.Message);
            Console.WriteLine(e.StackTrace);
            return new AllConfig();
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
            File.WriteAllText(file, JsonSerializer.Serialize(config, JsonOptions));
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

public class OpenCvRectJsonConverter : JsonConverter<Rect>
{
    public override unsafe Rect Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        RectHelper helper = JsonSerializer.Deserialize<RectHelper>(ref reader, options);
        return *(Rect*)&helper;
    }

    public override unsafe void Write(Utf8JsonWriter writer, Rect value, JsonSerializerOptions options)
    {
        RectHelper helper = *(RectHelper*)&value;
        JsonSerializer.Serialize(writer, helper, options);
    }

    // DO NOT MODIFY: Keep the layout same as OpenCvSharp.Rect
    private struct RectHelper
    {
        public int X { get; set; }
        public int Y { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
    }
}

public class OpenCvPointJsonConverter : JsonConverter<Point>
{
    public override unsafe Point Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        PointHelper helper = JsonSerializer.Deserialize<PointHelper>(ref reader, options);
        return *(Point*)&helper;
    }

    public override unsafe void Write(Utf8JsonWriter writer, Point value, JsonSerializerOptions options)
    {
        PointHelper helper = *(PointHelper*)&value;
        JsonSerializer.Serialize(writer, helper, options);
    }

    // DO NOT MODIFY: Keep the layout same as OpenCvSharp.Point
    private struct PointHelper
    {
        public int X { get; set; }
        public int Y { get; set; }
    }
}
