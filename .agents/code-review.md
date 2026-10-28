# 代码审查规则

- 结果使用简体中文；类名、方法名、变量名、路径、代码片段和错误消息保持原文。专业术语首次出现时可用“中文（English）”。
- 优先检查崩溃、死锁、资源泄漏和数据竞争，重点关注：
  - 异步任务、`CancellationToken`、线程切换和 STA UI 线程。
  - `Mat`、`Bitmap`、`Stream`、`CancellationTokenSource` 等 `IDisposable` 对象的释放。
  - 游戏输入、截图和多实例场景中的共享状态冲突。
- 不提出纯风格问题，除非影响正确性、可维护性或性能。
- 每个问题包含：严重程度、问题位置、问题原因、可能造成的影响、推荐修复方案。
- 检查是否绕过基础设施服务（规则见 `Docs/design/infrastructure-foundation.md` 第 14 节）：
  - 临时目录与临时文件使用 `ITempFileService`，不用 `Path.GetTempPath()`，不自己拼 `User/Temp` 或在业务目录旁建临时目录。
  - 可再生的图片和接口数据使用 `IFileCache` 或 `IRemoteImageService`，并在 `CachePartitions` 新增分区；不新建缓存目录，不用静态字典无上限地保存图片。
  - HTTP 请求使用 `IHttpClientFactory.CreateClient(HttpClientNames.X)`，不 `new HttpClient()`，不用静态 HttpClient 字段。
  - 较大的文件或需要进度的下载使用 `IDownloadService`，并在 `DownloadCategories` 新增分类；不自己写下载循环。
  - 网页使用 `WebpagePanel` 或 `WebViewSetup.InitializeAsync` 加 `WebViewProfiles`；不直接调用 `CoreWebView2Environment.CreateAsync`，不自定数据目录，浏览器参数只写在 `WebViewEnvironments` 中。
  - 新的会占用磁盘的目录实现 `IStorageAreaProvider` 登记；废弃旧目录时，在同一版本把它加入 `LegacyDataCleaner` 的清单，不写迁移代码。
