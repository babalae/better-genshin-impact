using Microsoft.Extensions.Logging;
using Microsoft.ClearScript;
using System;
using System.Reflection;
using System.Collections.Generic;
using System.Linq;

namespace BetterGenshinImpact.Core.Script.Dependence;

public class TypeHelper
{
    private HostFunctions hostFunctions = new HostFunctions();
    public TypeHelper(IScriptEngine engine)
    {
    }
    private readonly ILogger<TypeHelper> _logger = App.GetLogger<TypeHelper>();

    public bool isTypeObj(object obj)
    {
        if (obj == null) return false;
        return hostFunctions.isTypeObj(obj);
    }

    private Type getRealType(object obj)
    {
        var asm = typeof(HostItemFlags).Assembly;

        // public Type[] Types { get; }
        var hostType = asm.GetType("Microsoft.ClearScript.HostType", true);
        if (hostType == null) throw new InvalidOperationException("HostType type not found");
        var typesProp = hostType.GetProperty("Types", BindingFlags.Public | BindingFlags.Instance);
        if (typesProp == null) throw new InvalidOperationException("HostType.Types property not found");
        var types = (Type[]?)typesProp.GetValue(obj);
        if (types == null || types.Length == 0) throw new InvalidOperationException("HostType.Types is null or empty");
        if (types.Length > 1) throw new InvalidOperationException("HostType.Types has multiple types, cannot determine which one to create");
        var type = types[0];
        return type;
    }

    public object GetNullInstance(object obj)
    {
        // obj是HostType，通过反射获取真实Type，然后创建一个nullwrapper
        var asm = typeof(HostItemFlags).Assembly;

        var type = getRealType(obj);
        // _logger.LogDebug($"[Script] 创建类型 {type.FullName} 的 null HostObject");

        // private static HostObject GetNullWrapper(Type type)
        var hostObject = asm.GetType("Microsoft.ClearScript.HostObject", true);
        if (hostObject == null) throw new InvalidOperationException("HostObject type not found");
        var getNullWrapperMethod = hostObject.GetMethod("GetNullWrapper", BindingFlags.NonPublic | BindingFlags.Static);
        if (getNullWrapperMethod == null) throw new InvalidOperationException("HostObject.GetNullWrapper method not found");
        var instance = getNullWrapperMethod.Invoke(null, [type]);
        if (instance == null) throw new InvalidOperationException("HostObject.GetNullWrapper returned null");

        return instance;
    }

    public Type GetType(object obj)
    {
        if (obj == null) throw new ArgumentNullException(nameof(obj));
        _logger.LogDebug($"[Script] 获取对象 {obj} 的类型 {obj?.GetType().FullName}");
        return obj.GetType();
    }

    private Dictionary<string, object> dumpMethodInfo(MethodInfo methodInfo)
    {
        var parameters = methodInfo.GetParameters();
        var paramStrs = new List<string>();
        foreach (var param in parameters)
        {
            var paramStr = $"{param.ParameterType.Name} {param.Name}";
            if (param.IsOptional)
            {
                paramStr += $" = {param.DefaultValue ?? "null"}";
            }
            paramStrs.Add(paramStr);
        }
        var definition = $"{methodInfo.Name}({string.Join(", ", paramStrs)})";
        _logger.LogDebug($"[Script] 方法定义: {definition}");
        var definitionDict = new Dictionary<string, object>
        {
            { "name", methodInfo.Name },
            { "definition", definition },
            { "parameterTypes", parameters.AsEnumerable().Select(p => p.ParameterType.FullName) },
        };
        return definitionDict;
    }

    public string GetMethodDefinition(object method)
    {
        _logger.LogDebug($"[Script] 获取方法 {method} 的定义");
        var asm = typeof(HostItemFlags).Assembly;
        var hostMethod = asm.GetType("Microsoft.ClearScript.HostMethod", true);
        if (hostMethod == null) throw new InvalidOperationException("HostMethod type not found");
        // private readonly HostItem target;
        var targetField = hostMethod.GetField("target", BindingFlags.NonPublic | BindingFlags.Instance);
        if (targetField == null) throw new InvalidOperationException("HostMethod.target field not found");
        var target = targetField.GetValue(method);
        if (target == null) throw new InvalidOperationException("HostMethod.target is null");
        var targetType = target.GetType();

        // assert target type is HostItem
        var hostItem = asm.GetType("Microsoft.ClearScript.HostItem", true);
        if (hostItem == null) throw new InvalidOperationException("HostItem type not found");
        if (!hostItem.IsAssignableFrom(targetType))
        {
            throw new InvalidOperationException($"HostMethod.target is not a HostItem, but {targetType.FullName}");
        }

        // private readonly string name;
        var nameField = hostMethod.GetField("name", BindingFlags.NonPublic | BindingFlags.Instance);
        if (nameField == null) throw new InvalidOperationException("HostItem.name field not found");
        var name = (string?)nameField.GetValue(method);
        if (name == null) throw new InvalidOperationException("HostItem.name is null");

        _logger.LogDebug($"[Script] 获取方法定义 {target} {name}");

        // public HostTarget Target { get; }
        var targetProp = hostItem.GetProperty("Target", BindingFlags.Public | BindingFlags.Instance);
        if (targetProp == null) throw new InvalidOperationException("HostItem.Target property not found");
        var targetValue = targetProp.GetValue(target); // should be hostType
        _logger.LogDebug($"[Script] 方法目标类型 {targetValue} {targetValue.GetType().FullName}");

        // if type is HostType, get the real Type
        if (isTypeObj(targetValue))
        {
            return GetMethodDefinitionForType(targetValue, name);
        }
        else
        {
            throw new InvalidOperationException($"HostItem.Target is not a HostType, but {targetValue?.GetType().FullName}");
        }
    }

    private string dumpMethodDefinitionForType(Type type, string methodName)
    {
        _logger.LogDebug($"[Script] 获取类型 {type.FullName} 的方法 {methodName} 定义");
        var matchedMethods = new List<MethodInfo>();
        foreach (var methodInfo in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
        {
            if (methodInfo.Name.Equals(methodName, StringComparison.OrdinalIgnoreCase))
            {
                matchedMethods.Add(methodInfo);
            }
        }
        if (matchedMethods.Count == 0)
        {
            throw new InvalidOperationException($"类型 {type.FullName} 没有找到方法 {methodName}");
        }
        // convert method info to string function definition
        var definitions = new List<Dictionary<string, object>>();
        foreach (var methodInfo in matchedMethods)
        {
            definitions.Add(dumpMethodInfo(methodInfo));
        }
        // return json string
        return System.Text.Json.JsonSerializer.Serialize(definitions);
    }
    
    public string GetMethodDefinitionForType(object typeObj, string methodName)
    {
        if (!isTypeObj(typeObj))
        {
            throw new ArgumentException("参数不是类型对象", nameof(typeObj));
        }
        return dumpMethodDefinitionForType(getRealType(typeObj), methodName);
    }
    public string GetMethodDefinitionForType<T>(string methodName)
    {
        return dumpMethodDefinitionForType(typeof(T), methodName);
    }
}