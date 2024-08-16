using BetterGenshinImpact.GameTask.Model;
using System.Threading;

namespace BetterGenshinImpact.GameTask.AutoMusicGame;

public class AutoMusicGameParam(CancellationTokenSource cts) : BaseTaskParam(cts);
