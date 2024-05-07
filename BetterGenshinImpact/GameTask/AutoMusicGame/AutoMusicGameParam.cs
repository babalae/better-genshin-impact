using System.Threading;
using BetterGenshinImpact.GameTask.Model;

namespace BetterGenshinImpact.GameTask.AutoMusicGame;

public class AutoMusicGameParam(CancellationTokenSource cts) : BaseTaskParam(cts);
