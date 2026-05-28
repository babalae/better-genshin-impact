#!/usr/bin/env python3
"""
BetterGI 截图守护进程，基于 Wayland PipeWire

通过 D-Bus 与 xdg-desktop-portal 通信，获取屏幕/窗口的 PipeWire
视频流，将 BGR 原始帧写入 /dev/shm 共享内存，供 Wine 下的
BetterGI WPF 应用读取。

依赖于 python3-dbus 和 python3-gi
"""
import sys, os, signal, struct, atexit, traceback
from pathlib import Path

loop = None

SHM_DIR   = Path("/dev/shm/bettergi")
FRAME_BIN = SHM_DIR / "frame.bin"
FRAME_TMP = SHM_DIR / "frame.bin.tmp"
PID_FILE  = SHM_DIR / "daemon.pid"

pipeline = None
# 请求路径 -> 回调 映射表，用于将 portal 的异步响应关联到对应的请求
pending = {}
session = None # ScreenCast 会话对象

SHM_DIR.mkdir(parents=True, exist_ok=True)
PID_FILE.write_text(str(os.getpid()))

def cleanup():
    if pipeline is not None:
        pipeline.set_state(Gst.State.NULL)
    print("python 守护进程退出", file=sys.stderr)

atexit.register(cleanup)

def die(msg=None, code=1):
    if msg is not None:
        print(f"[daemon] {msg}", file=sys.stderr)
    if loop is not None:
        loop.quit()
    else:
        sys.exit(code)

try:
    from dbus.mainloop.glib import DBusGMainLoop
    DBusGMainLoop(set_as_default=True)
    import dbus
    import gi
    gi.require_version('Gst', '1.0')
    from gi.repository import Gst, GLib
    Gst.init(None)
except Exception as e:
    die(f"请检查依赖项是否安装 {e}")

def on_response(response, results, path=None):
    """
    全局 D-Bus 信号处理：当任意请求收到响应时，
    根据请求路径取出 pending 中对应的回调并执行。
    """
    cb = pending.pop(path, None)
    if cb is not None:
        cb(response, dict(results))

def on_closed(*args, path=None):
    """当用户在系统层面撤销共享权限时触发"""
    if session is None or path != session:
        return
    die("Portal 会话被系统或用户关闭")

bus = dbus.SessionBus()
bus.add_signal_receiver(
    on_response,
    signal_name="Response",
    dbus_interface="org.freedesktop.portal.Request",
    path_keyword="path",
)
bus.add_signal_receiver(
    on_closed,
    signal_name="Closed",
    dbus_interface="org.freedesktop.portal.Session",
    path_keyword="path"
)

portal = bus.get_object("org.freedesktop.portal.Desktop", "/org/freedesktop/portal/desktop")
sc = dbus.Interface(portal, "org.freedesktop.portal.ScreenCast")

def start_pipeline(node_id):
    """
    根据 PipeWire 节点 ID 启动 GStreamer 管道，
    将视频流转换为 BGR 格式并通过 appsink 写入共享内存。
    """
    global pipeline

    pipe = Gst.parse_launch(
        f"pipewiresrc path={node_id} "
        f"! videoconvert "
        f"! video/x-raw,format=BGR "
        f"! appsink name=s emit-signals=true sync=false "
        f"  max-buffers=1 drop=true"
    )
    if pipe is None:
        die("GStreamer pipeline failed")

    def on_new_sample(sink):
        """处理来自 appsink 的新采样，将帧数据写入共享内存。"""
        try:
            sample = sink.emit("pull-sample")
            if not sample:
                return Gst.FlowReturn.ERROR
            buf = sample.get_buffer()
            caps = sample.get_caps()
            w = h = 0
            if caps:
                s2 = caps.get_structure(0)
                _, w = s2.get_int("width")  if s2.has_field("width")  else (False, 0)
                _, h = s2.get_int("height") if s2.has_field("height") else (False, 0)
            ok, info = buf.map(Gst.MapFlags.READ)
            if ok:
                try:
                    with FRAME_TMP.open("wb") as f:
                        # 大端字节序写入 width 和 height
                        # 一共 8B
                        f.write(struct.pack("!II", w, h))
                        f.write(info.data)
                    FRAME_TMP.rename(FRAME_BIN)
                finally:
                    buf.unmap(info)
            return Gst.FlowReturn.OK
        except Exception:
            traceback.print_exc(file=sys.stderr)
            return Gst.FlowReturn.ERROR

    appsink = pipe.get_by_name("s")
    appsink.connect("new-sample", on_new_sample)

    gst_bus = pipe.get_bus()
    gst_bus.add_signal_watch()
    gst_bus.connect("message::error", lambda _bus, msg: die(f"gst error: {msg.parse_error()[0].message}"))
    gst_bus.connect("message::eos", lambda _bus, _msg: die(code=0))

    pipe.set_state(Gst.State.PLAYING)
    pipeline = pipe
    print(f"管道已在节点 {node_id} 上启动", file=sys.stderr)

def on_started(response, results):
    """ScreenCast Start 方法回调，提取第一个流的节点 ID 并启动管道。"""
    if response == 0:
        streams = results.get("streams", [])
        if not streams:
            die("未从 portal 返回任何流")
        node_id = str(streams[0][0])
        start_pipeline(node_id)
    else:
        die(f"启动失败，响应代码: {response}")

def on_selected(response, results):
    """用户选择捕获源后的回调，调用 Start 启动流传输。"""
    global session
    if response == 0:
        h = sc.Start(session, "", {})
        pending[h] = on_started
    else:
        die(f"源选择失败或被取消，响应代码: {response}")

def on_created(response, results):
    """CreateSession 回调，保存会话句柄并弹出源选择对话框。"""
    global session
    if response == 0:
        session = results["session_handle"]
        h = sc.SelectSources(
            session,
            {
                "types":       dbus.UInt32(3),   # 屏幕 | 窗口
                "multiple":    False,
            },
        )
        pending[h] = on_selected
    else:
        die(f"创建会话失败，响应代码: {response}")

def init():
    h = sc.CreateSession({"session_handle_token": "bettergi"})
    pending[h] = on_created

    def _on_timeout():
        if h in pending:
            pending.pop(h)
            die("Portal 请求超时（选择窗口）")
        return GLib.SOURCE_REMOVE
    GLib.timeout_add_seconds(30, _on_timeout)

def handle_exit_signal(signum, frame):
    """信号处理函数，在主线程中安全退出。"""
    GLib.idle_add(die, None, 0, priority=GLib.PRIORITY_HIGH)

for sig in (signal.SIGTERM, signal.SIGINT, signal.SIGHUP):
    signal.signal(sig, handle_exit_signal)

if __name__ == "__main__":
    loop = GLib.MainLoop()
    GLib.idle_add(init)
    loop.run()
