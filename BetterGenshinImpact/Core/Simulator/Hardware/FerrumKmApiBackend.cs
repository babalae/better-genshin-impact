using BetterGenshinImpact.Helpers;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO.Ports;
using System.Text;
using System.Threading;
using Vanara.PInvoke;

namespace BetterGenshinImpact.Core.Simulator.Hardware;

internal static class FerrumKmApiBackend
{
    private static readonly object SyncRoot = new();
    private static readonly Dictionary<string, FerrumKmConnection> Connections = new(StringComparer.OrdinalIgnoreCase);

    public static IHardwareKeyboardBackend CreateKeyboard(string portName)
    {
        return new FerrumKmKeyboardBackend(GetConnection(portName));
    }

    public static IHardwareMouseBackend CreateMouse(string portName)
    {
        return new FerrumKmMouseBackend(GetConnection(portName));
    }

    private static FerrumKmConnection GetConnection(string portName)
    {
        lock (SyncRoot)
        {
            if (!Connections.TryGetValue(portName, out var connection))
            {
                connection = new FerrumKmConnection(portName);
                Connections[portName] = connection;
            }

            return connection;
        }
    }

    private sealed class FerrumKmConnection(string portName) : IHardwareApiConnection
    {
        private readonly object _syncRoot = new();
        private readonly ILogger? _logger = App.GetService<ILogger<FerrumKmConnection>>();
        private SerialPort? _serialPort;
        private bool _isDisposed;
        private int? _cursorX;
        private int? _cursorY;
        private bool _horizontalWheelWarningShown;

        public bool EnsureConnected()
        {
            lock (_syncRoot)
            {
                if (_isDisposed)
                {
                    return false;
                }

                if (_serialPort?.IsOpen == true)
                {
                    return true;
                }

                try
                {
                    _serialPort?.Dispose();
                    _serialPort = new SerialPort(portName, 115200, Parity.None, 8, StopBits.One)
                    {
                        ReadTimeout = 100,
                        WriteTimeout = 100,
                        NewLine = "\r\n"
                    };
                    _serialPort.Open();
                    Thread.Sleep(150);
                    _cursorX = null;
                    _cursorY = null;
                    _logger?.LogInformation("Ferrum KM connected: {Port}", portName);
                    return true;
                }
                catch (Exception ex)
                {
                    SafeClosePort();
                    _logger?.LogWarning(ex, "Failed to connect Ferrum KM: {Port}", portName);
                    return false;
                }
            }
        }

        public void Dispose()
        {
            lock (_syncRoot)
            {
                _isDisposed = true;
                SafeClosePort();
            }
        }

        public void KeyDown(int hidCode) => WriteCommand(FormattableString.Invariant($"km.down({hidCode})"));

        public void KeyUp(int hidCode) => WriteCommand(FormattableString.Invariant($"km.up({hidCode})"));

        public void KeyPress(int hidCode) => WriteCommand(FormattableString.Invariant($"km.press({hidCode})"));

        public bool TryReadKeyState(int hidCode, out HardwareInputState state) => TryQueryState(FormattableString.Invariant($"km.isdown({hidCode})"), out state);

        public void MouseMoveBy(int dx, int dy)
        {
            WriteCommand(FormattableString.Invariant($"km.move({dx}, {dy})"));
            lock (_syncRoot)
            {
                if (_cursorX.HasValue) _cursorX += dx;
                if (_cursorY.HasValue) _cursorY += dy;
            }
        }

        public void MouseMoveTo(int x, int y)
        {
            var (currentX, currentY) = GetTrackedCursorPosition();
            MouseMoveBy(x - currentX, y - currentY);
        }

        public void MouseButtonDown(HardwareMouseButton button) => WriteCommand(FormattableString.Invariant($"km.{GetButtonName(button)}(1)"));

        public void MouseButtonUp(HardwareMouseButton button) => WriteCommand(FormattableString.Invariant($"km.{GetButtonName(button)}(0)"));

        public void MouseButtonClick(HardwareMouseButton button, int count)
        {
            if (count <= 1)
            {
                WriteCommand(FormattableString.Invariant($"km.click({(int)button})"));
                return;
            }

            WriteCommand(FormattableString.Invariant($"km.click({(int)button}, {count})"));
        }

