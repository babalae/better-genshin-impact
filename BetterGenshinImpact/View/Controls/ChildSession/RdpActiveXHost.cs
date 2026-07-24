using System;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using BetterGenshinImpact.Service.ChildSession;
using DrawingSize = System.Drawing.Size;

namespace BetterGenshinImpact.View.Controls.ChildSession;

internal sealed class RdpActiveXHost : AxHost
{
    // Windows 10+ 自带的非脚本化 RDP ActiveX 控件（MsRdpClient10）。
    private const string RdpClientClsid = "A0C63C30-F08D-4AB4-907C-34905D770C7D";
    private const short VariantFalse = 0;
    private const short VariantTrue = -1;

    private static readonly RemoteKey LeftWindowsKey = new(0x5B, IsExtended: true);
    private static readonly RemoteKey DKey = new(0x20, IsExtended: false);
    private static readonly RemoteKey TabKey = new(0x0F, IsExtended: false);
    private bool _smartSizingEnabled = true;

    internal RdpActiveXHost()
        : base(RdpClientClsid)
    {
        Dock = DockStyle.Fill;
    }

    internal int ConnectedState
    {
        get
        {
            if (!IsHandleCreated)
            {
                return 0;
            }

            return Convert.ToInt32(GetComProperty(GetOcx(), "Connected"), CultureInfo.InvariantCulture);
        }
    }

    internal void ConnectToChildSession(DrawingSize desktopSize)
    {
        if (ConnectedState != 0)
        {
            return;
        }

        var client = GetOcx();
        var width = Math.Clamp(desktopSize.Width, 200, 8192);
        var height = Math.Clamp(desktopSize.Height, 200, 8192);

        SetComProperty(client, "Server", "localhost");
        SetComProperty(client, "DesktopWidth", width);
        SetComProperty(client, "DesktopHeight", height);
        SetComProperty(client, "ColorDepth", 32);
        SetComProperty(client, "ConnectingText", "正在创建 BetterGI 桌面分身...");
        SetComProperty(client, "DisconnectedText", "BetterGI 桌面分身已断开");

        var advancedSettings = GetComProperty(client, "AdvancedSettings7")
            ?? throw new COMException("RDP ActiveX 未返回 AdvancedSettings7。");
        RunComStep("启用 CredSSP", () =>
            SetComProperty(advancedSettings, "EnableCredSspSupport", true));
        RunComStep("启用远程 Windows 键", () =>
            SetComProperty(advancedSettings, "EnableWindowsKey", 1));
        RunComStep("设置显示缩放", () =>
            SetComProperty(advancedSettings, "SmartSizing", _smartSizingEnabled));

        object connectToChildSession = true;
        RunComStep("设置 ConnectToChildSession", () =>
        {
            var extendedSettings = (IMsRdpExtendedSettings)client;
            TrySetExtendedProperty(extendedSettings, "EnableZoom", true);
            extendedSettings.set_Property("ConnectToChildSession", ref connectToChildSession);
        });

        RunComStep("调用 RDP Connect()", () => InvokeComMethod(client, "Connect"));
    }

    internal void SendShowDesktopShortcut()
    {
        KeyStroke[] strokes =
        [
            new KeyStroke(LeftWindowsKey, IsKeyUp: false),
            new KeyStroke(DKey, IsKeyUp: false),
            new KeyStroke(DKey, IsKeyUp: true),
            new KeyStroke(LeftWindowsKey, IsKeyUp: true)
        ];

        SendShortcut(strokes, "Win+D");
    }

    internal void SendTaskViewShortcut()
    {
        KeyStroke[] strokes =
        [
            new KeyStroke(LeftWindowsKey, IsKeyUp: false),
            new KeyStroke(TabKey, IsKeyUp: false),
            new KeyStroke(TabKey, IsKeyUp: true),
            new KeyStroke(LeftWindowsKey, IsKeyUp: true)
        ];

        SendShortcut(strokes, "Win+Tab");
    }

    internal void SetSmartSizing(bool enabled)
    {
        _smartSizingEnabled = enabled;
        if (!IsHandleCreated)
        {
            return;
        }

        var advancedSettings = GetComProperty(GetOcx(), "AdvancedSettings7")
            ?? throw new COMException("RDP ActiveX 未返回 AdvancedSettings7。");
        RunComStep("设置显示缩放", () =>
            SetComProperty(advancedSettings, "SmartSizing", enabled));
    }

    internal void DisconnectSession()
    {
        if (ConnectedState != 0)
        {
            InvokeComMethod(GetOcx(), "Disconnect");
        }
    }

