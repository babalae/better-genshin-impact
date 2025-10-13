# @types/bettergi

BGI的Typescript类型声明，解决脚本补全问题

注意在更改JS导出类型后，需要同时更新此处的文件

## 对偶测试

在bgi中运行 checker/generate-type-checks.js ，脚本会遍历当前环境内全部的JS变量，并遍历对象的静态property和实例property。

将输出的type测试脚本保存到ts文件中，即可检查d.ts文件是否完整且正确。