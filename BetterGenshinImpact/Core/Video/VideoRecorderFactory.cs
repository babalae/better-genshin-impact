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
                if (_obsRecorder == null)
                {
                    _obsRecorder = new ObsRecorder(fileName);
                    // TaskControl.Logger.LogInformation("当前选择使用OBS录制，OBS首次启动较慢，请耐心等待...");
                }
                return _obsRecorder;
            default:
                throw new ArgumentException("不支持的录制工具");
        }
    }
}