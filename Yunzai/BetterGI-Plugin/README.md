# Yunzai BetterGI Plugin

通过 WebSocket 将 Yunzai 的 QQ 命令转发给 BetterGI，并使用 Yunzai Puppeteer 把控制反馈与 QQ 通知渲染为图片。

## 安装

将 `BetterGI-Plugin` 复制到 Yunzai 的 `plugins/` 目录。插件加载时会从 Yunzai 根目录的 `package.json` 读取 Yunzai 版本，并在日志中提示连接状态。

选择一种连接模式，在 `config/default.json` 中填写 `mode`、`host`、`port` 和 `token`，两端令牌和端口保持一致：

- `mode: "client"`：插件加载时不建立连接；收到控制命令时连接 BetterGI，并复用连接处理后续命令，直到连接断开。`host` 填 BetterGI 可达的 IPv4 地址，BetterGI 设置页填写监听 IP。
- `mode: "server"`：插件加载时立即启动 WebSocket 服务端；BetterGI 启动后主动连接并保持长连接，断开后自动重连。BetterGI 设置页启用“反向 WebSocket”，并填写 Yunzai 服务端 IP。Yunzai 的 `host` 可设为 `0.0.0.0` 监听所有 IPv4 网卡。

反向模式下，BetterGI 主程序和无头 Worker 会分别连接 Yunzai。QQ 命令由主程序处理；Worker 日志沿用主页“使用通知渠道”的聚合间隔和通知事件设置。BetterGI 连接期间，QQ 通知由 Yunzai 的 Puppeteer 网页模板渲染为图片并发往 QQ 私聊/群 OpenID；连接不可用时退回 BetterGI 原有 QQ 通知发送方式。

事件中的 `e.user_id` 会作为 QQ OpenID 发送，必须加入 BetterGI 远程命令授权列表。

## 命令

- `#执行调度器 配置组名称`（兼容 `#启动任务 配置组名称`）
- `#一条龙`
- `#启动截图器`、`#关闭截图器`
- `#暂停任务`、`#继续任务`、`#停止任务`
- `#退出游戏`

## 连接协议

插件在直连模式下根据 `host` 和 `port` 连接 `ws://<BetterGI-IP>:<port>/`，在握手头中发送 `Authorization: Bearer <token>`。控制请求发送 `{"message":"#一条龙","userOpenId":"QQ OpenID"}`；服务端响应包含 `message`、`logs`、`screenshotBase64` 和 `betterGiVersion`。反向模式使用同一鉴权头，BetterGI 主动连接 Yunzai 并维持长连接。

图片模板位于 `resources/control.html`，使用 Yunzai 的 `e.runtime.render()` 和 Puppeteer 生成图片。
