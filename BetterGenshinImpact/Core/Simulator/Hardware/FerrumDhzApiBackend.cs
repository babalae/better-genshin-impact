using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace BetterGenshinImpact.Core.Simulator.Hardware;

internal static class FerrumDhzApiBackend
{
    private static readonly object SyncRoot = new();
    private static readonly Dictionary<string, FerrumDhzConnection> Connections = new(StringComparer.OrdinalIgnoreCase);

    public static IHardwareKeyboardBackend CreateKeyboard(string ip, string port, string key)
    {
        return new FerrumDhzKeyboardBackend(GetConnection(ip, port, key));
    }

    public static IHardwareMouseBackend CreateMouse(string ip, string port, string key)
    {
        return new FerrumDhzMouseBackend(GetConnection(ip, port, key));
    }

    private static FerrumDhzConnection GetConnection(string ip, string port, string key)
    {
        var cacheKey = $"{ip.Trim()}|{port.Trim()}|{key.Trim()}";

        lock (SyncRoot)
        {
            if (!Connections.TryGetValue(cacheKey, out var connection))
            {
                connection = new FerrumDhzConnection(ip, port, key);
                Connections[cacheKey] = connection;
            }

            return connection;
        }
    }

    private sealed class FerrumDhzConnection(string ip, string port, string key) : IHardwareApiConnection
    {
        private readonly object _syncRoot = new();
        private readonly object _stateSyncRoot = new();
        private readonly ILogger? _logger = App.GetService<ILogger<FerrumDhzConnection>>();
        private readonly HashSet<string> _physicalKeys = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _softwareKeys = new(StringComparer.OrdinalIgnoreCase);
        private Socket? _commandSocket;
        private Socket? _monitorSocket;
        private Thread? _monitorThread;
        private bool _isDisposed;
        private bool _horizontalWheelWarningShown;
        private int? _cursorX;
        private int? _cursorY;
        private int _physicalMouseButtons;
        private int _softwareMouseButtons;
        private int _shiftKey;

        public bool EnsureConnected()
        {
            lock (_syncRoot)
            {
                if (_isDisposed)
                {
                    return false;
                }

                if (_commandSocket != null)
                {
                    return true;
                }

                try
                {
                    if (!TryCreateEndpoint(out var endpoint))
                    {
                        _logger?.LogWarning("Ferrum DHZ endpoint is invalid: {Ip}:{Port}", ip, port);
                        return false;
                    }

                    _commandSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp)
                    {
                        SendTimeout = 500
                    };
                    _commandSocket.Connect(endpoint);
                    _shiftKey = ParseShiftKey(key);
                    StartMonitorUnsafe();
                    _cursorX = null;
                    _cursorY = null;
                    _logger?.LogInformation("Ferrum DHZ connected: {Ip}:{Port}", ip, port);
                    return true;
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Failed to connect Ferrum DHZ: {Ip}:{Port}", ip, port);
                    SafeCloseSockets();
                    return false;
                }
            }
        }

        public void Dispose()
        {
            lock (_syncRoot)
            {
                _isDisposed = true;
                SafeCloseSockets();
            }
        }

        public void KeyDown(int hidCode)
        {
            if (!TryMapHidToDhzKey(hidCode, out var keyName))
            {
                _logger?.LogWarning("Ferrum DHZ key mapping is missing for HID code {HidCode}", hidCode);
                return;
            }

            lock (_syncRoot)
            {
                if (!EnsureConnected())
                {
                    return;
                }

                lock (_stateSyncRoot)
                {
                    _softwareKeys.Add(keyName);
                }

                SendCommandUnsafe(FormattableString.Invariant($"keydown({keyName})"));
            }
        }

