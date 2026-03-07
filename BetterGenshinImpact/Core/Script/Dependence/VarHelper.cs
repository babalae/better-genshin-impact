using System;
using System.Collections.Generic;
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
    /// 允许创建的类型白名单（全量匹配，精确匹配完整类型名称）
    /// </summary>
    private static readonly HashSet<string> TypeWhitelist = new(StringComparer.OrdinalIgnoreCase)
    {
        // 基本类型
        "System.Int32",
        "System.Double",
        "System.String",
        "System.Boolean",
        "System.Single",
        "System.Int64",
    };

    /// <summary>
    /// 允许的程序集白名单（程序集匹配，允许该程序集下的所有类型）
    /// </summary>
    private static readonly HashSet<string> AssemblyWhitelist = new(StringComparer.OrdinalIgnoreCase)
    {
        "OpenCvSharp",
        "OpenCvSharp.Extensions",
    };

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
    /// <exception cref="ArgumentException">类型名称无法解析或不在白名单中时抛出</exception>
    public object NewVar(string typeName)
    {
        // 获取基础类型名称（去除数组标记）
        var baseTypeName = GetBaseTypeName(typeName);
        
        // 检查白名单：全量匹配 或 程序集匹配
        if (!IsTypeAllowed(baseTypeName))
        {
            throw new ArgumentException($"类型 '{typeName}' 不在允许的白名单中");
        }

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
    /// 获取基础类型名称（去除数组标记）
    /// </summary>
    private static string GetBaseTypeName(string typeName)
    {
        return typeName.Replace("[]", "").Trim();
    }

    /// <summary>
    /// 检查类型是否在白名单中（全量匹配 或 程序集匹配）
    /// </summary>
    private bool IsTypeAllowed(string typeName)
    {
        // 1. 全量匹配：精确匹配完整类型名称
        if (TypeWhitelist.Contains(typeName))
        {
            return true;
        }

        // 2. 程序集匹配：检查类型所属程序集是否在白名单中
        var namespacePrefix = typeName.Split('.')[0];
        if (AssemblyWhitelist.Contains(namespacePrefix))
        {
            return true;
        }

        return false;
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
}
