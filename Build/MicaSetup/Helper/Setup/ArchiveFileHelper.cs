using MicaSetup.Controls.Animations;
using SharpCompress.Archives.GZip;
using SharpCompress.Archives.Rar;
using SharpCompress.Archives.SevenZip;
using SharpCompress.Archives.Zip;
using SharpCompress.Common;
using SharpCompress.Readers;
using System;
using System.IO;

namespace MicaSetup.Helper;

public static class ArchiveFileHelper
{
    public static long TotalUncompressSize(string filePath, ReaderOptions? readerOptions = null!)
    {
        using dynamic archive = filePath.OpenArchive(readerOptions);
        return archive.TotalUncompressSize;
    }

    public static long TotalUncompressSize(Stream stream, ReaderOptions? readerOptions = null!)
    {
        using dynamic archive = stream.OpenArchive(readerOptions);
        return archive.TotalUncompressSize;
    }

    public static void ExtractAll(string destinationDirectory, string filePath, ReaderOptions? readerOptions = null!, ExtractionOptions? options = null)
    {
        using dynamic archive = filePath.OpenArchive(readerOptions);
        using IReader reader = archive.ExtractAllEntries();
        reader.WriteAllToDirectory(destinationDirectory, options);
    }

    public static void ExtractAll(string destinationDirectory, Stream stream, Action<double, string> progressCallback = null!, ReaderOptions? readerOptions = null!, ExtractionOptions? options = null)
    {
        using dynamic archive = stream.OpenArchive(readerOptions);
        using IReader reader = archive.ExtractAllEntries();
        using ProgressAccumulator acc = new(anime: DoubleEasingAnimations.EaseInOutCirc);
        long currentTotalSize = default;
        double currentProgress = default;
        string currentKey = null!;

        void ProgressCallback(double progress)
        {
            if (currentProgress != progress || currentKey != reader.Entry.Key)
            {
                progressCallback?.Invoke(currentProgress = progress, currentKey = reader.Entry.Key);
            }
        }

        while (reader.MoveToNextEntry())
        {
            ProgressCallback(Math.Min(currentTotalSize / (double)archive.TotalUncompressSize, 1d));

            if ((reader.Entry.Size / 1048576d) > 1d)
            {
                _ = acc.Reset(Math.Min(currentTotalSize / (double)archive.TotalUncompressSize, 1d),
                    Math.Min((currentTotalSize + reader.Entry.Size) / (double)archive.TotalUncompressSize, 1d),
                    reader.Entry.Size / 8912.896,
                    ProgressCallback
                ).Start();
            }
            reader.WriteEntryToDirectory(destinationDirectory, options);

            acc.Stop();
            currentTotalSize += reader.Entry.Size;
            ProgressCallback(Math.Min(currentTotalSize / (double)archive.TotalUncompressSize, 1d));
        }
    }
}

file enum ArchiveFileType
{
    Unknown,
    SevenZip,
    Zip,
    Rar,
    GZip,
}

file static class ArchiveFileHelperExtension
{
    public static ArchiveFileType GetArchiveType(this Stream stream)
    {
        byte[] header = new byte[3];
        stream.Read(header, 0, header.Length);

        if (header[0] == 0x37 && header[1] == 0x7A)
        {
            return ArchiveFileType.SevenZip;
        }
        else if (header[0] == 0x50 && header[1] == 0x4B)
        {
            return ArchiveFileType.Zip;
        }
        else if (header[0] == 0x52 && header[1] == 0x61 && header[2] == 0x72)
        {
            return ArchiveFileType.Rar;
        }
        else if (header[0] == 0x1F && header[1] == 0x8B)
        {
            return ArchiveFileType.GZip;
        }
        return ArchiveFileType.Unknown;
    }

    public static ArchiveFileType GetArchiveType(this string filePath)
    {
        using Stream fileStream = filePath.OpenRead();
        return GetArchiveType(fileStream);
    }

    public static Stream OpenRead(this string filePath)
    {
        return new FileStream(filePath, FileMode.Open, FileAccess.Read);
    }

    public static dynamic OpenArchive(this string filePath, ReaderOptions? readerOptions = null!)
    {
        ArchiveFileType type = filePath.GetArchiveType();
        dynamic? archive = type switch
        {
            ArchiveFileType.Zip => ZipArchive.Open(filePath, readerOptions),
            ArchiveFileType.GZip => GZipArchive.Open(filePath, readerOptions),
            ArchiveFileType.Rar => RarArchive.Open(filePath, readerOptions),
            ArchiveFileType.SevenZip or _ => SevenZipArchive.Open(filePath, readerOptions),
        };
        return archive;
    }

    public static dynamic OpenArchive(this Stream stream, ReaderOptions? readerOptions = null!)
    {
        ArchiveFileType type = stream.GetArchiveType();
        using dynamic? archive = type switch
        {
            ArchiveFileType.Zip => ZipArchive.Open(stream, readerOptions),
            ArchiveFileType.GZip => GZipArchive.Open(stream, readerOptions),
            ArchiveFileType.Rar => RarArchive.Open(stream, readerOptions),
            ArchiveFileType.SevenZip or _ => SevenZipArchive.Open(stream, readerOptions),
        };
        return archive;
    }
}