    private void SendShortcut(KeyStroke[] strokes, string displayName)
    {
        if (ConnectedState != 1)
        {
            throw new InvalidOperationException(
                $"桌面分身尚未完全连接，无法发送 {displayName}。");
        }

        if (!ChildSessionNativeMethods.TryFocusRdpInputWindow(Handle))
        {
            throw new InvalidOperationException("无法将键盘焦点切换到桌面分身。");
        }

        RunComStep($"向 Child Session 发送 {displayName}", () => SendKeyStrokes(strokes));
    }

    private void SendKeyStrokes(KeyStroke[] strokes)
    {
        var keyUpStates = new short[strokes.Length];
        var keyData = new int[strokes.Length];

        for (var index = 0; index < strokes.Length; index++)
        {
            var stroke = strokes[index];
            keyUpStates[index] = stroke.IsKeyUp ? VariantTrue : VariantFalse;
            keyData[index] = CreateRdpScanCode(stroke.Key);
        }

        var nonScriptableClient = (IMsRdpClientNonScriptable)GetOcx();
        nonScriptableClient.SendKeys(
            strokes.Length,
            ref keyUpStates[0],
            ref keyData[0]);
    }

    private static int CreateRdpScanCode(RemoteKey key)
    {
        const int extendedScanCodeFlag = 0x0100;
        return key.ScanCode | (key.IsExtended ? extendedScanCodeFlag : 0);
    }

    private static object? GetComProperty(object target, string propertyName)
    {
        return target.GetType().InvokeMember(
            propertyName,
            BindingFlags.GetProperty,
            binder: null,
            target,
            args: null,
            CultureInfo.InvariantCulture);
    }

    private static void SetComProperty(object target, string propertyName, object value)
    {
        target.GetType().InvokeMember(
            propertyName,
            BindingFlags.SetProperty,
            binder: null,
            target,
            [value],
            CultureInfo.InvariantCulture);
    }

    private static void TrySetExtendedProperty(
        IMsRdpExtendedSettings extendedSettings,
        string propertyName,
        object value)
    {
        try
        {
            extendedSettings.set_Property(propertyName, ref value);
        }
        catch (COMException)
        {
            // 旧版 MsTscAx 可能不支持该扩展属性，继续使用原有显示行为。
        }
    }

    private static object? InvokeComMethod(object target, string methodName, params object[]? args)
    {
        return target.GetType().InvokeMember(
            methodName,
            BindingFlags.InvokeMethod,
            binder: null,
            target,
            args,
            CultureInfo.InvariantCulture);
    }

    private static void RunComStep(string stepName, Action action)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            var actualException = exception.GetBaseException();
            if (actualException is COMException comException)
            {
                throw new COMException(
                    $"{stepName}失败：{comException.Message}",
                    comException.ErrorCode);
            }

            throw;
        }
    }

    [ComImport]
    [Guid("302D8188-0052-4807-806A-362B628F9AC5")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMsRdpExtendedSettings
    {
        void set_Property(
            [In, MarshalAs(UnmanagedType.BStr)] string propertyName,
            [In, MarshalAs(UnmanagedType.Struct)] ref object value);

        [return: MarshalAs(UnmanagedType.Struct)]
        object get_Property([In, MarshalAs(UnmanagedType.BStr)] string propertyName);
    }

    [ComImport]
    [Guid("2F079C4C-87B2-4AFD-97AB-20CDB43038AE")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMsRdpClientNonScriptable
    {
        void put_ClearTextPassword([In, MarshalAs(UnmanagedType.BStr)] string value);

        void put_PortablePassword([In, MarshalAs(UnmanagedType.BStr)] string value);

        [return: MarshalAs(UnmanagedType.BStr)]
        string get_PortablePassword();

        void put_PortableSalt([In, MarshalAs(UnmanagedType.BStr)] string value);

        [return: MarshalAs(UnmanagedType.BStr)]
        string get_PortableSalt();

        void put_BinaryPassword([In, MarshalAs(UnmanagedType.BStr)] string value);

        [return: MarshalAs(UnmanagedType.BStr)]
        string get_BinaryPassword();

        void put_BinarySalt([In, MarshalAs(UnmanagedType.BStr)] string value);

        [return: MarshalAs(UnmanagedType.BStr)]
        string get_BinarySalt();

        void ResetPassword();

        void NotifyRedirectDeviceChange(nuint wParam, nint lParam);

        void SendKeys(
            int numKeys,
            [In] ref short keyUpStates,
            [In] ref int keyData);
    }

    private readonly record struct RemoteKey(int ScanCode, bool IsExtended);

    private readonly record struct KeyStroke(RemoteKey Key, bool IsKeyUp);
}
