using System.Collections.Generic;

namespace BetterGenshinImpact.Service.Interface;

public interface ICollectedPointsService
{
    IReadOnlySet<string> CollectedIds { get; }

    bool IsCollected(string pointId);

    bool Toggle(string pointId);

    void Save();
}
