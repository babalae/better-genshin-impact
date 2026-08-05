using System;
namespace BetterGenshinImpact.GameTask.AutoPathing.Handler;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public class ActionHandlerAttribute : Attribute
{
    public string Key { get; }
    public string Method { get; }

    public ActionHandlerAttribute(string key, string method = "After")
    {
        Key = key;
        Method = method;
    }
}