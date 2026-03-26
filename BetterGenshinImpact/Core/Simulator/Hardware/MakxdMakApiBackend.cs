using BetterGenshinImpact.Helpers;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO.Ports;
using System.Threading;
using Vanara.PInvoke;

namespace BetterGenshinImpact.Core.Simulator.Hardware;

internal static class MakxdMakApiBackend
{
    private static readonly object SyncRoot = new();
    private static readonly Dictionary<string, MakxdMakConnection> Connections = new(StringComparer.OrdinalIgnoreCase);

    public static IHardwareKeyboardBackend CreateKeyboard(string portName)
    {
        return new MakxdMakKeyboardBackend(GetConnection(portName));
    }

    public static IHardwareMouseBackend CreateMouse(string portName)
    {
        return new MakxdMakMouseBackend(GetConnection(portName));
    }

    private static MakxdMakConnection GetConnection(string portName)
    {
        lock (SyncRoot)
        {
            if (!Connections.TryGetValue(portName, out var connection))
            {
                connection = new MakxdMakConnection(portName);
                Connections[portName] = connection;
            }

            return connection;
        }
    }

    private sealed class MakxdMakConnection(string portName) : IHardwareApiConnection
    {
        private readonly object _syncRoot = new();
        private readonly ILogger? _logger = App.GetService<ILogger<MakxdMakConnection>>();
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
                    var area = PrimaryScreen.WorkingArea;
                    WriteCommandUnsafe(FormattableString.Invariant($"km.screen({area.Width}, {area.Height})"));
                    _cursorX = null;
                    _cursorY = null;
                    _logger?.LogInformation("Makxd MAK connected: {Port}", portName);
                    return true;
                }
                catch (Exception ex)
                {
                    SafeClosePort();
                    _logger?.LogWarning(ex, "Failed to connect Makxd MAK: {Port}", portName);
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
            WriteCommand(FormattableString.Invariant($"km.moveto({x}, {y})"));
            lock (_syncRoot)
            {
                _cursorX = x;
                _cursorY = y;
            }
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
            _logger?.LogWarning("Makxd MAK does not expose horizontal wheel in current backend.");
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
                    WriteCommandUnsafe(command);
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Makxd MAK command failed: {Command}", command);
                    SafeClosePort();
                }
            }
        }

        private void WriteCommandUnsafe(string command)
        {
            if (_serialPort?.IsOpen != true)
            {
                return;
            }

            _serialPort.Write($"{command}\r");
            _serialPort.BaseStream.Flush();
            if (_serialPort.BytesToRead > 0)
            {
                _serialPort.DiscardInBuffer();
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
    }

    private sealed class MakxdMakKeyboardBackend(MakxdMakConnection connection) : IHardwareKeyboardBackend
    {
        public bool EnsureConnected() => connection.EnsureConnected();
        public void KeyDown(int hidCode) => connection.KeyDown(hidCode);
        public void KeyUp(int hidCode) => connection.KeyUp(hidCode);
        public void KeyPress(int hidCode) => connection.KeyPress(hidCode);
    }

    private sealed class MakxdMakMouseBackend(MakxdMakConnection connection) : IHardwareMouseBackend
    {
        public bool EnsureConnected() => connection.EnsureConnected();
        public void MouseMoveBy(int dx, int dy) => connection.MouseMoveBy(dx, dy);
        public void MouseMoveTo(int x, int y) => connection.MouseMoveTo(x, y);
        public void MouseButtonDown(HardwareMouseButton button) => connection.MouseButtonDown(button);
        public void MouseButtonUp(HardwareMouseButton button) => connection.MouseButtonUp(button);
        public void MouseButtonClick(HardwareMouseButton button, int count) => connection.MouseButtonClick(button, count);
        public void MouseWheelVertical(int delta) => connection.MouseWheelVertical(delta);
        public void MouseWheelHorizontal(int delta) => connection.MouseWheelHorizontal(delta);
    }
}
