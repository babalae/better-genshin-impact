using BetterGenshinImpact.Helpers;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO.Ports;
using System.Text;
using System.Threading;

namespace BetterGenshinImpact.Core.Simulator.Hardware;

internal static class MakcuKmApiBackend
{
    private static readonly object SyncRoot = new();
    private static readonly Dictionary<string, MakcuKmConnection> Connections = new(StringComparer.OrdinalIgnoreCase);

    public static IHardwareMouseBackend CreateMouse(string portName)
    {
        return new MakcuKmMouseBackend(GetConnection(portName));
    }

    private static MakcuKmConnection GetConnection(string portName)
    {
        lock (SyncRoot)
        {
            if (!Connections.TryGetValue(portName, out var connection))
            {
                connection = new MakcuKmConnection(portName);
                Connections[portName] = connection;
            }

            return connection;
        }
    }

    private sealed class MakcuKmConnection(string portName) : IHardwareApiConnection
    {
        private static readonly byte[] HandshakeBytes = [0xDE, 0xAD, 0x05, 0x00, 0xA5, 0x00, 0x09, 0x3D, 0x00];
        private readonly object _syncRoot = new();
        private readonly ILogger? _logger = App.GetService<ILogger<MakcuKmConnection>>();
        private SerialPort? _serialPort;
        private bool _isDisposed;
        private bool _horizontalWheelWarningShown;
        private string _deviceVersion = string.Empty;

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
                        NewLine = "\n"
                    };
                    _serialPort.Open();
                    Thread.Sleep(150);
                    _serialPort.Write(HandshakeBytes, 0, HandshakeBytes.Length);
                    _serialPort.BaseStream.Flush();
                    _serialPort.BaudRate = 4_000_000;
                    TryReadVersion();
                    Thread.Sleep(150);
                    WriteCommandUnsafe("km.buttons(1)", "\r\n");
                    WriteCommandUnsafe("km.echo(0)", "\r\n");
                    TryDiscardInput();
                    _logger?.LogInformation("Makcu KM connected: {Port}, version: {Version}", portName, _deviceVersion);
                    return true;
                }
                catch (Exception ex)
                {
                    SafeClosePort();
                    _logger?.LogWarning(ex, "Failed to connect Makcu KM: {Port}", portName);
                    return false;
                }
            }
        }

        public string? BaudRateText
        {
            get
            {
                lock (_syncRoot)
                {
                    return _serialPort?.IsOpen == true
                        ? _serialPort.BaudRate.ToString(CultureInfo.InvariantCulture)
                        : null;
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

        public void MouseMoveBy(int dx, int dy) => WriteCommand(FormattableString.Invariant($"km.move({dx}, {dy})"));

        public void MouseMoveTo(int x, int y)
        {
            var area = PrimaryScreen.WorkingArea;
            WriteCommand(FormattableString.Invariant($"km.screen({area.Width}, {area.Height})"));
            WriteCommand(FormattableString.Invariant($"km.moveto({x}, {y})"));
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
            _logger?.LogWarning("Makcu KM does not expose horizontal wheel in current backend.");
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
                    WriteCommandUnsafe(command, "\r");
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Makcu KM command failed: {Command}", command);
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
                    _logger?.LogDebug(ex, "Makcu KM state query failed: {Command}", command);
                    SafeClosePort();
                }
            }

            return false;
        }

        private void WriteCommandUnsafe(string command, string terminator)
        {
            if (_serialPort?.IsOpen != true)
            {
                return;
            }

            _serialPort.Write($"{command}{terminator}");
            _serialPort.BaseStream.Flush();
            TryDiscardInput();
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

        private void TryReadVersion()
        {
            if (_serialPort?.IsOpen != true)
            {
                return;
            }

            try
            {
                _serialPort.Write("km.version()\r");
                Thread.Sleep(100);
                _deviceVersion = _serialPort.ReadLine().Trim();
            }
            catch (Exception ex)
            {
                _deviceVersion = string.Empty;
                _logger?.LogDebug(ex, "Makcu KM version probe failed on {Port}", portName);
            }
        }

        private void TryDiscardInput()
        {
            try
            {
                if (_serialPort?.BytesToRead > 0)
                {
                    _serialPort.DiscardInBuffer();
                }
            }
            catch
            {
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
                HardwareMouseButton.Side1 => "ms1",
                HardwareMouseButton.Side2 => "ms2",
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

    private sealed class MakcuKmMouseBackend(MakcuKmConnection connection) : IHardwareMouseBackend, IHardwareMouseStateBackend, IHardwareConnectionInfoProvider
    {
        public string? BaudRateText => connection.BaudRateText;
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
