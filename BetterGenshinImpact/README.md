## 项目结构

参考：[AI生成的项目结构说明](ProjectStructure.md)

## 如何编译并运行整个工程？

### Rider (推荐)
1. `git clone https://github.com/babalae/better-genshin-impact.git`
2. 推荐使用 [Rider](https://www.jetbrains.com/zh-cn/rider/) 打开本项目。速度快且免费！

### Visual Studio 2022
1. `git clone https://github.com/babalae/better-genshin-impact.git`
2. 需要使用 [Visual Studio 2022](https://visualstudio.microsoft.com/zh-hans/downloads/) 打开本项目。

请注意当前 `/Asset` 目录下的部分文件过大，比如地图特征数据（300M+），需要手动从 Release 包中获取并拷贝至对应的编译目录下，软件才能够正常运行对应的功能。（当前仅影响地图追踪、自动传送相关功能）

### 运行项目闪退？

可能是 Windows SDK 版本不够。使用 Visual Studio Installer 安装 `10.0.22621.0` 及以上版本的 Windows SDK，或者编辑项目文件的 `TargetFramework` 降低版本。