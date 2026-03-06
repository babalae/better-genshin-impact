using System;
using Microsoft.ClearScript;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.Core.Script.Dependence;

/// <summary>
/// 变量辅助类，封装HostFunctions的newVar方法
/// </summary>
public class VarHelper
{
    private readonly HostFunctions _hostFunctions = new();
    private readonly ILogger<VarHelper> _logger = App.GetLogger<VarHelper>();

    /// <summary>
    /// 创建指定类型的变量占位符，用于接收out参数
    /// </summary>
    /// <typeparam name="T">目标类型</typeparam>
    /// <param name="initValue">可选的初始值</param>
    /// <returns>变量占位符对象，支持value、out、ref属性</returns>
    public object NewVar<T>(T initValue = default!)
    {
        _logger.LogDebug("创建变量占位符(泛型)，类型: {Type}", typeof(T));
        return _hostFunctions.newVar<T>(initValue);
    }

    /// <summary>
    /// 通过类型名称创建变量占位符
    /// </summary>
    /// <param name="typeName">类型名称（如 "OpenCvSharp.Size", "OpenCvSharp.Point[]"）</param>
    /// <returns>变量占位符对象，支持value、out、ref属性</returns>
    /// <exception cref="ArgumentException">类型名称无法解析时抛出</exception>
    public object NewVar(string typeName)
    {
        var type = ResolveType(typeName);
        if (type == null)
        {
            throw new ArgumentException($"无法解析类型 '{typeName}'");
        }

        _logger.LogDebug("创建变量占位符(字符串)，类型名称: {TypeName}", typeName);
        
        // 使用反射调用泛型方法
        var method = typeof(HostFunctions).GetMethod("newVar");
        var genericMethod = method!.MakeGenericMethod(type);
        return genericMethod.Invoke(_hostFunctions, new object?[] { null })!;
    }

    /// <summary>
    /// 解析类型名称为Type对象
    /// </summary>
    /// <param name="typeName">类型名称</param>
    /// <returns>Type对象，解析失败返回null</returns>
    private Type? ResolveType(string typeName)
    {
        // 处理数组类型（如 "OpenCvSharp.Point[]"）
        if (typeName.EndsWith("[]"))
        {
            var elementTypeName = typeName.Substring(0, typeName.Length - 2);
            var elementType = ResolveType(elementTypeName);
            if (elementType != null)
            {
                return elementType.MakeArrayType();
            }
            return null;
        }

        // 遍历已加载的程序集查找类型
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            var type = assembly.GetType(typeName);
            if (type != null) return type;
        }
        
        return null;
    }

    /// <summary>
    /// 从占位符中获取值
    /// </summary>
    /// <param name="placeholder">变量占位符</param>
    /// <returns>占位符中存储的值</returns>
    public object? GetValue(object placeholder)
    {
        if (placeholder == null)
        {
            _logger.LogWarning("占位符为null");
            return null;
        }

        try
        {
            var type = placeholder.GetType();
            var valueProperty = type.GetProperty("Value");
            if (valueProperty != null)
            {
                var value = valueProperty.GetValue(placeholder);
                _logger.LogDebug("获取占位符值: {Value}", value);
                return value;
            }
            
            _logger.LogWarning("占位符类型 {Type} 没有Value属性", type.Name);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("获取占位符值失败: {Message}", ex.Message);
            return null;
        }
    }


}


