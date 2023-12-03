using System;
using System.Runtime.InteropServices;

namespace MicaSetup.Natives;

public static class UxTheme
{
    [DllImport(Lib.UxTheme, SetLastError = false, ExactSpelling = true)]
    public static extern int SetWindowThemeAttribute(nint hwnd, WINDOWTHEMEATTRIBUTETYPE eAttribute, in WTA_OPTIONS pvAttribute, uint cbAttribute);

    public enum WINDOWTHEMEATTRIBUTETYPE
    {
        WTA_NONCLIENT = 1,
    }

    [Flags]
    public enum ThemeDialogTextureFlags
    {
        /// <summary>Disables background texturing.</summary>
        ETDT_DISABLE = 0x00000001,

        /// <summary>Enables dialog window background texturing. The texturing is defined by a visual style.</summary>
        ETDT_ENABLE = 0x00000002,

        /// <summary>Uses the Tab control texture for the background texture of a dialog window.</summary>
        ETDT_USETABTEXTURE = 0x00000004,

        /// <summary>
        /// Enables dialog window background texturing. The texture is the Tab control texture defined by the visual style. This flag is
        /// equivalent to (ETDT_ENABLE | ETDT_USETABTEXTURE).
        /// </summary>
        ETDT_ENABLETAB = (ETDT_ENABLE | ETDT_USETABTEXTURE),

        /// <summary>Uses the Aero wizard texture for the background texture of a dialog window.</summary>
        ETDT_USEAEROWIZARDTABTEXTURE = 0x00000008,

        /// <summary>ETDT_ENABLE | ETDT_USEAEROWIZARDTABTEXTURE.</summary>
        ETDT_ENABLEAEROWIZARDTAB = (ETDT_ENABLE | ETDT_USEAEROWIZARDTABTEXTURE),

        /// <summary>ETDT_DISABLE | ETDT_ENABLE | ETDT_USETABTEXTURE | ETDT_USEAEROWIZARDTABTEXTURE.</summary>
        ETDT_VALIDBITS = (ETDT_DISABLE | ETDT_ENABLE | ETDT_USETABTEXTURE | ETDT_USEAEROWIZARDTABTEXTURE),
    }

    [Flags]
    public enum WTNCA
    {
        /// <summary>Prevents the window caption from being drawn.</summary>
        WTNCA_NODRAWCAPTION = 0x00000001,

        /// <summary>Prevents the system icon from being drawn.</summary>
        WTNCA_NODRAWICON = 0x00000002,

        /// <summary>Prevents the system icon menu from appearing.</summary>
        WTNCA_NOSYSMENU = 0x00000004,

        /// <summary>Prevents mirroring of the question mark, even in right-to-left (RTL) layout.</summary>
        WTNCA_NOMIRRORHELP = 0x00000008
    }

    public struct WTA_OPTIONS
    {
        public WTNCA Flags;
        public uint Mask;
    }
}
