# BetterGI Full English Patch

A portable, one-click English translation patcher for **BetterGI (Better Genshin Impact)**. It provides complete translation for the BetterGI GUI, system-wide universal translation for live in-game logs and all drop-down menus, and automatic translation for JavaScript scripts downloaded from the Script Repo — offered as an installable patch that smart-merges with BetterGI's existing `en.json` dictionary.

---

## Features

* **Complete BetterGI GUI & Menu Translation**: Hooks WPF, `Wpf.Ui`, `Wpf.Ui.Violeta`, and BetterGI custom controls (`TextBlock`, `Run`, `AccessText`, `Button`, `CheckBox`, `MenuItem`, `TabItem`, `Window`, `ToolTip`, `ToastControl`, `MessageBox`, `ContentDialog`, `InfoBar`, `Snackbar`, `TextBox` placeholders, `MultiSelectComboBox`, `TwoStateButton`, `CascadingComboBox`, etc.).
* **System-Wide Drop-Down & Script GUI Translation**: Translates JS script `settings.json` labels, checkboxes, multi-checkboxes, dropdowns (`ComboBox`), `CascadeSelector`s, `manifest.json` names/descriptions, pathing trees, and combat/TCG strategy names visually on the GUI without altering underlying script keys or values.
* **Script Repo & Downloaded JS Auto-Translation**: Forces the Script Repo UI to `en-US` and automatically translates downloaded JavaScript scripts, repo scripts, tags, descriptions, and READMEs inside BetterGI's embedded `WebView2` (`WebpagePanel`) for both local and online repository pages.
* **Universal Live Log Translation**: Translates BetterGI's on-screen running logs (`MaskWindow` `RichTextBox` / Serilog `Run`s) in real time while leaving log files on disk intact for Log Analysis.
* **3-Tier Auto-Translation**: Combines instant dictionary & pattern/glossary matching (`en.json`, 3,500+ strings) with a persistent `User\I18n\auto_cache.json` cache and a non-blocking background auto-translator for newly installed scripts and unseen repo content.

---

## What's Inside This Folder

* **`Apply-EnglishPatch.bat`** — Double-click to install or re-apply the English patch (and after any BetterGI update).
* **`Uninstall-EnglishPatch.bat`** — Double-click to revert `BetterGI.exe` and `User\I18n\en.json` back to stock.
* **`Apply-EnglishPatch.ps1`** — Smart-merges `en.json`, compiles/deploys `BgiEnHook.dll`, patches `BetterGI.exe`, and sets the UI language to English.
* **`en.json`** — Master English translation dictionary (3,500+ translated UI, dialog, toast, script, log, and Genshin Impact glossary strings).
* **`BgiEnHook.cs` / `BgiEnHook.dll`** — Portable .NET runtime `StartupHook` source and prebuilt DLL.

---

## Prerequisites

* Windows with **BetterGI** installed (`BetterGI.exe`).
* PowerShell (built into Windows).
* *(Optional)* .NET Framework 4.x (`C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe`) if you want `Apply-EnglishPatch.ps1` to recompile `BgiEnHook.cs` from source; otherwise the included `BgiEnHook.dll` is deployed automatically.

---

## How to Install and Use

1. **Download the `EnglishPatch` folder** (clone this repository or download it as a ZIP and extract it).
2. **Place `EnglishPatch` inside your BetterGI installation folder** (the folder containing `BetterGI.exe`, e.g. `D:\Games\BetterGI\EnglishPatch`).
   * *Alternative*: You can keep `EnglishPatch` anywhere and either select your BetterGI folder when prompted, or run:
     ```powershell
     powershell -NoProfile -ExecutionPolicy Bypass -File ".\Apply-EnglishPatch.ps1" -BetterGiDir "D:\Games\BetterGI"
     ```
3. **Close BetterGI** completely if it is running (the installer will also offer to close it for you).
4. **Double-click `Apply-EnglishPatch.bat`**. The script will:
   * Back up your stock `User\I18n\en.json` to `User\I18n\en.json.bak` and `BetterGI.exe` to `BetterGI.exe.bak`.
   * Smart-merge `en.json` with any existing keys in `User\I18n\en.json`.
   * Compile/deploy `BgiEnHook.dll` next to `BetterGI.exe`.
   * Patch `BetterGI.exe` to load `BgiEnHook.dll` at startup and set the UI language to English.
5. **Launch `BetterGI.exe`** as normal.

---

## How to Re-Apply After a BetterGI Update

Whenever BetterGI updates to a new version:
1. Make sure BetterGI is closed.
2. Open the `EnglishPatch` folder and double-click **`Apply-EnglishPatch.bat`**.
3. Launch `BetterGI.exe` as normal.

> **Smart Merge**: The patcher automatically preserves any brand-new keys introduced by a new BetterGI release while overlaying all English translations on top.

---

## How to Uninstall

1. Make sure BetterGI is closed.
2. Open the `EnglishPatch` folder and double-click **`Uninstall-EnglishPatch.bat`** (or run `Apply-EnglishPatch.ps1 -Uninstall`).
3. This restores `BetterGI.exe` from `BetterGI.exe.bak`, restores `User\I18n\en.json` from `en.json.bak`, and removes `BgiEnHook.dll`.
