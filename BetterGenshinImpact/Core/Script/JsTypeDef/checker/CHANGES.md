# 类型检查生成器改进

## 修改内容

### 1. 添加方法重载信息收集

在 `analyzeObject` 函数中添加了 `getMethodOverloads` 辅助函数，用于获取 C# 方法的重载信息：

```javascript
function getMethodOverloads(typeObj, methodName) {
  // 使用 TypeHelper.GetMethodDefinitionForType 获取方法的所有重载定义
  // 返回包含 name, definition, parameterTypes, paramCount 的数组
}
```

### 2. 收集静态方法和实例方法的重载

修改了以下部分以收集重载信息：

- **静态方法**：在分析构造函数的静态方法时，获取并保存重载信息
- **实例方法**：在分析构造函数的实例方法时，获取并保存重载信息

### 3. 生成重载验证代码

在生成的 TypeScript 类型断言代码中：

#### 添加了重载验证辅助函数：

```typescript
function assertOverloads(
  fn: Function, 
  overloads: Array<{name: string, definition: string, paramCount: number}>
): void {
  // 验证方法重载（当前仅验证参数个数）
}
```

#### 为每个方法生成重载检查：

```typescript
// 验证方法 templateMatch 的重载 (3 个重载)
// - TemplateMatch(Mat mat)
// - TemplateMatch(Mat mat, Boolean useMask, Color maskColor = null)
// - TemplateMatch(Mat mat, Double x, Double y, Double w, Double h)
assertOverloads(Vision.templateMatch, [
  {name: "TemplateMatch", definition: "TemplateMatch(Mat mat)", paramCount: 1},
  {name: "TemplateMatch", definition: "TemplateMatch(Mat mat, Boolean useMask, Color maskColor = null)", paramCount: 3},
  {name: "TemplateMatch", definition: "TemplateMatch(Mat mat, Double x, Double y, Double w, Double h)", paramCount: 5}
]);
```

### 4. 配置更新

将 `TypeHelper` 添加到 `excludeGlobals` 列表中，避免将其作为全局变量进行类型检查。

## 使用方法

1. 在 ClearScript 运行时环境中运行 `generate-type-checks.js`
2. 脚本会自动调用 `TypeHelper.GetMethodDefinitionForType` 获取方法重载信息
3. 生成的 `runtime-type-assertions.ts` 文件包含重载验证代码
4. 运行 `tsc --noEmit` 检查类型错误

## 示例输出

对于一个具有多个重载的构造函数方法，生成的代码如下：

```typescript
// Vision - function (Constructor)
if (typeof Vision !== 'undefined') {
  // 验证 Vision 是一个可调用的函数
  assertCallable(Vision);
  
  // Vision 的静态方法
  if ('templateMatch' in Vision && typeof Vision.templateMatch === 'function') {
    assertCallable(Vision.templateMatch);
    // 验证方法 templateMatch 的重载 (3 个重载)
    // - TemplateMatch(Mat mat)
    // - TemplateMatch(Mat mat, Boolean useMask, Color maskColor = null)
    // - TemplateMatch(Mat mat, Double x, Double y, Double w, Double h)
    assertOverloads(Vision.templateMatch, [
      {name: "TemplateMatch", definition: "TemplateMatch(Mat mat)", paramCount: 1},
      {name: "TemplateMatch", definition: "TemplateMatch(Mat mat, Boolean useMask, Color maskColor = null)", paramCount: 3},
      {name: "TemplateMatch", definition: "TemplateMatch(Mat mat, Double x, Double y, Double w, Double h)", paramCount: 5}
    ]);
  }
}
```

## 注意事项

- 当前实现仅验证参数个数，暂不验证参数类型
- 需要在 ClearScript 环境中运行，因为依赖 `TypeHelper.GetMethodDefinitionForType` 方法
- 如果无法获取重载信息，会记录警告日志但不会中断执行
