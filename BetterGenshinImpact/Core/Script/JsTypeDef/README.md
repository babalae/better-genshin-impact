# @types/bettergi

BGI的Typescript类型声明，解决脚本补全问题

注意在更改JS导出类型后，需要同时更新此处的文件

## 生成测试ts

在bgi中运行 checker/generate-type-checks.js ，脚本会遍历当前环境内全部的JS变量，并遍历对象的静态property和实例property，输出到日志中

将输出的type测试脚本保存到bettergi-tests.ts文件中，即可检查d.ts文件是否完整且正确。

目前由于脚本不支持嵌套类型，需要手动将下面的一段注释掉
```
    // AutoFightParam 的静态方法
    if ('fightFinishDetectConfig' in AutoFightParam && typeof AutoFightParam.fightFinishDetectConfig === 'function') {
      assertCallable(AutoFightParam.fightFinishDetectConfig);
    }
```

## DefinitelyTyped提交方法

- 生成测试ts，并保存到bettergi-tests.ts，确保tsc没有报错
- 运行npm run build，确保成功运行
- 将dist/bettergi.d.ts和bettergi-tests.ts复制到DefinitelyTyped目录提交即可
