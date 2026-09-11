using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Recognition.ONNX;

namespace BetterGenshinImpact.Service.Interface;

public interface IInferenceDeviceDiscoveryService
{
    Task<IReadOnlyList<InferenceDeviceDescriptor>> DiscoverAsync(CancellationToken cancellationToken = default);
}
