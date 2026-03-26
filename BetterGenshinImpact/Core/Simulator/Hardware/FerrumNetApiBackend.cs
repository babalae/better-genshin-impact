using Microsoft.Extensions.Logging;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.Threading;

namespace BetterGenshinImpact.Core.Simulator.Hardware;

internal static class FerrumNetApiBackend
{
    private static readonly object SyncRoot = new();
    private static readonly Dictionary<string, FerrumNetConnection> Connections = new(StringComparer.OrdinalIgnoreCase);

    public static IHardwareKeyboardBackend CreateKeyboard(string ip, string port, string uuid)
    {
        return new FerrumNetKeyboardBackend(GetConnection(ip, port, uuid));
    }

    public static IHardwareMouseBackend CreateMouse(string ip, string port, string uuid)
    {
        return new FerrumNetMouseBackend(GetConnection(ip, port, uuid));
    }

    private static FerrumNetConnection GetConnection(string ip, string port, string uuid)
    {
        var cacheKey = $"{ip.Trim()}|{port.Trim()}|{uuid.Trim()}";

        lock (SyncRoot)
        {
            if (!Connections.TryGetValue(cacheKey, out var connection))
            {
                connection = new FerrumNetConnection(ip, port, uuid);
                Connections[cacheKey] = connection;
            }

            return connection;
        }
    }

    private sealed class FerrumNetConnection(string ip, string port, string uuid) : IHardwareApiConnection
    {
        private delegate void PayloadWriter(Span<byte> payload);

        private const uint CmdConnect = 0xAF3C2828;
        private const uint CmdMouseMove = 0xAEDE7345;
        private const uint CmdMouseLeft = 0x9823AE8D;
        private const uint CmdMouseMiddle = 0x97A3AE8D;
        private const uint CmdMouseRight = 0x238D8212;
        private const uint CmdMouseWheel = 0xFFEEAD38;
        private const uint CmdMouseAutoMove = 0xAEDE7346;
        private const uint CmdKeyboardAll = 0x123C2C2F;
        private const uint CmdMonitor = 0x27388020;
        private const int HeaderLength = 16;
        private const int MousePayloadLength = 56;
        private const int KeyboardPayloadLength = 12;
        private const int MonitorPacketLength = 20;

        private readonly object _syncRoot = new();
        private readonly object _stateSyncRoot = new();
        private readonly ILogger? _logger = App.GetService<ILogger<FerrumNetConnection>>();
        private readonly byte[] _softwareKeyboardButtons = new byte[10];
        private readonly byte[] _physicalKeyboardButtons = new byte[10];
        private Socket? _commandSocket;
        private Socket? _monitorSocket;
        private Thread? _monitorThread;
        private bool _isDisposed;
        private bool _horizontalWheelWarningShown;
        private uint _deviceToken;
        private uint _index;
        private int? _cursorX;
        private int? _cursorY;
        private int _softwareMouseButtons;
        private byte _softwareKeyboardModifiers;
        private byte _physicalMouseButtons;
        private byte _physicalKeyboardModifiers;

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
                        _logger?.LogWarning("Ferrum NET endpoint is invalid: {Ip}:{Port}", ip, port);
                        return false;
                    }

                    if (!TryParseDeviceToken(uuid, out _deviceToken))
                    {
                        _logger?.LogWarning("Ferrum NET UUID is invalid: {Uuid}", uuid);
                        return false;
                    }

