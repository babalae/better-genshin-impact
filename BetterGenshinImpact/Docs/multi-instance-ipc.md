# BetterGI 多实例命名管道协议

## 设计目标

协议不再使用 InstanceId，也不要求根实例保存“由谁启动了谁”的进程树。实例关系只由
当前 Windows 用户、Windows Session、进程用途和进程 ID 决定。

当前协议版本为 `v2`，规则如下：

- 每个 Windows 用户最多有一个根 BetterGI；
- 根 BetterGI 是该用户固定根管道的持有者，通常运行在主桌面 Session；
- 与根不在同一个 Session 的 BetterGI 是桌面分身 BetterGI；
- WebView 无论在哪个 Session 启动，都是根 BetterGI 的直接客户端；
- 桌面分身 BetterGI 只能查询和访问相同 Session 中的 WebView；
- 同一个 Session 只允许一个 BetterGI，WebView 则允许多个并通过进程 ID 精确寻址。

独立任务的选择面板、任务下发和状态回传不在当前实现范围内。

## 管道和 Windows 用户隔离

每个 Windows 用户只有一个固定根管道：

```text
BetterGI.v2.user-<Windows用户SID>.root
```

命名管道名称在整台计算机上可见，而不是按 Windows Session 隔离。因此根 BetterGI、
桌面分身 BetterGI 和 WebView 即使运行在不同 Session，也能连接同一个固定端点。
管道 ACL 只允许当前 Windows 用户 SID 完全控制，并显式拒绝 `Network` SID，所以
不同 Windows 用户分别拥有自己的根 BetterGI，彼此不会注册到对方的实例组中。

协议内容不敏感，不使用密钥、令牌、InstanceId 或父实例 ID。根端从已连接的命名管道
句柄调用 Windows API 获取客户端真实进程 ID，再由进程 ID取得真实 Windows Session；
客户端提交的用途只用于区分 BetterGI 与 WebView，不能伪造进程或 Session 身份。

## 启动参数

客户端用途仅使用：

```text
--instance <childSession|webview>
```

- `--instance childSession` 表示该进程只能作为桌面分身客户端，根暂时不存在时也不会
  自行成为根；
- `--instance webview` 表示该进程只能作为 WebView 客户端；
- 不带 `--instance` 的 BetterGI 会先竞争固定根管道。竞争成功即成为根；竞争失败则
  连接已有根，由根根据真实 Session 决定是转发重复启动还是注册为桌面分身。

应用自身重启时额外传递：

```text
--restart-from-pid <旧进程ID>
```

新进程会等待旧进程退出，再竞争根管道或替换同 Session 中的旧客户端连接。该参数只用于
消除重启交接期间的竞态，不作为长期身份。

## 连接拓扑和访问规则

```text
某个 Windows 用户的根 BetterGI
├─ Session 2 的桌面分身 BetterGI（最多一个）
├─ Session 3 的桌面分身 BetterGI（最多一个）
├─ Session 1 的 WebView（可多个，以进程 ID 区分）
├─ Session 2 的 WebView（可多个，以进程 ID 区分）
└─ Session 3 的 WebView（可多个，以进程 ID 区分）
```

所有客户端都直接连接根管道，不在桌面分身下面建立二级管道。这样根重启后只需恢复一个
固定端点，客户端也只需重连一个固定名称。

WebView 访问规则：

- 根 BetterGI 可以查询和访问全部 WebView；
- 桌面分身 BetterGI 只能查询与自己 Windows Session 相同的 WebView；
- 单播通过 WebView 进程 ID 精确定位；
- 组播由调用方先查询可见 WebView，再逐个发送，不在协议中维护易失的实例组。

## 连接建立和重复启动

客户端连接固定根管道后，首先发送 `connection.open`。根根据管道连接的真实进程 ID 和
Session 处理：

1. `webview`：按进程 ID 注册，可在断线后替换旧连接。
2. BetterGI 与根位于相同 Session：视为重复启动，把业务启动参数转发给根窗口，然后
   新进程退出。
3. BetterGI 与根位于不同 Session：注册为桌面分身。
4. 同一 Session 已有桌面分身：把业务启动参数转发给已有进程，然后新进程退出。
5. 旧连接已经失效，或 `--restart-from-pid` 指向该 Session 中被替换的进程：接受新连接。

## 断线、闪退和退出语义

桌面分身 BetterGI 与 WebView 在根管道断开后，每秒尝试重新连接固定根管道，且没有总
超时时间。因此根 BetterGI 闪退、被结束进程或隔很久后才重新启动，都不改变客户端的
发现地址；新根取得固定管道后，仍存活的客户端会自动重新注册。

正常关闭根 BetterGI 时，现有 `ChildSessionService.Dispose` 流程继续主动断开 RDP 并
注销桌面分身 Session。只有未执行正常释放流程的闪退场景，桌面分身才会留存并等待根
恢复。

连接断开即移除对应运行时记录，不把客户端清单持久化。根重启后的关系由仍存活的客户端
重新连接重建，避免把已经退出的 PID 当作在线实例。

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
  "version": 2,
  "requestId": "00000000-0000-0000-0000-000000000000",
  "operation": "ping",
  "success": true,
  "errorCode": null,
  "errorMessage": null,
  "data": {}
}
```

响应使用相同 `requestId`，并将 `operation` 设为 `response`。信封中不再传输
`sourceInstanceId`。

当前控制操作：

| 操作 | 用途 |
| --- | --- |
| `ping` | 检查连接并读取端点描述 |
| `connection.open` | 客户端声明用途，根按真实 PID/Session 注册或转发激活 |
| `activation.dispatch` | 根向已有 BetterGI 转发二次启动参数 |
| `input.relativeMouse.subscribe` | 桌面分身请求根开始转发相对鼠标 |
| `input.relativeMouse.unsubscribe` | 桌面分身停止转发相对鼠标 |
| `input.relativeMouse.state` | 预留的相对鼠标状态通知名称 |
| `webview.list` | 查询当前调用方可见的 WebView |
| `webview.send` | 根按目标进程 ID 向 WebView 单播 |
| `webview.message` | 根向目标 WebView 下发消息 |

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
Raw Input 采集线程。根 BetterGI 仅在游戏鼠标模式已启用、桌面分身窗口可见、RDP 已连接且
`Input Capture Window` 具有键盘焦点时转发。
