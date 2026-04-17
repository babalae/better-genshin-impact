namespace BetterGenshinImpact.Model.Gear.Parameter;

public class CSharpReflectionGearTaskParams : BaseGearTaskParams
{
    /// <summary>
    /// 要调用的方法路径
    /// 格式: "ClassName.MethodName" 或 "AssemblyName:ClassName.MethodName"
    /// </summary>
    public string MethodPath { get; set; } = string.Empty;
    
    /// <summary>
    /// 方法参数的JSON字符串
    /// 支持基本类型、复杂对象、数组等
    /// 例如: "[\"hello\", 123, {\"name\": \"test\"}]"
    /// </summary>
    public string ParametersJson { get; set; } = string.Empty;
}