using System;
using System.IO;
using System.Threading;

namespace Updater;

internal sealed class Program
{
    private static void Main(string[] args)
    {
        if (args.Length < 2)
        {
            // Avoid user interaction
            Environment.ExitCode = 1;
            return;
        }

        // Chattering ms
        Thread.Sleep(200);

        string sourcePath = args[0];
        string targetPath = args[1];

        if (!Directory.Exists(sourcePath))
        {
            Environment.ExitCode = 1;
            return;
        }

        if (!Directory.Exists(targetPath))
        {
            Directory.CreateDirectory(targetPath);
        }

        // No fallback measures
        foreach (string file in Directory.GetFiles(sourcePath))
        {
            string fileName = Path.GetFileName(file);
            string targetFile = Path.Combine(targetPath, fileName);

            File.Copy(file, targetFile, true);
        }
    }
}