        public void KeyUp(int hidCode)
        {
            if (!TryMapHidToDhzKey(hidCode, out var keyName))
            {
                _logger?.LogWarning("Ferrum DHZ key mapping is missing for HID code {HidCode}", hidCode);
                return;
            }

            lock (_syncRoot)
            {
                if (!EnsureConnected())
                {
                    return;
                }

                lock (_stateSyncRoot)
                {
                    _softwareKeys.Remove(keyName);
                }

                SendCommandUnsafe(FormattableString.Invariant($"keyup({keyName})"));
            }
        }

        public void KeyPress(int hidCode)
        {
            KeyDown(hidCode);
            Thread.Sleep(20);
            KeyUp(hidCode);
        }

        public void MouseMoveBy(int dx, int dy)
        {
            lock (_syncRoot)
            {
                if (!EnsureConnected())
                {
                    return;
                }

                foreach (var (stepX, stepY) in SplitMovement(dx, dy))
                {
                    SendCommandUnsafe(FormattableString.Invariant($"move({stepX}, {stepY})"));
                    if (_cursorX.HasValue) _cursorX += stepX;
                    if (_cursorY.HasValue) _cursorY += stepY;
                    Thread.Sleep(1);
                }
            }
        }

        public void MouseMoveTo(int x, int y)
        {
            var (currentX, currentY) = GetTrackedCursorPosition();
            MouseMoveBy(x - currentX, y - currentY);
        }

        public void MouseButtonDown(HardwareMouseButton button)
        {
            lock (_syncRoot)
            {
                if (!EnsureConnected())
                {
                    return;
                }

                lock (_stateSyncRoot)
                {
                    _softwareMouseButtons |= GetMouseButtonMask(button);
                }

                SendCommandUnsafe(FormattableString.Invariant($"{GetMouseButtonName(button)}(1)"));
            }
        }

        public void MouseButtonUp(HardwareMouseButton button)
        {
            lock (_syncRoot)
            {
                if (!EnsureConnected())
                {
                    return;
                }

                lock (_stateSyncRoot)
                {
                    _softwareMouseButtons &= ~GetMouseButtonMask(button);
                }

                SendCommandUnsafe(FormattableString.Invariant($"{GetMouseButtonName(button)}(0)"));
            }
        }

        public void MouseButtonClick(HardwareMouseButton button, int count)
        {
            for (var i = 0; i < Math.Max(1, count); i++)
            {
                MouseButtonDown(button);
                Thread.Sleep(20);
                MouseButtonUp(button);
                Thread.Sleep(20);
            }
        }

        public void MouseWheelVertical(int delta)
        {
            lock (_syncRoot)
            {
                if (!EnsureConnected() || delta == 0)
                {
                    return;
                }

                SendCommandUnsafe(FormattableString.Invariant($"wheel({delta})"));
            }
        }

        public void MouseWheelHorizontal(int delta)
        {
            if (delta == 0 || _horizontalWheelWarningShown)
            {
                return;
            }

            _horizontalWheelWarningShown = true;
            _logger?.LogWarning("Ferrum DHZ does not expose horizontal wheel in current backend.");
        }

        public bool TryReadMouseButtonState(HardwareMouseButton button, out HardwareInputState state)
        {
            lock (_stateSyncRoot)
            {
                var mask = GetMouseButtonMask(button);
                var physical = (_physicalMouseButtons & mask) != 0;
                var hardware = (_softwareMouseButtons & mask) != 0;
                state = ToHardwareInputState(physical, hardware);
                return true;
            }
        }

        public bool TryReadKeyState(int hidCode, out HardwareInputState state)
        {
            state = HardwareInputState.None;
            if (!TryMapHidToDhzKey(hidCode, out var keyName))
            {
                return false;
            }

            lock (_stateSyncRoot)
            {
                var physical = _physicalKeys.Contains(keyName);
                var hardware = _softwareKeys.Contains(keyName);
                state = ToHardwareInputState(physical, hardware);
                return true;
            }
        }

