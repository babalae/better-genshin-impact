using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Recognition.ONNX;

namespace BetterGenshinImpact.Service.Interface;

public interface IInferenceDeviceDiscoveryService
{
    Task<IReadOnlyList<InferenceDeviceDescriptor>> DiscoverAsync(
        HardwareAccelerationConfig? config = null,
        CancellationToken cancellationToken = default);
}
