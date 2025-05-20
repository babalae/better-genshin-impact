## 使用单元测试前准备

### 准备Assets

Assets目录下面是一个子模块（submodule）  
为了避免不关注测试的用户下载不必要的数据，子模块没有保持最新，因此为了进行测试，要手动下载Assets目录下的最新数据资源：  
`git submodule update --remote`  
可能需要先init：  
`git submodule init`

子模块被定义在 [.gitmodules](../../.gitmodules) 文件中，因此上述git命令也应在该文件所在目录执行

Assets项目地址：[https://github.com/huiyadanli/BetterGI.UnitTest.Assets](https://github.com/huiyadanli/BetterGI.UnitTest.Assets)


### 准备配置文件

有的单元测试要读取配置，目前采取读取主项目BetterGenshinImpact编译环境相同配置的方式  
因此须要编译运行一次主项目BetterGenshinImpact，使得User/config.json被创建出来