        private void StartMonitorUnsafe()
        {
            _monitorSocket?.Dispose();
            _monitorSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp)
            {
                ReceiveTimeout = 1000
            };
            _monitorSocket.Bind(new IPEndPoint(IPAddress.Any, 0));
            var monitorPort = ((IPEndPoint)_monitorSocket.LocalEndPoint!).Port;

            SendCommandUnsafe(FormattableString.Invariant($"monitor({monitorPort})"));

            _monitorThread = new Thread(MonitorLoop)
            {
                IsBackground = true,
                Name = $"FerrumDhzMonitor:{ip}:{port}"
            };
            _monitorThread.Start();
        }

        private void MonitorLoop()
        {
            var buffer = new byte[2048];

            while (true)
            {
                Socket? socket;

                lock (_syncRoot)
                {
                    if (_monitorSocket == null || _isDisposed)
                    {
                        return;
                    }

                    socket = _monitorSocket;
                }

                try
                {
                    var received = socket.Receive(buffer);
                    if (received <= 0)
                    {
                        continue;
                    }

                    var text = DecodeText(buffer, received);
                    ParseMonitorPayload(text);
                }
                catch (SocketException ex) when (ex.SocketErrorCode == SocketError.TimedOut || ex.SocketErrorCode == SocketError.Interrupted)
                {
                    continue;
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    _logger?.LogDebug(ex, "Ferrum DHZ monitor loop stopped.");
                    return;
                }
            }
        }

        private void ParseMonitorPayload(string text)
        {
            var parts = text.Split('|', 6, StringSplitOptions.TrimEntries);
            if (parts.Length < 6)
            {
                return;
            }

            var mouseButtons = 0;
            if (IsTruthy(parts[0])) mouseButtons |= GetMouseButtonMask(HardwareMouseButton.Left);
            if (IsTruthy(parts[1])) mouseButtons |= GetMouseButtonMask(HardwareMouseButton.Middle);
            if (IsTruthy(parts[2])) mouseButtons |= GetMouseButtonMask(HardwareMouseButton.Right);
            if (IsTruthy(parts[3])) mouseButtons |= GetMouseButtonMask(HardwareMouseButton.Side1);
            if (IsTruthy(parts[4])) mouseButtons |= GetMouseButtonMask(HardwareMouseButton.Side2);

            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match match in Regex.Matches(parts[5], @"KEY_[A-Z0-9_]+", RegexOptions.IgnoreCase))
            {
                keys.Add(match.Value.ToUpperInvariant());
            }

            lock (_stateSyncRoot)
            {
                _physicalMouseButtons = mouseButtons;
                _physicalKeys.Clear();
                foreach (var physicalKey in keys)
                {
                    _physicalKeys.Add(physicalKey);
                }
            }
        }

        private void SendCommandUnsafe(string command)
        {
            if (_commandSocket == null)
            {
                return;
            }

            var bytes = Encoding.ASCII.GetBytes(EncodeText(command));
            _commandSocket.Send(bytes);
        }

        private (int X, int Y) GetTrackedCursorPosition()
        {
            lock (_syncRoot)
            {
                if (_cursorX.HasValue && _cursorY.HasValue)
                {
                    return (_cursorX.Value, _cursorY.Value);
                }

                if (Vanara.PInvoke.User32.GetCursorPos(out var point))
                {
                    _cursorX = point.X;
                    _cursorY = point.Y;
                    return (point.X, point.Y);
                }

                _cursorX = 0;
                _cursorY = 0;
                return (0, 0);
            }
        }

        private string EncodeText(string command)
        {
            return ShiftLetters(command, _shiftKey);
        }

        private string DecodeText(byte[] buffer, int length)
        {
            var text = Encoding.ASCII.GetString(buffer, 0, length).Trim('\0', '\r', '\n', ' ');
            return ShiftLetters(text, -_shiftKey);
        }

        private void SafeCloseSockets()
        {
            try
            {
                _monitorSocket?.Close();
            }
            catch
            {
            }

            try
            {
                _commandSocket?.Close();
            }
            catch
            {
            }

            _monitorSocket?.Dispose();
            _commandSocket?.Dispose();
            _monitorSocket = null;
            _commandSocket = null;
            _monitorThread = null;
        }

        private bool TryCreateEndpoint(out IPEndPoint endpoint)
        {
            endpoint = new IPEndPoint(IPAddress.None, 0);
            return IPAddress.TryParse(ip.Trim(), out var address)
                && int.TryParse(port.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedPort)
                && parsedPort is > 0 and <= 65535
                && (endpoint = new IPEndPoint(address, parsedPort)) != null;
        }

        private static int ParseShiftKey(string value)
        {
            var digits = Regex.Match(value ?? string.Empty, @"-?\d+").Value;
            return int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;
        }

        private static string ShiftLetters(string text, int shift)
        {
            if (string.IsNullOrEmpty(text) || shift == 0)
            {
                return text;
            }

            var normalizedShift = shift % 26;
            var chars = text.ToCharArray();

            for (var i = 0; i < chars.Length; i++)
            {
                chars[i] = chars[i] switch
                {
                    >= 'a' and <= 'z' => (char)('a' + (((chars[i] - 'a') + normalizedShift + 26) % 26)),
                    >= 'A' and <= 'Z' => (char)('A' + (((chars[i] - 'A') + normalizedShift + 26) % 26)),
                    _ => chars[i]
                };
            }

            return new string(chars);
        }

        private static IEnumerable<(int X, int Y)> SplitMovement(int dx, int dy)
        {
            var remainingX = dx;
            var remainingY = dy;

            while (remainingX != 0 || remainingY != 0)
            {
                var stepX = Math.Clamp(remainingX, -127, 127);
                var stepY = Math.Clamp(remainingY, -127, 127);
                remainingX -= stepX;
                remainingY -= stepY;
                yield return (stepX, stepY);
            }
        }

        private static bool IsTruthy(string value)
        {
            return value.Trim() is "1" or "true" or "True";
        }

        private static HardwareInputState ToHardwareInputState(bool physical, bool hardware)
        {
            if (physical && hardware) return HardwareInputState.Both;
            if (physical) return HardwareInputState.Physical;
            if (hardware) return HardwareInputState.Hardware;
            return HardwareInputState.None;
        }

        private static string GetMouseButtonName(HardwareMouseButton button)
        {
            return button switch
            {
                HardwareMouseButton.Left => "left",
                HardwareMouseButton.Right => "right",
                HardwareMouseButton.Middle => "middle",
                HardwareMouseButton.Side1 => "side1",
                HardwareMouseButton.Side2 => "side2",
                _ => "left"
            };
        }

        private static int GetMouseButtonMask(HardwareMouseButton button)
        {
            return button switch
            {
                HardwareMouseButton.Left => 0x01,
                HardwareMouseButton.Right => 0x02,
                HardwareMouseButton.Middle => 0x04,
                HardwareMouseButton.Side1 => 0x08,
                HardwareMouseButton.Side2 => 0x10,
                _ => 0
            };
        }

        private static bool TryMapHidToDhzKey(int hidCode, out string keyName)
        {
            keyName = hidCode switch
            {
                >= 4 and <= 29 => $"KEY_{(char)('A' + hidCode - 4)}",
                30 => "KEY_1",
                31 => "KEY_2",
                32 => "KEY_3",
                33 => "KEY_4",
                34 => "KEY_5",
                35 => "KEY_6",
                36 => "KEY_7",
                37 => "KEY_8",
                38 => "KEY_9",
                39 => "KEY_0",
                40 => "KEY_ENTER",
                41 => "KEY_ESC",
                42 => "KEY_BACKSPACE",
                43 => "KEY_TAB",
                44 => "KEY_SPACE",
                45 => "KEY_MINUS",
                46 => "KEY_EQUAL",
                47 => "KEY_LEFTBRACKET",
                48 => "KEY_RIGHTBRACKET",
                49 => "KEY_BACKSLASH",
                51 => "KEY_SEMICOLON",
                52 => "KEY_APOSTROPHE",
                53 => "KEY_TILDE",
                54 => "KEY_COMMA",
                55 => "KEY_PERIOD",
                56 => "KEY_SLASH",
                57 => "KEY_CAPSLOCK",
                >= 58 and <= 69 => $"KEY_F{hidCode - 57}",
                73 => "KEY_INSERT",
                74 => "KEY_HOME",
                75 => "KEY_PAGEUP",
                76 => "KEY_DELETE",
                77 => "KEY_END",
                78 => "KEY_PAGEDOWN",
                79 => "KEY_RIGHT",
                80 => "KEY_LEFT",
                81 => "KEY_DOWN",
                82 => "KEY_UP",
                83 => "KEY_NUMLOCK",
                84 => "KEY_NUM_DIVIDE",
                85 => "KEY_NUM_MULTIPLY",
                86 => "KEY_NUM_SUBTRACT",
                87 => "KEY_NUM_ADD",
                88 => "KEY_NUM_ENTER",
                89 => "KEY_NUM1",
                90 => "KEY_NUM2",
                91 => "KEY_NUM3",
                92 => "KEY_NUM4",
                93 => "KEY_NUM5",
                94 => "KEY_NUM6",
                95 => "KEY_NUM7",
                96 => "KEY_NUM8",
                97 => "KEY_NUM9",
                98 => "KEY_NUM0",
                99 => "KEY_NUM_DECIMAL",
                101 => "KEY_APPLICATION",
                224 => "KEY_LEFTCTRL",
                225 => "KEY_LEFTSHIFT",
                226 => "KEY_LEFTALT",
                227 => "KEY_LEFTWIN",
                228 => "KEY_RIGHTCTRL",
                229 => "KEY_RIGHTSHIFT",
                230 => "KEY_RIGHTALT",
                231 => "KEY_RIGHTWIN",
                _ => string.Empty
            };

            return !string.IsNullOrEmpty(keyName);
        }
    }

    private sealed class FerrumDhzKeyboardBackend(FerrumDhzConnection connection) : IHardwareKeyboardBackend, IHardwareKeyboardStateBackend
    {
        public bool EnsureConnected() => connection.EnsureConnected();
        public void KeyDown(int hidCode) => connection.KeyDown(hidCode);
        public void KeyUp(int hidCode) => connection.KeyUp(hidCode);
        public void KeyPress(int hidCode) => connection.KeyPress(hidCode);
        public bool TryGetKeyState(int hidCode, out HardwareInputState state) => connection.TryReadKeyState(hidCode, out state);
    }

    private sealed class FerrumDhzMouseBackend(FerrumDhzConnection connection) : IHardwareMouseBackend, IHardwareMouseStateBackend
    {
        public bool EnsureConnected() => connection.EnsureConnected();
        public void MouseMoveBy(int dx, int dy) => connection.MouseMoveBy(dx, dy);
        public void MouseMoveTo(int x, int y) => connection.MouseMoveTo(x, y);
        public void MouseButtonDown(HardwareMouseButton button) => connection.MouseButtonDown(button);
        public void MouseButtonUp(HardwareMouseButton button) => connection.MouseButtonUp(button);
        public void MouseButtonClick(HardwareMouseButton button, int count) => connection.MouseButtonClick(button, count);
        public void MouseWheelVertical(int delta) => connection.MouseWheelVertical(delta);
        public void MouseWheelHorizontal(int delta) => connection.MouseWheelHorizontal(delta);
        public bool TryGetButtonState(HardwareMouseButton button, out HardwareInputState state) => connection.TryReadMouseButtonState(button, out state);
    }
}
