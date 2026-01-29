## 项目结构

参考：[AI生成的项目结构说明](ProjectStructure.md)

## 如何编译并运行整个工程？

### Rider (推荐)
1. `git clone https://github.com/babalae/better-genshin-impact.git`
2. 推荐使用 [Rider](https://www.jetbrains.com/zh-cn/rider/) 打开本项目。速度快且免费！

### Visual Studio 2022
1. `git clone https://github.com/babalae/better-genshin-impact.git`
2. 需要使用 [Visual Studio 2022](https://visualstudio.microsoft.com/zh-hans/downloads/) 打开本项目。

~~请注意当前 `/Asset` 目录下的部分文件过大，比如地图特征数据（300M+），需要手动从 Release 包中获取并拷贝至对应的编译目录下，软件才能够正常运行对应的功能。（当前仅影响地图追踪、自动传送相关功能）~~  现在已经丢到 nuget 上可以直接编译构建了

## 如何通过 Github Action 直接构建完整包？

首先 fork 这个项目并启用 GitHub Actions。然后提交 `<commit>` 并且推送到 `<branch>` 。

最后前往这里 `https://github.com/<your-user-name>/better-genshin-impact/actions/workflows/publish.yml` 并点击 `Run workflow`:

- `Use workflow from` 选择 `<branch>`
- `BetterGI Version` 填写 `<current-version>+<commit hash>`
- `Kachina Installer Channel` 选择 `release` （无需修改）
- 不要勾选 `创建 GitHub Release 草稿`

点击绿色按钮 `Run workflow`，等待约10分钟。

刷新当前页面，点击最新的 Run，`Artifacts` 中的 `BetterGI_7z` 就是构建的完整包。


## 运行项目闪退？

可能是 Windows SDK 版本不够。使用 Visual Studio Installer 安装 `10.0.22621.0` 及以上版本的 Windows SDK，或者编辑项目文件的 `TargetFramework` 降低版本。
