using System;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using BetterGenshinImpact.Model.Gear.Parameter;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.Model.Gear;

/// <summary>
/// 直接使用C#反射来执行任务的GearTask
/// 可以调用任意C#方法
/// 需要在配置中指定要调用的方法名和参数
/// </summary>
public class CSharpReflectionGearTask : BaseGearTask
{
    private readonly CSharpReflectionGearTaskParams _params;
    private readonly ILogger<CSharpReflectionGearTask> _logger = App.GetLogger<CSharpReflectionGearTask>();
    
    public CSharpReflectionGearTask(CSharpReflectionGearTaskParams paramsObj)
    {
        _params = paramsObj;
    }
    
    public override async Task Run()
    {
        if (string.IsNullOrWhiteSpace(_params.MethodPath))
        {
            _logger.LogWarning("MethodPath参数为空，无法执行反射调用");
            return;
        }

        try
        {
            await ExecuteMethodCall(_params.MethodPath, _params.ParametersJson);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "执行反射方法调用时发生异常: {MethodPath}", _params.MethodPath);
            throw;
        }
    }

    private async Task ExecuteMethodCall(string methodPath, string parametersJson)
    {
        // 解析JSON参数
        JToken[] parameters = new JToken[0];
        if (!string.IsNullOrWhiteSpace(parametersJson))
        {
            try
            {
                var jsonArray = JArray.Parse(parametersJson);
                parameters = jsonArray.ToArray();
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "解析参数JSON失败: {ParametersJson}", parametersJson);
                throw new InvalidOperationException($"参数JSON格式错误: {ex.Message}");
            }
        }

        _logger.LogInformation("准备执行反射调用: {MethodPath}, 参数数量: {ParamCount}", methodPath, parameters.Length);

        // 解析类型和方法名
        Type targetType;
        string methodName;
        
        if (methodPath.Contains(':'))
        {
            // 格式: AssemblyName:ClassName.MethodName
            var assemblyParts = methodPath.Split(':');
            var assemblyName = assemblyParts[0];
            var classMethodParts = assemblyParts[1].Split('.');
            var className = string.Join(".", classMethodParts.Take(classMethodParts.Length - 1));
            methodName = classMethodParts.Last();
            
            var assembly = Assembly.LoadFrom(assemblyName);
            targetType = assembly.GetType(className) ?? throw new InvalidOperationException($"无法找到类型: {className}");
        }
        else
        {
            // 格式: ClassName.MethodName
            var parts2 = methodPath.Split('.');
            methodName = parts2.Last();
            var className = string.Join(".", parts2.Take(parts2.Length - 1));
            
            // 在当前程序集和已加载的程序集中查找类型
            targetType = Type.GetType(className) ?? 
                        AppDomain.CurrentDomain.GetAssemblies()
                            .SelectMany(a => a.GetTypes())
                            .FirstOrDefault(t => t.FullName == className || t.Name == className);
            
            if (targetType == null)
            {
                throw new InvalidOperationException($"无法找到类型: {className}");
            }
        }

        // 获取方法信息
        var methods = targetType.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance)
            .Where(m => m.Name == methodName)
            .ToArray();

        if (methods.Length == 0)
        {
            throw new InvalidOperationException($"无法找到方法: {methodName}");
        }

        // 选择最匹配的方法
        MethodInfo targetMethod = null;
        object[] convertedParams = null;

        foreach (var method in methods)
        {
            var paramTypes = method.GetParameters();
            if (paramTypes.Length == parameters.Length)
            {
                try
                {
                    convertedParams = ConvertParameters(parameters, paramTypes);
                    targetMethod = method;
                    break;
                }
                catch
                {
                    // 参数转换失败，尝试下一个重载
                    continue;
                }
            }
        }

        if (targetMethod == null)
        {
            throw new InvalidOperationException($"无法找到匹配的方法重载: {methodName}, 参数数量: {parameters.Length}");
        }

        // 执行方法
        object instance = null;
        if (!targetMethod.IsStatic)
        {
            // 创建实例（假设有无参构造函数）
            instance = Activator.CreateInstance(targetType);
        }

        _logger.LogInformation("执行方法: {TypeName}.{MethodName}", targetType.Name, methodName);
        
        var result = targetMethod.Invoke(instance, convertedParams);
        
        // 如果返回Task，等待完成
        if (result is Task task)
        {
            await task;
        }
        
        _logger.LogInformation("方法执行完成");
    }

    private object[] ConvertParameters(JToken[] parameters, ParameterInfo[] paramTypes)
    {
        var convertedParams = new object[parameters.Length];
        
        for (int i = 0; i < parameters.Length; i++)
        {
            var paramValue = parameters[i];
            var paramType = paramTypes[i].ParameterType;
            
            // 处理空值
            if (paramValue.Type == JTokenType.Null)
            {
                convertedParams[i] = null;
                continue;
            }
            
            // 类型转换
            convertedParams[i] = ConvertJsonToType(paramValue, paramType);
        }
        
        return convertedParams;
    }

    private object ConvertJsonToType(JToken jToken, Type targetType)
    {
        // 处理可空类型
        var underlyingType = Nullable.GetUnderlyingType(targetType) ?? targetType;
        
        // 基本类型处理
        if (underlyingType == typeof(string))
            return jToken.Value<string>();
        
        if (underlyingType == typeof(int))
            return jToken.Value<int>();
        
        if (underlyingType == typeof(long))
            return jToken.Value<long>();
        
        if (underlyingType == typeof(double))
            return jToken.Value<double>();
        
        if (underlyingType == typeof(float))
            return jToken.Value<float>();
        
        if (underlyingType == typeof(bool))
            return jToken.Value<bool>();
        
        if (underlyingType == typeof(DateTime))
            return jToken.Value<DateTime>();
        
        if (underlyingType == typeof(decimal))
            return jToken.Value<decimal>();
        
        if (underlyingType.IsEnum)
        {
            if (jToken.Type == JTokenType.String)
                return Enum.Parse(underlyingType, jToken.Value<string>(), true);
            else
                return Enum.ToObject(underlyingType, jToken.Value<int>());
        }
        
        // 复杂对象类型，使用JsonConvert反序列化
        try
        {
            return jToken.ToObject(targetType);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "无法将JSON转换为类型 {TargetType}: {JsonText}", targetType.Name, jToken.ToString());
            throw new InvalidOperationException($"无法将JSON转换为类型 {targetType.Name}: {ex.Message}");
        }
    }
    
    private object ConvertToType(string value, Type targetType)
    {
        // 处理可空类型
        var underlyingType = Nullable.GetUnderlyingType(targetType) ?? targetType;
        
        if (underlyingType == typeof(string))
            return value;
        
        if (underlyingType == typeof(int))
            return int.Parse(value, CultureInfo.InvariantCulture);
        
        if (underlyingType == typeof(long))
            return long.Parse(value, CultureInfo.InvariantCulture);
        
        if (underlyingType == typeof(double))
            return double.Parse(value, CultureInfo.InvariantCulture);
        
        if (underlyingType == typeof(float))
            return float.Parse(value, CultureInfo.InvariantCulture);
        
        if (underlyingType == typeof(bool))
            return bool.Parse(value);
        
        if (underlyingType == typeof(DateTime))
            return DateTime.Parse(value, CultureInfo.InvariantCulture);
        
        if (underlyingType.IsEnum)
            return Enum.Parse(underlyingType, value, true);
        
        // 尝试使用Convert.ChangeType
        return Convert.ChangeType(value, underlyingType, CultureInfo.InvariantCulture);
    }
}