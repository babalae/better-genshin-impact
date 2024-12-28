using System;

namespace BetterGenshinImpact.Core.Video;

public interface IVideoRecorder : IDisposable
{
    public bool Start();
    
    public void Stop();
}