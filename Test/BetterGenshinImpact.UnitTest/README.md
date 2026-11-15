## 使用单元测试前准备

### 准备Assets

`Assets` 是测试资源子模块，测试前需在仓库根目录下载主仓库记录的资源版本：

```powershell
git submodule update --init -- Test/BetterGenshinImpact.UnitTest/Assets
```

日常运行测试不要使用 `--remote`，以免资源版本与测试代码不一致。新增或更新资源时，先在资源仓库提交，再在主仓库更新子模块指针；共享主仓库提交前须先推送对应的资源提交。

子模块被定义在 [.gitmodules](../../.gitmodules) 文件中，因此上述git命令也应在该文件所在目录执行

Assets项目地址：[https://github.com/huiyadanli/BetterGI.UnitTest.Assets](https://github.com/huiyadanli/BetterGI.UnitTest.Assets)

### 地图遮罩回归测试

定位组件职责、上下游依赖与资源生命周期见[地图遮罩模块说明](../../Docs/technical/map-mask.md)。

真实截图、坐标 JSON 和[详细样本说明](Assets/MapMask/README.md)统一放在资源子模块 `Assets/MapMask`。编译时截图和 JSON 会复制到测试输出目录的 `TestData/MapMask`，测试从 `AppContext.BaseDirectory` 加载，不依赖个人截图目录或当前工作目录。缺少资源会明确失败；初始化子模块后需重新编译。

```powershell
dotnet test Test/BetterGenshinImpact.UnitTest/BetterGenshinImpact.UnitTest.csproj --filter 'FullyQualifiedName~MapMaskTests'
```
