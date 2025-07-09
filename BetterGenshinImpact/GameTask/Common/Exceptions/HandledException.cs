namespace BetterGenshinImpact.GameTask.AutoGeniusInvokation.Exception; // TODO: change this namespace to BetterGenshinImpact.GameTask.Common.Exception

/// <summary>
/// 无需再次抛出的异常类，通常用于在任务中捕获异常后处理完毕，不要再向上抛出异常。
/// </summary>
/// <param name="message"></param>
public class HandledException(string message) : System.Exception(message)
{
}
