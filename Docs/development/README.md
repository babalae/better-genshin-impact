# 开发者文档

本目录存放面向贡献者的开发文档，包括编译、调试、代码规范与协作流程。

## 发行包专属资源（本地构建会缺的东西）

有一部分体积较大的资源**不随源码仓库分发**，源码仓库与 `BetterGI.Assets.*` NuGet 资源包里都没有，
只在完整发行包里（见 [Build/setup_build.cmd](../../Build/setup_build.cmd) 中
「添加一些配置文件开始 / 大文件不适合放在 Github」那一步）。

目前已知的这类资源：

| 相对程序目录的路径 | 用途 | 缺失时的表现 |
| --- | --- | --- |
| `Assets\Web\ScriptRepo\index.html` | 「脚本仓库 → 打开仓库」的网页界面（WebView2） | 打开后只有一个白屏 |

因此，**从 Visual Studio 调试运行、或直接用 `dotnet publish` 的产物运行时，这些功能会缺资源**。

补齐方式（二选一）：

1. 把发行包里对应的目录直接复制到程序目录（例如 `BetterGenshinImpact\bin\x64\Debug\net8.0-windows10.0.22621.0\`）；
2. 让构建自动补齐：在本机未被版本控制的 `BetterGenshinImpact\BetterGenshinImpact.csproj.user` 里指定
   本机已有的完整发行包目录，构建与 `publish` 时会自动把 `Assets\Web` 复制到输出目录：

```xml
<PropertyGroup>
  <BetterGiReleaseAssetsDir>D:\BetterGI</BetterGiReleaseAssetsDir>
</PropertyGroup>
```

未指定该属性（或该目录下没有 `Assets\Web`）时，构建会照常跳过这一步并在输出里给出提示，不影响编译。