        public void MouseWheelVertical(int delta)
        {
            if (delta != 0)
            {
                WriteCommand(FormattableString.Invariant($"km.wheel({delta.ToString(CultureInfo.InvariantCulture)})"));
            }
        }

        public void MouseWheelHorizontal(int delta)
        {
            if (delta == 0 || _horizontalWheelWarningShown)
            {
                return;
            }

            _horizontalWheelWarningShown = true;
            _logger?.LogWarning("Ferrum KM does not expose horizontal wheel in current backend.");
        }

        public bool TryReadMouseButtonState(HardwareMouseButton button, out HardwareInputState state)
        {
            return TryQueryState(FormattableString.Invariant($"km.{GetButtonName(button)}()"), out state);
        }

        private void WriteCommand(string command)
        {
            lock (_syncRoot)
            {
                if (!EnsureConnected())
                {
                    return;
                }

                try
                {
                    _serialPort!.Write($"{command}\r");
                    _serialPort.BaseStream.Flush();
                    if (_serialPort.BytesToRead > 0)
                    {
                        _serialPort.DiscardInBuffer();
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Ferrum KM command failed: {Command}", command);
                    SafeClosePort();
                }
            }
        }

        private bool TryQueryState(string command, out HardwareInputState state)
        {
            state = HardwareInputState.None;

            lock (_syncRoot)
            {
                if (!EnsureConnected())
                {
                    return false;
                }

                try
                {
                    _serialPort!.DiscardInBuffer();
                    _serialPort.Write($"{command}\r");
                    _serialPort.BaseStream.Flush();
                    var response = ReadResponseUnsafe();
                    if (TryParseState(response, out state))
                    {
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogDebug(ex, "Ferrum KM state query failed: {Command}", command);
                    SafeClosePort();
                }
            }

            return false;
        }

        private string ReadResponseUnsafe()
        {
            if (_serialPort?.IsOpen != true)
            {
                return string.Empty;
            }

            var builder = new StringBuilder();
            var deadline = Environment.TickCount64 + 120;

            while (Environment.TickCount64 < deadline)
            {
                Thread.Sleep(10);

                if (_serialPort.BytesToRead <= 0)
                {
                    if (builder.Length > 0)
                    {
                        break;
                    }

                    continue;
                }

                builder.Append(_serialPort.ReadExisting());
            }

            return builder.ToString();
        }

        private (int X, int Y) GetTrackedCursorPosition()
        {
            lock (_syncRoot)
            {
                if (_cursorX.HasValue && _cursorY.HasValue)
                {
                    return (_cursorX.Value, _cursorY.Value);
                }

                if (User32.GetCursorPos(out var point))
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

        private void SafeClosePort()
        {
            try
            {
                if (_serialPort?.IsOpen == true)
                {
                    _serialPort.Close();
                }
            }
            catch
            {
            }
            finally
            {
                _serialPort?.Dispose();
                _serialPort = null;
            }
        }

        private static string GetButtonName(HardwareMouseButton button)
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

        private static bool TryParseState(string response, out HardwareInputState state)
        {
            for (var i = response.Length - 1; i >= 0; i--)
            {
                state = response[i] switch
                {
                    '0' => HardwareInputState.None,
                    '1' => HardwareInputState.Physical,
                    '2' => HardwareInputState.Hardware,
                    '3' => HardwareInputState.Both,
                    _ => (HardwareInputState)(-1)
                };

                if ((int)state >= 0)
                {
                    return true;
                }
            }

            state = HardwareInputState.None;
            return false;
        }
    }

    private sealed class FerrumKmKeyboardBackend(FerrumKmConnection connection) : IHardwareKeyboardBackend, IHardwareKeyboardStateBackend
    {
        public bool EnsureConnected() => connection.EnsureConnected();
        public void KeyDown(int hidCode) => connection.KeyDown(hidCode);
        public void KeyUp(int hidCode) => connection.KeyUp(hidCode);
        public void KeyPress(int hidCode) => connection.KeyPress(hidCode);
        public bool TryGetKeyState(int hidCode, out HardwareInputState state) => connection.TryReadKeyState(hidCode, out state);
    }

    private sealed class FerrumKmMouseBackend(FerrumKmConnection connection) : IHardwareMouseBackend, IHardwareMouseStateBackend
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
