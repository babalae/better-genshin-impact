using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text;

namespace MicaSetup.Helper;

public static class ProcessExtension
{
    public static FluentProcess FileName(this FluentProcess self, string filename)
    {
        self.StartInfo.FileName = filename;
        return self;
    }

    public static FluentProcess Arguments(this FluentProcess self, string args)
    {
        self.StartInfo.Arguments = args;
        return self;
    }

    public static FluentProcess CreateNoWindow(this FluentProcess self, bool enabled = true)
    {
        self.StartInfo.CreateNoWindow = enabled;
        return self;
    }

    public static FluentProcess Environment(this FluentProcess self, params KeyValuePair<string, string>[] vars)
    {
        foreach (var v in vars)
        {
            self.StartInfo.Environment.Add(v!);
        }
        return self;
    }

    public static Process EnvironmentVariables(this FluentProcess self, params KeyValuePair<string, string>[] vars)
    {
        foreach (var v in vars)
        {
            self.StartInfo.EnvironmentVariables.Add(v.Key, v.Value);
        }
        return self;
    }

    public static FluentProcess RedirectStandardError(this FluentProcess self, bool enabled = true)
    {
        self.StartInfo.RedirectStandardError = enabled;
        return self;
    }

    public static FluentProcess RedirectStandardInput(this FluentProcess self, bool enabled = true)
    {
        self.StartInfo.RedirectStandardInput = enabled;
        return self;
    }

    public static FluentProcess RedirectStandardOutput(this FluentProcess self, bool enabled = true)
    {
        self.StartInfo.RedirectStandardOutput = enabled;
        return self;
    }

    public static FluentProcess StandardErrorEncoding(this FluentProcess self, Encoding encoding = null!)
    {
        self.StartInfo.StandardErrorEncoding = encoding ?? Encoding.Default;
        return self;
    }

    public static FluentProcess StandardOutputEncoding(this FluentProcess self, Encoding encoding = null!)
    {
        self.StartInfo.StandardOutputEncoding = encoding ?? Encoding.Default;
        return self;
    }

    public static FluentProcess BeginOutputRead(this FluentProcess self, Stream stream)
    {
        long pos = stream.Position;
        self.StandardOutput.BaseStream.CopyTo(stream);
        stream.Position = pos;
        return self;
    }

    public static FluentProcess BeginErrorRead(this FluentProcess self, Stream stream)
    {
        long pos = stream.Position;
        self.StandardOutput.BaseStream.CopyTo(stream);
        stream.Position = pos;
        return self;
    }

    public static FluentProcess UseShellExecute(this FluentProcess self, bool enabled = true)
    {
        self.StartInfo.UseShellExecute = enabled;
        return self;
    }

    public static FluentProcess Verb(this FluentProcess self, string verb)
    {
        self.StartInfo.Verb = verb;
        return self;
    }

    public static FluentProcess WorkingDirectory(this FluentProcess self, string directory)
    {
        self.StartInfo.WorkingDirectory = directory;
        return self;
    }

    public static FluentProcess SetOutputDataReceived(this FluentProcess self, DataReceivedEventHandler handler)
    {
        self.OutputDataReceived += handler;
        return self;
    }

    public static FluentProcess SetErrorDataReceived(this FluentProcess self, DataReceivedEventHandler handler)
    {
        self.ErrorDataReceived += handler;
        return self;
    }

    [SuppressMessage("Style", "IDE0060:")]
    public static void Forget(this FluentProcess self)
    {
    }
}

public class FluentProcess : Process
{
    public static FluentProcess Create()
    {
        return new FluentProcess();
    }

    public new static FluentProcess Start(string fileName)
    {
        return Create()
            .FileName(fileName)
            .Start();
    }

    public new static FluentProcess Start(string fileName, string args)
    {
        return new FluentProcess()
            .FileName(fileName)
            .Arguments(args)
            .Start();
    }

    public new FluentProcess BeginErrorReadLine()
    {
        base.BeginErrorReadLine();
        return this;
    }

    public new FluentProcess BeginOutputReadLine()
    {
        base.BeginOutputReadLine();
        return this;
    }

    public new FluentProcess WaitForExit()
    {
        base.WaitForExit();
        return this;
    }

    public new FluentProcess WaitForInputIdle()
    {
        base.WaitForInputIdle();
        return this;
    }

    public new FluentProcess Start()
    {
        Logger.Info(
        $"""
            "{StartInfo.FileName}" {StartInfo.Arguments}
        """);
        base.Start();
        return this;
    }
}
