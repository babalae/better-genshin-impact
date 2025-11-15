/**
 * 高级类型检查生成器
 * 使用TypeScript的结构化类型系统进行运行时类型验证
 * 
 * 核心思想：
 * 1. 不检查精确类型匹配，而是检查结构兼容性
 * 2. 使用 TypeScript 的 "鸭子类型" 特性
 * 3. 让 TSC 自动推断并验证类型兼容性
 */

// 使用 IIFE 包裹所有代码，避免污染全局作用域
(function() {
  'use strict';
  
  // ==================== 配置 ====================

  const CONFIG = {
    excludeGlobals: [
      // 基础类型和值
      'undefined', 'NaN', 'Infinity',
      'Object', 'Function', 'Array', 'String', 'Boolean', 'Number', 'Date', 'RegExp', 'Error',
      'Math', 'JSON', 'Promise', 'Symbol', 'Map', 'Set', 'WeakMap', 'WeakSet',
      'Proxy', 'Reflect', 'ArrayBuffer', 'DataView', 'Int8Array', 'Uint8Array',
      'console', 'window', 'document', 'global', 'globalThis',
      'eval', 'parseInt', 'parseFloat', 'isNaN', 'isFinite',
      'decodeURI', 'decodeURIComponent', 'encodeURI', 'encodeURIComponent',
      'escape', 'unescape',
      '__dirname', '__filename', 'require', 'module', 'exports', 'process',
      
      // JavaScript 内置错误类型
      'AggregateError', 'EvalError', 'RangeError', 'ReferenceError', 'SyntaxError', 'TypeError', 'URIError',
      
      // JavaScript 内置对象
      'Intl', 'Atomics',
      
      // TypedArray 类型
      'Uint16Array', 'Int16Array', 'Uint32Array', 'Int32Array', 
      'Float32Array', 'Float64Array', 'Uint8ClampedArray',
      'BigUint64Array', 'BigInt64Array', 'BigInt',
      
      // ES6+ 新增类型
      'FinalizationRegistry', 'WeakRef', 'Iterator', 'SharedArrayBuffer',
      
      // WebAssembly
      'WebAssembly',
      
      // V8 特定
      'gc',

      // ClearScript 相关
      'EngineInternal', 'HostObject', 'HostInvocable', 'HostFunctions', 'TypeHelper',

      // C# 相关
      'Task',

      // OpenCvSharp
      'OpenCvSharp',
    ],
    excludeFunctionProperties: [
      'Target', // C# delegate 属性
      'prototype', // JavaScript 原型
      'caller', // 函数调用者
      'callee', // 函数自身
      'arguments', // 函数参数
    ],
    excludeProperties: [
      'Equals', // C# override Equals
      'ReferenceEquals', // C# override Equals,
      'GetHashCode', 'ToString', 'GetType', // C# override Object 方法
    ],
    maxDepth: 4,
    outputFile: 'runtime-type-assertions.ts',
  };

  // ==================== 类型推断 ====================

  /**
   * 获取值的详细类型信息
   */
  function getDetailedType(value) {
    if (value === null) return { kind: 'null', tsType: 'null' };
    if (value === undefined) return { kind: 'undefined', tsType: 'Unknown' };
    
    const type = typeof value;
    
    if (type === 'string') return { kind: 'string', tsType: 'string' };
    if (type === 'number') return { kind: 'number', tsType: 'number' };
    if (type === 'boolean') return { kind: 'boolean', tsType: 'boolean' };
    
    if (type === 'function') {
      return {
        kind: 'function',
        tsType: 'Function',
        params: value.length,
      };
    }
    
    if (type === 'object') {
      if (Array.isArray(value)) {
        return {
          kind: 'array',
          tsType: 'any[]',
          length: value.length,
        };
      }
      
      if (value instanceof Date) return { kind: 'Date', tsType: 'Date' };
      if (value instanceof RegExp) return { kind: 'RegExp', tsType: 'RegExp' };
      if (value instanceof Promise) return { kind: 'Promise', tsType: 'Promise<any>' };
      if (value instanceof Map) return { kind: 'Map', tsType: 'Map<any, any>' };
      if (value instanceof Set) return { kind: 'Set', tsType: 'Set<any>' };
      
      // 检查构造函数
      if (value.constructor && value.constructor.name !== 'Object') {
        return {
          kind: 'instance',
          tsType: value.constructor.name,
          className: value.constructor.name,
        };
      }
      
      return {
        kind: 'object',
        tsType: 'object',
      };
    }
    
    return { kind: 'unknown', tsType: 'any' };
  }

  /**
   * 安全访问属性
   */
  function safeGetProperty(obj, key) {
    try {
      return obj[key];
    } catch (e) {
      return undefined;
    }
  }

  /**
   * 获取对象的可枚举自有属性
   */
  function getEnumerableProperties(obj, isFunction = false) {
    try {
      const isConstructor = isConstructorType(obj);
      return Object.getOwnPropertyNames(obj).filter(key => {
        try {
          // 如果是函数，先判断是到底是constructor，还是普通函数
          if (isFunction) {
            if (!isConstructor) {
              // 普通函数，不保留任何属性
              return false;
            }
            if (CONFIG.excludeFunctionProperties.includes(key)) {
              return false;
            }
          }
          if (CONFIG.excludeProperties.includes(key)) {
            return false;
          }
          if (false) {
            // 过滤掉访问会出错的属性
            const value = obj[key];
          }
          return true;
        } catch (e) {
          return false;
        }
      });
    } catch (e) {
      return [];
    }
  }

  // ==================== 对象结构分析 ====================

  /**
   * 检查对象是否是构造函数类型（C# Type 对象）
   */
  function isConstructorType(obj) {
    try {
      return HostFunctions.isTypeObj(obj);
    } catch (e) {
      return false;
    }
  }

  /**
   * 尝试创建构造函数的实例
   */
  function tryCreateInstance(typeObj) {
    try {
      // return HostFunctions.newVar(typeObj, null);
      return TypeHelper.GetNullInstance(typeObj);
    } catch (e) {
      log.info(`  ⚠ 无法创建实例: ${e.message}`);
      return null;
    }
  }

  /**
   * 获取方法的重载信息
   */
  function getMethodOverloads(typeObj, methodName) {
    try {
      const overloads = JSON.parse(TypeHelper.GetMethodDefinitionForType(typeObj, methodName));
      if (overloads && Array.isArray(overloads)) {
        return overloads.map(o => ({
          name: o.name,
          definition: o.definition,
          parameterTypes: o.parameterTypes || [],
          paramCount: (o.parameterTypes || []).length
        }));
      }
    } catch (e) {
      log.debug(`  ⚠ 无法获取方法 ${methodName} 的重载信息: ${e.message}`);
    }
    return null;
  }

  /**
   * 分析对象结构
   */
  function analyzeObject(obj, visited = new WeakSet(), depth = 0) {
    if (depth > CONFIG.maxDepth) return null;
    if (obj === null || obj === undefined) return null;
    if (typeof obj !== 'object' && typeof obj !== 'function') return null;
    if (visited.has(obj)) return null;
    
    visited.add(obj);
    
    const structure = {
      properties: {},
      methods: {},
      instanceProperties: {}, // 实例属性
      instanceMethods: {},    // 实例方法
      isConstructor: false,   // 是否是构造函数
    };
    
    const isFunction = typeof obj === 'function';
    const keys = getEnumerableProperties(obj, isFunction);
    log.debug(`Analyzing ${isFunction ? 'function' : 'object'} (depth ${depth}): found ${keys.length} keys [${keys.join(', ')}]`);
    
    // 检查是否是构造函数类型
    const isCtorType = isConstructorType(obj);
    structure.isConstructor = isCtorType;
    
    // 分析静态属性和方法
    for (const key of keys) {
      if (key.startsWith('_')) continue; // 跳过私有属性
      
      const value = safeGetProperty(obj, key);
      const typeInfo = getDetailedType(value);
      
      if (typeInfo.kind === 'function') {
        const methodInfo = {
          params: typeInfo.params,
          type: 'Function',
        };
        
        // 如果是构造函数类型，获取方法重载信息
        if (isCtorType) {
          const overloads = getMethodOverloads(obj, key);
          if (overloads && overloads.length > 0) {
            methodInfo.overloads = overloads;
          }
        }
        
        structure.methods[key] = methodInfo;
      } else {
        structure.properties[key] = {
          type: typeInfo.tsType,
          kind: typeInfo.kind,
        };
        
        // 递归分析嵌套对象
        if (typeInfo.kind === 'object' || typeInfo.kind === 'instance') {
          const nested = analyzeObject(value, visited, depth + 1);
          if (nested && (Object.keys(nested.properties).length > 0 || Object.keys(nested.methods).length > 0)) {
            structure.properties[key].nested = nested;
          }
        }
      }
    }
    
    // 如果是构造函数类型，尝试创建实例并分析实例属性
    if (isCtorType) {
      const instance = tryCreateInstance(obj);
      if (instance) {
        const instanceKeys = getEnumerableProperties(instance, false);
        
        for (const key of instanceKeys) {
          if (key.startsWith('_')) continue;
          
          const value = safeGetProperty(instance, key);
          const typeInfo = getDetailedType(value);
          
          if (typeInfo.kind === 'function') {
            const methodInfo = {
              params: typeInfo.params,
              type: 'Function',
            };
            
            // 获取实例方法的重载信息
            const overloads = getMethodOverloads(obj, key);
            if (overloads && overloads.length > 0) {
              methodInfo.overloads = overloads;
            }
            
            structure.instanceMethods[key] = methodInfo;
          } else {
            structure.instanceProperties[key] = {
              type: typeInfo.tsType,
              kind: typeInfo.kind,
            };
            
            // 递归分析嵌套对象
            if (typeInfo.kind === 'object' || typeInfo.kind === 'instance') {
              const nested = analyzeObject(value, visited, depth + 1);
              if (nested && (Object.keys(nested.properties).length > 0 || Object.keys(nested.methods).length > 0)) {
                structure.instanceProperties[key].nested = nested;
              }
            }
          }
        }
      }
    }
    
    return structure;
  }

  // ==================== 代码生成 ====================

  /**
   * 生成类型断言代码
   * 使用 TypeScript 的结构化类型检查
   */
  
  // 仅在生成器内部使用：根据参数个数生成“纯静态（TSC）重载校验”函数（静态方法）
  function emitStaticOverloadCountChecks(lines, ownerName, methodPath, counts, indent = 0) {
    const i = '  '.repeat(indent);
    // const fnName = `__tc_only__${sanitizeName(ownerName)}_${sanitizeName(methodPath.replace(/\W+/g, '_'))}`;
    const sorted = Array.from(new Set(counts)).sort((a, b) => a - b);

    lines.push(`${i}// 纯静态重载检查：${methodPath}（仅校验各参数个数是否可调用）`);
    // lines.push(`${i}function ${fnName}(f: typeof ${methodPath}) {`);
    lines.push(`${i}(f: typeof ${methodPath}) => {`);
    for (const n of sorted) {
      const args = n === 0 ? '' : Array.from({ length: n }, () => 'undefined as any').join(', ');
      lines.push(`${i}  void f(${args}); // 参数个数 = ${n}`);
    }
    lines.push(`${i}}`);
  }

  // 仅在生成器内部使用：根据参数个数生成“纯静态（TSC）重载校验”函数（实例方法）
  function emitInstanceOverloadCountChecks(lines, ctorName, methodName, counts, indent = 0) {
    const i = '  '.repeat(indent);
    const safeCtor = sanitizeName(ctorName);
    const safeMethod = sanitizeName(methodName);
    // const fnName = `__tc_only__${safeCtor}_inst_${safeMethod}`;
    const sorted = Array.from(new Set(counts)).sort((a, b) => a - b);

    lines.push(`${i}// 纯静态重载检查：实例方法 ${ctorName}.${methodName}（仅校验各参数个数是否可调用）`);
    // lines.push(`${i}function ${fnName}(inst: InstanceType<typeof ${ctorName}>) {`);
    lines.push(`${i}(inst: InstanceType<typeof ${ctorName}>) => {`);
    for (const n of sorted) {
      const args = n === 0 ? '' : Array.from({ length: n }, () => 'undefined as any').join(', ');
      lines.push(`${i}  void inst.${methodName}(${args}); // 参数个数 = ${n}`);
    }
    lines.push(`${i}}`);
  }

  function generateTypeAssertions(globalVars) {
    const lines = [
      '/**',
      ' * 运行时类型断言脚本',
    ' * ',
    ' * 此文件使用 TypeScript 的结构化类型系统来验证运行时对象',
    ' * 不检查精确类型，而是验证结构兼容性',
    ' * ',
    ' * 优势：',
    ' * - 利用 TS 的 "鸭子类型" 自动推断',
    ' * - 检查对象是否具有所需的属性和方法',
    ' * - 允许实际类型比声明类型更具体',
    ' * - 支持检查构造函数类型的静态属性和实例属性',
    ' * - 使用 InstanceType<typeof T> 推断实例类型，无需实际创建实例',
    ' * ',
    ' * 使用方法：',
    ' * 1. 在 JS 运行时环境中运行 generate-type-checks-v2.js',
    ' * 2. 将输出复制到此文件',
    ' * 3. 运行 tsc --noEmit 检查类型错误',
    ' */',
    '',
    '/* eslint-disable */',
    '// @ts-check',
    '',
  ];
  
  lines.push('// ==================== 类型定义 ====================');
  lines.push('');
  lines.push('/**');
  lines.push(' * ClearScript HostObject 类型');
  lines.push(' * 表示从 C# 传递到 JavaScript 的宿主对象');
  lines.push(' */');
  lines.push('type HostObject = any;');
  lines.push('');
  lines.push('/**');
  lines.push(' * ClearScript HostInvocable 类型');
  lines.push(' * 表示可以从 JavaScript 调用的宿主对象');
  lines.push(' */');
  lines.push('type HostInvocable = any;');
  lines.push('');
  lines.push('/**');
  lines.push(' * 未知类型');
  lines.push(' * 用于表示无法确定的类型');
  lines.push(' */');
  lines.push('type Unknown = any;');
  lines.push('');
  lines.push('// ==================== 类型断言辅助函数 ====================');
  lines.push('');
  lines.push('/**');
  lines.push(' * 类型守卫：验证对象具有指定的结构');
  lines.push(' * TypeScript 会自动检查 actual 是否可以赋值给 expected 的类型');
  lines.push(' */');
  lines.push('function assertStructure<T>(actual: T, expected: T): T {');
  lines.push('  return actual;');
  lines.push('}');
  lines.push('');
  lines.push('/**');
  lines.push(' * 验证属性存在性');
  lines.push(' */');
  lines.push('function assertHasProperty<T, K extends keyof T>(obj: T, key: K): T[K] {');
  lines.push('  return obj[key];');
  lines.push('}');
  lines.push('');
  lines.push('/**');
  lines.push(' * 验证方法可调用性（支持函数和构造函数）');
  lines.push(' */');
  lines.push('/* eslint-disable-next-line  @typescript-eslint/no-unsafe-function-type */');
  lines.push('function assertCallable<T extends Function>(fn: T): T {');
  lines.push('  return fn;');
  lines.push('}');
  lines.push('');
  
  lines.push('// ==================== 全局变量类型断言 ====================');
  lines.push('');
  
  for (const [name, info] of Object.entries(globalVars)) {
    const safeName = sanitizeName(name);
    
    const isConstructor = info.structure?.isConstructor;
    const hasInstanceProps = isConstructor && Object.keys(info.structure.instanceProperties || {}).length > 0;
    const hasInstanceMethods = isConstructor && Object.keys(info.structure.instanceMethods || {}).length > 0;
    
    lines.push(`// ${name} - ${info.typeInfo.kind}${isConstructor ? ' (Constructor)' : ''}`);
    lines.push(`if (typeof ${name} !== 'undefined') {`);
    
    if (info.typeInfo.kind === 'function') {
      // 函数：验证可调用性
      lines.push(`  // 验证 ${name} 是一个可调用的函数`);
      lines.push(`  assertCallable(${name});`);
      
      // 如果函数有静态属性，检查这些属性
      if (info.structure && Object.keys(info.structure.properties).length > 0) {
        lines.push(`  // ${name} 的静态属性`);
        for (const [propName, propInfo] of Object.entries(info.structure.properties)) {
          const camelPropName = toCamelCase(propName);
          const propPath = `${name}.${camelPropName}`;
          lines.push(`  if ('${camelPropName}' in ${name}) {`);
          lines.push(`    const _prop_${safeName}_${sanitizeName(camelPropName)}: ${propInfo.type} = ${propPath};`);
          lines.push(`  }`);
        }
      }
      
      // 如果函数有静态方法，检查这些方法
      if (info.structure && Object.keys(info.structure.methods).length > 0) {
        lines.push(`  // ${name} 的静态方法`);
        for (const [methodName, methodInfo] of Object.entries(info.structure.methods)) {
          const camelMethodName = toCamelCase(methodName);
          const methodPath = `${name}.${camelMethodName}`;
          const safeMethodName = sanitizeName(camelMethodName);
          
          lines.push(`  if ('${camelMethodName}' in ${name} && typeof ${methodPath} === 'function') {`);
          lines.push(`    assertCallable(${methodPath});`);
          
          // 纯静态（TSC）重载参数个数检查
          if (methodInfo.overloads && methodInfo.overloads.length > 0) {
            const counts = methodInfo.overloads.map(o => o.paramCount);
            emitStaticOverloadCountChecks(lines, name, methodPath, counts, 2);
          }
          
          lines.push(`  }`);
        }
      }
      
      // 如果是构造函数类型，生成实例属性检查
      if (hasInstanceProps || hasInstanceMethods) {
        lines.push(`  // ${name} 的实例属性和方法（使用 InstanceType 推断）`);
        lines.push(`  {`);
        lines.push(`    // 声明实例类型，但不实际创建实例`);
        lines.push(`    type _Instance_${safeName} = InstanceType<typeof ${name}>;`);
        lines.push(`    const _checkInstance_${safeName}: Partial<_Instance_${safeName}> = {} as any;`);
        lines.push(``);
        
        // 生成实例属性检查
        for (const [propName, propInfo] of Object.entries(info.structure.instanceProperties || {})) {
          const camelPropName = toCamelCase(propName);
          lines.push(`    // 检查实例属性 ${camelPropName}: ${propInfo.type}`);
          lines.push(`    if ('${camelPropName}' in _checkInstance_${safeName}) {`);
          lines.push(`      const _instProp_${safeName}_${sanitizeName(camelPropName)}: ${propInfo.type} = _checkInstance_${safeName}.${camelPropName}!;`);
          lines.push(`    }`);
        }
        
        // 生成实例方法检查
        for (const [methodName, methodInfo] of Object.entries(info.structure.instanceMethods || {})) {
          const camelMethodName = toCamelCase(methodName);
          const safeMethodName = sanitizeName(camelMethodName);
          
          lines.push(`    // 检查实例方法 ${camelMethodName}`);
          lines.push(`    if ('${camelMethodName}' in _checkInstance_${safeName} && typeof _checkInstance_${safeName}.${camelMethodName} === 'function') {`);
          lines.push(`      assertCallable(_checkInstance_${safeName}.${camelMethodName}!);`);
          
          // 纯静态（TSC）重载参数个数检查
          if (methodInfo.overloads && methodInfo.overloads.length > 0) {
            const counts = methodInfo.overloads.map(o => o.paramCount);
            emitInstanceOverloadCountChecks(lines, name, camelMethodName, counts, 3);
          }
          
          lines.push(`    }`);
        }
        
        lines.push(`  }`);
      }
      
    } else if (info.typeInfo.kind === 'object' || info.typeInfo.kind === 'instance') {
      // 对象：验证结构
      if (info.structure) {
        generateObjectAssertions(lines, name, info.structure, 1);
      } else {
        lines.push(`  const _var_${safeName}: object = ${name};`);
      }
      
    } else {
      // 原始类型：直接验证类型
      lines.push(`  const _var_${safeName}: ${info.typeInfo.tsType} = ${name};`);
    }
    
    lines.push(`}`);
    lines.push('');
  }
  
  lines.push('// ==================== 导出 ====================');
  lines.push('');
  lines.push('export {};');
  lines.push('');
  lines.push('/**');
  lines.push(' * 类型检查完成！');
  lines.push(' * ');
  lines.push(' * 如果看到类型错误，说明：');
  lines.push(' * 1. 运行时对象缺少类型定义中声明的属性');
  lines.push(' * 2. 运行时对象的属性类型与类型定义不兼容');
  lines.push(' * 3. 类型定义文件(bgi.d.ts)需要更新');
  lines.push(' */');
  
    return lines.join('\n');
  }

  /**
   * 生成对象的属性断言
   */
  function generateObjectAssertions(lines, objPath, structure, indent) {
    const indentStr = '  '.repeat(indent);
    
    // 生成属性断言
    for (const [propName, propInfo] of Object.entries(structure.properties)) {
      const camelPropName = toCamelCase(propName); // 首字母小写
      const propPath = `${objPath}.${camelPropName}`;
      const safePropName = sanitizeName(camelPropName);
      
      lines.push(`${indentStr}// ${propPath}: ${propInfo.type}`);
      lines.push(`${indentStr}if ('${camelPropName}' in ${objPath}) {`);
      
      if (propInfo.nested) {
        // 嵌套对象：递归生成断言
        lines.push(`${indentStr}  const _nested_${sanitizeName(objPath)}_${safePropName} = ${propPath};`);
        generateObjectAssertions(lines, propPath, propInfo.nested, indent + 1);
      } else {
        // 简单属性：直接断言类型
        lines.push(`${indentStr}  const _prop_${sanitizeName(objPath)}_${safePropName}: ${propInfo.type} = ${propPath};`);
      }
      
      lines.push(`${indentStr}}`);
    }
    
    // 生成方法断言（仅校验可调用性，不再生成重载检查）
    for (const [methodName, methodInfo] of Object.entries(structure.methods)) {
      const camelMethodName = toCamelCase(methodName); // 首字母小写
      const methodPath = `${objPath}.${camelMethodName}`;
      const safeMethodName = sanitizeName(camelMethodName);
      
      lines.push(`${indentStr}// ${methodPath}: Function`);
      lines.push(`${indentStr}if ('${camelMethodName}' in ${objPath} && typeof ${methodPath} === 'function') {`);
      lines.push(`${indentStr}  assertCallable(${methodPath});`);
      lines.push(`${indentStr}}`);
    }
  }

  /**
   * 清理名称用于变量命名
   */
  function sanitizeName(name) {
    return name.replace(/[^a-zA-Z0-9_]/g, '_');
  }

  /**
   * 将属性名首字母转换为小写
   */
  function toCamelCase(name) {
    if (!name || name.length === 0) return name;
    return name.charAt(0).toLowerCase() + name.slice(1);
  }

  // ==================== 主函数 ====================

  /**
   * 收集全局变量信息
   */
  function collectGlobalVariables() {
    const globalObj = (typeof globalThis !== 'undefined') ? globalThis : 
                      (typeof window !== 'undefined') ? window :
                      (typeof global !== 'undefined') ? global : this;
    
    const result = {};
    const visited = new WeakSet();
    
    const globalKeys = Object.getOwnPropertyNames(globalObj);
    
    for (const key of globalKeys) {
      if (CONFIG.excludeGlobals.includes(key)) continue;
      
      try {
        const value = globalObj[key];
        const typeInfo = getDetailedType(value);
        
        let structure = null;
        if (typeInfo.kind === 'object' || typeInfo.kind === 'instance' || typeInfo.kind === 'function') {
          structure = analyzeObject(value, visited);
        }
        
        result[key] = {
          typeInfo,
          structure,
        };
      } catch (e) {
        // 跳过无法访问的属性
        continue;
      }
    }
    
    return result;
  }

  /**
   * 主函数
   */
  function main() {
    log.info('🔍 开始分析运行时环境...\n');
    
    const globalVars = collectGlobalVariables();
    const count = Object.keys(globalVars).length;
    
    log.info(`✓ 发现 ${count} 个全局变量\n`);
    
    // 打印摘要
    log.info('变量摘要:');
    for (const [name, info] of Object.entries(globalVars)) {
      const propCount = info.structure ? Object.keys(info.structure.properties).length : 0;
      const methodCount = info.structure ? Object.keys(info.structure.methods).length : 0;
      const instPropCount = info.structure ? Object.keys(info.structure.instanceProperties || {}).length : 0;
      const instMethodCount = info.structure ? Object.keys(info.structure.instanceMethods || {}).length : 0;
      const isConstructor = info.structure?.isConstructor;
      
      let summary = `  - ${name} [${info.typeInfo.kind}]`;
      if (propCount > 0 || methodCount > 0) {
        summary += ` (static: ${propCount} props, ${methodCount} methods)`;
      }
      if (isConstructor && (instPropCount > 0 || instMethodCount > 0)) {
        summary += ` (instance: ${instPropCount} props, ${instMethodCount} methods)`;
      }
      log.info(summary);
    }
    
    log.info('\n📝 生成类型断言代码...\n');
    
    const code = generateTypeAssertions(globalVars);
    
    log.info('✓ 代码生成完成！\n');
    log.info(`输出文件: ${CONFIG.outputFile}\n`);
    
    return code;
  }

  // ==================== 执行 ====================

  if (typeof module !== 'undefined' && module.exports) {
    module.exports = { main, collectGlobalVariables, generateTypeAssertions };
  }

  // 自动执行
  if (typeof window !== 'undefined' || typeof globalThis !== 'undefined') {
    const code = main();
    
    // 尝试保存文件（Node.js环境）
    if (typeof require !== 'undefined') {
      try {
        const fs = require('fs');
        const path = require('path');
        const outputPath = path.join(__dirname, CONFIG.outputFile);
        fs.writeFileSync(outputPath, code, 'utf8');
        log.info(`✓ 文件已保存到: ${outputPath}\n`);
      } catch (e) {
        log.info('⚠ 无法自动保存文件，请手动复制下面的代码：\n');
        log.info('='.repeat(60));
        log.info(code);
        log.info('='.repeat(60));
      }
    } else {
      log.info('='.repeat(60));
      log.info(code);
      log.info('='.repeat(60));
    }
  }
})(); // IIFE 结束
