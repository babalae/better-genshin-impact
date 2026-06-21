# CSharpReflectionGearTask 使用说明

## 概述
`CSharpReflectionGearTask` 是一个通过反射调用任意C#方法的任务类。它将方法路径和参数分离，支持JSON格式的复杂参数输入。使用 Newtonsoft.Json 库进行JSON解析，提供更好的容错性和兼容性。

## 参数格式

### MethodPath 参数格式
```
格式1: "ClassName.MethodName"
格式2: "AssemblyName:ClassName.MethodName"
```

- `ClassName`: 类的完整名称（包含命名空间）
- `MethodName`: 要调用的方法名
- `AssemblyName`: 程序集名称（可选，用于加载外部程序集）

### ParametersJson 参数格式
```
格式: JSON数组字符串
例如: "[\"param1\", 123, {\"name\": \"value\"}]"
```

- 支持基本类型：string, int, double, bool, DateTime等
- 支持复杂对象：通过JSON反序列化
- 支持数组和集合类型
- 空值使用 `null`

## 使用示例

### 1. 调用静态方法
```csharp
var params = new CSharpReflectionGearTaskParams
{
    MethodPath = "System.Console.WriteLine",
    ParametersJson = "[\"Hello World\"]"
};
```

### 2. 调用带多个参数的方法
```csharp
var params = new CSharpReflectionGearTaskParams
{
    MethodPath = "System.Math.Max",
    ParametersJson = "[10, 20]"
};
```

### 3. 调用无参数方法
```csharp
var params = new CSharpReflectionGearTaskParams
{
    MethodPath = "System.GC.Collect",
    ParametersJson = "[]"
};
```

### 4. 使用外部程序集
```csharp
var params = new CSharpReflectionGearTaskParams
{
    MethodPath = "MyAssembly.dll:MyNamespace.MyClass.MyMethod",
    ParametersJson = "[\"param1\", \"param2\"]"
};
```

### 5. 传递复杂对象参数
```csharp
var params = new CSharpReflectionGearTaskParams
{
    MethodPath = "MyNamespace.MyClass.ProcessData",
    ParametersJson = "[{\"name\": \"张三\", \"age\": 25, \"active\": true}]"
};
```

### 6. 传递数组参数
```csharp
var params = new CSharpReflectionGearTaskParams
{
    MethodPath = "MyNamespace.MyClass.ProcessArray",
    ParametersJson = "[[1, 2, 3, 4, 5]]"
};
```

### 7. 混合类型参数
```csharp
var params = new CSharpReflectionGearTaskParams
{
    MethodPath = "MyNamespace.MyClass.ComplexMethod",
    ParametersJson = "[\"text\", 123, true, {\"key\": \"value\"}, [1, 2, 3]]"
};
```

## 支持的参数类型

### 基本类型
- `string`: 字符串
- `int`: 整数
- `long`: 长整数
- `double`: 双精度浮点数
- `float`: 单精度浮点数
- `decimal`: 十进制数
- `bool`: 布尔值
- `DateTime`: 日期时间
- `Enum`: 枚举类型（支持字符串名称和数值）

### 复杂类型
- `object`: 自定义类和结构体
- `Array`: 数组类型
- `List<T>`: 泛型集合
- `Dictionary<TKey, TValue>`: 字典类型
- 可空类型：如 `int?`, `DateTime?` 等

### 特殊值
- `null`: 空值（JSON中使用 `null`）

## 注意事项

1. **实例方法**: 系统会自动创建类的实例（要求有无参构造函数）
2. **参数转换**: 支持JSON到.NET类型的自动转换
3. **异步支持**: 如果方法返回 `Task`，系统会自动等待其完成
4. **方法重载**: 根据参数数量和类型自动匹配最合适的重载
5. **复杂对象**: 通过JsonSerializer进行反序列化，支持嵌套对象
6. **类型安全**: 严格的类型检查和转换，转换失败会抛出异常
7. **日志记录**: 完整的执行日志，便于调试和监控
8. **JSON格式**: 参数必须是有效的JSON数组格式

## 错误处理

- 如果找不到指定的类型或方法，会抛出 `InvalidOperationException`
- 如果参数转换失败，会尝试其他方法重载
- 所有异常都会被记录到日志中