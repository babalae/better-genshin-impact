# BetterGI 外部访问服务设计

## 目标与边界

外部访问服务为本机程序和 BetterGI 内部 WebView2 页面提供统一的 HTTP、WebSocket 与 MCP 入口。首个版本只公开健康状态、版本信息和实时日志，不提供任务启动、停止等业务控制能力。

服务只属于 Primary 实例，只监听 loopback 地址。HTTP、WebSocket 和 MCP 共用一个端口，默认端口为 `30648`。三项功能默认关闭；全部关闭或当前进程不是 Primary 实例时，不创建监听器。端口被占用或宿主启动失败只会更新设置页状态并写入日志，不影响 WPF 主程序继续运行。

## 分层

外部访问实现分为四层：

1. `IBgiExternalCapabilityService` 定义与传输无关的 BetterGI 能力，并返回共享 DTO。
2. HTTP 与 MCP 适配层把各自的请求转换为能力服务调用，不重复实现业务逻辑。
3. `ExternalLogHub`、`ExternalLogSink` 和 `ExternalLogWebSocketHandler` 负责日志采集、回放与实时传输。
4. `ExternalAccessHost` 管理独立 `WebApplication` 的生命周期、loopback 监听、路由与中间件。

## 配置与生命周期

配置位于 `AllConfig.ExternalAccessConfig`，并继续由现有配置服务保存到 `User/config.json`：

| 配置项 | 默认值 | 生效时机 |
|---|---:|---|
| `HttpApiEnabled` | `false` | 重启 BetterGI 后 |
| `WebSocketEnabled` | `false` | 重启 BetterGI 后 |
| `McpEnabled` | `false` | 重启 BetterGI 后 |
| `Port` | `30648` | 重启 BetterGI 后 |
| `AccessToken` | 空字符串 | 立即生效 |

HTTP 或 MCP 启用且令牌为空时，服务会生成 256-bit 随机值，转换为无填充的 Base64Url 字符串并立即保存。设置页只显示脱敏值，提供复制和重新生成操作；重新生成后旧令牌立即失效。BetterGI 不向 WebView2 页面自动注入令牌，调用页面必须自行提供。

## HTTP API

HTTP JSON 使用 `System.Text.Json` 和 camelCase 属性名。未启用 HTTP 时不映射下列路由，因此返回 `404`。

| 方法与路径 | 能力 | 返回模型 |
|---|---|---|
| `GET /api/v1/health` | 查询进程健康状态 | `status`、`instanceRole`、`processId`、`startedAtUtc`、`uptimeSeconds` |
| `GET /api/v1/version` | 查询版本 | `product`、`version`、`apiVersion` |

示例请求：

```http
GET /api/v1/health HTTP/1.1
Host: 127.0.0.1:30648
Authorization: Bearer <token>
```

## MCP

MCP 使用 `ModelContextProtocol.AspNetCore` 的无状态 Streamable HTTP 传输，挂载在 `/mcp`，不提供旧式 SSE 端点。未启用 MCP 时不映射该路由。

| Tool | 共享能力与返回模型 |
|---|---|
| `get_bgi_health` | 与 `GET /api/v1/health` 相同 |
| `get_bgi_version` | 与 `GET /api/v1/version` 相同 |

## 鉴权、CORS 与本机边界

- HTTP API 与 MCP 要求 `Authorization: Bearer <token>`，缺失或错误时返回 `401`。
- 令牌通过固定时间比较校验，服务日志不得记录令牌。
- CORS 允许任意 Origin、Header 和 Method，不允许 Cookie 或 credentials；预检 `OPTIONS` 不要求鉴权。
- WebSocket 日志按产品约定不鉴权并允许任意 Origin。设置页会明确提示日志可能泄露路径、脚本名、异常堆栈等信息。
- Kestrel 只监听 loopback；额外的 Host 校验只接受 `localhost` 或 loopback IP，降低 DNS rebinding 风险。

## WebSocket 日志协议

连接地址为：

```text
ws://127.0.0.1:30648/ws/v1/logs?minimumLevel=Information
```

`minimumLevel` 可省略，默认值为 `Information`，并接受 Serilog 日志级别名称。值无效时握手请求返回 `400`。

连接建立后先按原顺序回放最近 200 条满足最低级别的日志，再持续推送实时日志。每个客户端拥有容量 512 的独立有界队列；队列满时丢弃最旧消息，不阻塞日志与业务线程，并在下一次发送前推送累计丢弃通知。

日志消息字段如下：

| 字段 | 含义 |
|---|---|
| `sequence` | 进程内递增序号 |
| `timestampUtc` | UTC 时间戳 |
| `level` | 日志级别 |
| `source` | 日志来源 |
| `message` | 已渲染消息 |
| `exception` | 异常文本；没有异常时为空 |
| `instance` | BetterGI 实例标识 |

丢弃通知为独立 JSON 消息，包含 `type: "dropped"` 与 `count`。服务支持多个并发客户端，并在客户端断开或应用退出时取消订阅并释放连接资源。
