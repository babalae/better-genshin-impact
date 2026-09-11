using System.Collections.Generic;
using BetterGenshinImpact.Core.Recognition.ONNX;
using Microsoft.ML.OnnxRuntime;

namespace BetterGenshinImpact.Service.Interface;

public interface IOnnxRuntimePluginRegistry
{
    bool TryRegister(OnnxRuntimePluginResolution resolution, out IReadOnlyList<OrtEpDevice> devices,
        out string error);
    IReadOnlyList<OrtEpDevice> GetDevices(string epName);
}
