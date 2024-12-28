using System;
using BetterGenshinImpact.GameTask.Common;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.Core.Video;

public class VideoRecorderFactory
{
    
   private static ObsRecorder? _obsRecorder;
    
    public static IVideoRecorder Create(string recorderType, string fileName)
    {
        switch (recorderType)
        {
            case "ffmpeg":
                return new FfmpegRecorder(fileName);
            case "obs":
                _obsRecorder ??= new ObsRecorder();
                return _obsRecorder;
            default:
                throw new ArgumentException("不支持的录制工具");
        }
    }
}