                    _commandSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp)
                    {
                        SendTimeout = 500,
                        ReceiveTimeout = 500
                    };
                    _commandSocket.Connect(endpoint);

                    _index = 0;
                    SendPacketCore(CmdConnect, Random.Shared.Next(), payloadWriter: null, payloadLength: 0, useCurrentIndex: true);
                    StartMonitorUnsafe();
                    _cursorX = null;
                    _cursorY = null;
                    _logger?.LogInformation("Ferrum NET connected: {Ip}:{Port}", ip, port);
                    return true;
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Failed to connect Ferrum NET: {Ip}:{Port}", ip, port);
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
            lock (_syncRoot)
            {
                if (!EnsureConnected())
                {
                    return;
                }

                ApplyKeyboardStateUnsafe(hidCode, isDown: true);
                SendKeyboardStateUnsafe();
            }
        }

        public void KeyUp(int hidCode)
        {
            lock (_syncRoot)
            {
                if (!EnsureConnected())
                {
                    return;
                }

                ApplyKeyboardStateUnsafe(hidCode, isDown: false);
                SendKeyboardStateUnsafe();
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

                SendPacketCore(CmdMouseMove, Random.Shared.Next(), payload =>
                {
                    WriteMousePayload(payload, _softwareMouseButtons, dx, dy, 0);
                }, MousePayloadLength);

                if (_cursorX.HasValue) _cursorX += dx;
                if (_cursorY.HasValue) _cursorY += dy;
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

                _softwareMouseButtons |= GetMouseButtonMask(button);
                SendMouseButtonStateUnsafe(button);
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

                _softwareMouseButtons &= ~GetMouseButtonMask(button);
                SendMouseButtonStateUnsafe(button);
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

                SendPacketCore(CmdMouseWheel, Random.Shared.Next(), payload =>
                {
                    WriteMousePayload(payload, _softwareMouseButtons, 0, 0, delta);
                }, MousePayloadLength);
            }
        }

        public void MouseWheelHorizontal(int delta)
        {
            if (delta == 0 || _horizontalWheelWarningShown)
            {
                return;
            }

            _horizontalWheelWarningShown = true;
            _logger?.LogWarning("Ferrum NET does not expose horizontal wheel in current backend.");
        }

        public bool TryReadMouseButtonState(HardwareMouseButton button, out HardwareInputState state)
        {
            lock (_stateSyncRoot)
            {
                var physical = (_physicalMouseButtons & GetMouseButtonMask(button)) != 0;
                var hardware = (_softwareMouseButtons & GetMouseButtonMask(button)) != 0;
                state = ToHardwareInputState(physical, hardware);
                return true;
            }
        }

        public bool TryReadKeyState(int hidCode, out HardwareInputState state)
        {
            lock (_stateSyncRoot)
            {
                var physical = IsKeyboardPressedUnsafe(_physicalKeyboardModifiers, _physicalKeyboardButtons, hidCode);
                var hardware = IsKeyboardPressedUnsafe(_softwareKeyboardModifiers, _softwareKeyboardButtons, hidCode);
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

            SendPacketCore(CmdMonitor, monitorPort | (0xAA55 << 16), payloadWriter: null, payloadLength: 0);

            _monitorThread = new Thread(MonitorLoop)
            {
                IsBackground = true,
                Name = $"FerrumNetMonitor:{ip}:{port}"
            };
            _monitorThread.Start();
        }

        private void MonitorLoop()
        {
            var buffer = new byte[1024];

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
                    if (received < MonitorPacketLength)
                    {
                        continue;
                    }

                    lock (_stateSyncRoot)
                    {
                        _physicalMouseButtons = buffer[1];
                        _physicalKeyboardModifiers = buffer[9];
                        Array.Copy(buffer, 10, _physicalKeyboardButtons, 0, _physicalKeyboardButtons.Length);
                    }
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
                    _logger?.LogDebug(ex, "Ferrum NET monitor loop stopped.");
                    return;
                }
            }
        }

        private void SendMouseButtonStateUnsafe(HardwareMouseButton button)
        {
            var command = button switch
            {
                HardwareMouseButton.Left => CmdMouseLeft,
                HardwareMouseButton.Right => CmdMouseRight,
                HardwareMouseButton.Middle => CmdMouseMiddle,
                HardwareMouseButton.Side1 => CmdMouseWheel,
                HardwareMouseButton.Side2 => CmdMouseWheel,
                _ => CmdMouseLeft
            };

            SendPacketCore(command, Random.Shared.Next(), payload =>
            {
                WriteMousePayload(payload, _softwareMouseButtons, 0, 0, 0);
            }, MousePayloadLength);
        }

        private void SendKeyboardStateUnsafe()
        {
            SendPacketCore(CmdKeyboardAll, Random.Shared.Next(), payload =>
            {
                payload[0] = _softwareKeyboardModifiers;
                payload[1] = 0;
                _softwareKeyboardButtons.CopyTo(payload[2..]);
            }, KeyboardPayloadLength);
        }

        private void ApplyKeyboardStateUnsafe(int hidCode, bool isDown)
        {
            if (TryGetModifierBit(hidCode, out var modifierBit))
            {
                if (isDown)
                {
                    _softwareKeyboardModifiers |= modifierBit;
                }
                else
                {
                    _softwareKeyboardModifiers &= (byte)~modifierBit;
                }

                return;
            }

            if (isDown)
            {
                if (_softwareKeyboardButtons.Contains((byte)hidCode))
                {
                    return;
                }

                for (var i = 0; i < _softwareKeyboardButtons.Length; i++)
                {
                    if (_softwareKeyboardButtons[i] == 0)
                    {
                        _softwareKeyboardButtons[i] = (byte)hidCode;
                        return;
                    }
                }

                Array.Copy(_softwareKeyboardButtons, 1, _softwareKeyboardButtons, 0, _softwareKeyboardButtons.Length - 1);
                _softwareKeyboardButtons[^1] = (byte)hidCode;
            }
            else
            {
                for (var i = 0; i < _softwareKeyboardButtons.Length; i++)
                {
                    if (_softwareKeyboardButtons[i] != hidCode)
                    {
                        continue;
                    }

                    Array.Copy(_softwareKeyboardButtons, i + 1, _softwareKeyboardButtons, i, _softwareKeyboardButtons.Length - i - 1);
                    _softwareKeyboardButtons[^1] = 0;
                    return;
                }
            }
        }

        private void SendPacketCore(uint command, int randomValue, PayloadWriter? payloadWriter, int payloadLength, bool useCurrentIndex = false)
        {
            if (_commandSocket == null)
            {
                return;
            }

            var buffer = new byte[HeaderLength + payloadLength];
            var index = useCurrentIndex ? _index : ++_index;
            BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(0, 4), _deviceToken);
            BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(4, 4), unchecked((uint)randomValue));
            BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(8, 4), index);
            BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(12, 4), command);
            payloadWriter?.Invoke(buffer.AsSpan(HeaderLength));

            _commandSocket.Send(buffer);

            var response = new byte[1024];
            var received = _commandSocket.Receive(response);
            if (received < HeaderLength)
            {
                throw new SocketException((int)SocketError.TimedOut);
            }

            var responseIndex = BinaryPrimitives.ReadUInt32LittleEndian(response.AsSpan(8, 4));
            var responseCommand = BinaryPrimitives.ReadUInt32LittleEndian(response.AsSpan(12, 4));
            if (responseCommand != command || responseIndex != index)
            {
                throw new InvalidOperationException($"Ferrum NET response mismatch. cmd={responseCommand:X8} idx={responseIndex}, expected cmd={command:X8} idx={index}");
            }
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

        private static void WriteMousePayload(Span<byte> payload, int buttons, int x, int y, int wheel)
        {
            BinaryPrimitives.WriteInt32LittleEndian(payload[0..4], buttons);
            BinaryPrimitives.WriteInt32LittleEndian(payload[4..8], x);
            BinaryPrimitives.WriteInt32LittleEndian(payload[8..12], y);
            BinaryPrimitives.WriteInt32LittleEndian(payload[12..16], wheel);

            for (var offset = 16; offset < MousePayloadLength; offset += 4)
            {
                BinaryPrimitives.WriteInt32LittleEndian(payload[offset..(offset + 4)], 0);
            }
        }

        private static bool TryParseDeviceToken(string text, out uint token)
        {
            var hex = Regex.Replace(text ?? string.Empty, "[^0-9a-fA-F]", string.Empty);
            if (hex.Length < 8 || !uint.TryParse(hex[..8], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out token))
            {
                token = 0;
                return false;
            }

            return true;
        }

        private bool TryCreateEndpoint(out IPEndPoint endpoint)
        {
            endpoint = new IPEndPoint(IPAddress.None, 0);
            return IPAddress.TryParse(ip.Trim(), out var address)
                && int.TryParse(port.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedPort)
                && parsedPort is > 0 and <= 65535
                && (endpoint = new IPEndPoint(address, parsedPort)) != null;
        }

        private static bool TryGetModifierBit(int hidCode, out byte modifierBit)
        {
            modifierBit = hidCode switch
            {
                224 => 0x01,
                225 => 0x02,
                226 => 0x04,
                227 => 0x08,
                228 => 0x10,
                229 => 0x20,
                230 => 0x40,
                231 => 0x80,
                _ => 0
            };

            return modifierBit != 0;
        }

        private static bool IsKeyboardPressedUnsafe(byte modifiers, byte[] keys, int hidCode)
        {
            if (TryGetModifierBit(hidCode, out var modifierBit))
            {
                return (modifiers & modifierBit) != 0;
            }

            return keys.Contains((byte)hidCode);
        }

        private static HardwareInputState ToHardwareInputState(bool physical, bool hardware)
        {
            if (physical && hardware) return HardwareInputState.Both;
            if (physical) return HardwareInputState.Physical;
            if (hardware) return HardwareInputState.Hardware;
            return HardwareInputState.None;
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
    }

    private sealed class FerrumNetKeyboardBackend(FerrumNetConnection connection) : IHardwareKeyboardBackend, IHardwareKeyboardStateBackend
    {
        public bool EnsureConnected() => connection.EnsureConnected();
        public void KeyDown(int hidCode) => connection.KeyDown(hidCode);
        public void KeyUp(int hidCode) => connection.KeyUp(hidCode);
        public void KeyPress(int hidCode) => connection.KeyPress(hidCode);
        public bool TryGetKeyState(int hidCode, out HardwareInputState state) => connection.TryReadKeyState(hidCode, out state);
    }

    private sealed class FerrumNetMouseBackend(FerrumNetConnection connection) : IHardwareMouseBackend, IHardwareMouseStateBackend
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
