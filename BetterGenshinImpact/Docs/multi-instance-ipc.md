# BetterGI 多实例命名管道协议

## 目标

每个 BetterGI 进程只公开一个双向命名管道端点。管道名称不包含 `childSession` 或
`webview` 等实例类型；实例类型属于进程运行时元数据，通过注册消息传递。

当前协议版本为 `v1`，用于：

- 普通重复启动时，把激活参数转发给当前 Windows 会话中的主实例；
- 建立 Primary、ChildSession、WebView 的父子关系；
- 查询实例树和维持父子连接；
- 在 Primary 与 ChildSession 之间转发相对鼠标移动。

独立任务的选择面板、任务下发和状态回传不在当前实现范围内。代码中仅保留
`task.*` 协议入口的 TODO 注释。

## 管道名称与可见性

命名格式：

```text
BetterGI.v1.session-<WindowsSessionId>
BetterGI.v1.instance-<32位小写InstanceId>
```

- 普通 Primary 使用当前 Windows 会话的固定名称
  `BetterGI.v1.session-<WindowsSessionId>`。该名称已存在时，新进程把激活参数发送给
  已运行进程并退出。
- 显式 `--instance childSession` 优先使用其所在 Windows 会话的固定名称；名称冲突时
  改用自身 InstanceId 对应的名称。
- 显式 `--instance webview` 使用自身 InstanceId 对应的名称。
- `session` 与 `instance` 只表示端点的发现方式，不表示实例类型。

管道名称不使用 `Global\` 前缀，因此保持 Windows 会话级隔离。管道 ACL 仅允许
创建管道的当前登录用户 SID 完全控制，并显式拒绝 `Network` SID；同一用户在
Primary 与 ChildSession 中启动的 BetterGI 可以正常通信。协议不使用密钥、令牌或
可执行文件路径认证；服务端仍会检查操作类型、父子层级和实例状态。

子实例通过启动参数取得父端点：

```text
--instance <childSession|webview>
--instance-id <guid>
--parent-instance <guid>
--parent-pipe <pipe-name>
```

`--profile` 保持独立，本协议不解释也不修改配置文件参数。

## 层级规则

```text
Primary A
├─ ChildSession B（最多一个）
│  └─ WebView C（可多个）
└─ WebView C（可多个）
```

- Primary 可以创建一个 ChildSession 和多个 WebView。
- ChildSession 可以创建多个 WebView。
- WebView 不允许创建任何子实例。
- 父进程为子进程预分配随机 InstanceId，并在持久双向连接上记录父子关系。
- 子实例断开时，父实例移除对应关系；子实例会重连父管道并重新注册。

## 帧格式

同一条连接同时承载 JSON 控制消息和二进制相对鼠标批次。整数使用小端序。

```text
uint32 payloadLength
byte   payloadType
byte[] payload
```

`payloadLength` 最大为 1 MiB。

| payloadType | 内容 |
| --- | --- |
| `1` | UTF-8 JSON 控制消息 |
| `2` | 相对鼠标二进制批次 |

JSON 信封的主要字段：

```json
{
  "version": 1,
  "requestId": "00000000-0000-0000-0000-000000000000",
  "operation": "ping",
  "sourceInstanceId": "00000000-0000-0000-0000-000000000000",
  "success": true,
  "errorCode": null,
  "errorMessage": null,
  "data": {}
}
```

响应使用相同 `requestId`，并将 `operation` 设为 `response`。

当前控制操作：

| 操作 | 用途 |
| --- | --- |
| `ping` | 检查端点并读取实例描述 |
| `activation.forward` | 转发二次启动参数 |
| `instance.register` | 子实例注册 |
| `instance.unregister` | 子实例主动注销 |
| `instance.heartbeat` | 父子连接心跳 |
| `instance.getTree` | 递归读取实例树 |
| `input.relativeMouse.subscribe` | ChildSession 请求父实例开始转发 |
| `input.relativeMouse.unsubscribe` | ChildSession 停止转发 |
| `input.relativeMouse.state` | 预留的相对鼠标状态通知名称 |

相对鼠标批次结构：

```text
uint16 sampleCount            // 1..64
uint64 firstSequence
int64  baseUtcTicks
repeat sampleCount:
    int32 deltaX
    int32 deltaY
    int32 timestampOffsetUs
```

发送端最多等待 5 ms 或累计 64 个样本后发送。队列拥塞时将后续位移合并，避免阻塞
Raw Input 采集线程。Primary 仅在桌面分身窗口可见、RDP 已连接且
`Input Capture Window` 具有键盘焦点时转发。

## 激活可靠性

普通重复启动会使用同一个 `requestId` 进行有限次数重试（0、200、500 ms 退避），
只有收到成功响应后才按成功退出。已运行实例缓存近期激活响应，避免重试导致
`bettergi://start` 被执行多次。这覆盖了 PR #3304 所描述的“窗口已唤醒但截图器未启动”
场景。
