using System;

namespace BetterGenshinImpact.GameTask.LogParse;

public class NoLoginException(string message = "未登录") : Exception(message)
{
}