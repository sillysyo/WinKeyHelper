# WinKeyHelper

A tiny Windows tray utility for users who want a few keyboard conveniences without a background polling loop.

## Features

- Block **Left/Right Windows keys** globally.
- Toggle Win-key blocking with `Ctrl + Alt + W`.
- Double-click the tray icon to toggle Win-key blocking and switch to **English (US)**.
- Switch the active keyboard layout to **English (US)** from the tray menu.
- Disable the **Sticky Keys shortcut** triggered by pressing Shift five times.
- Optional startup with Windows.
- Single-instance, tray-only application.
- The tray and executable use the icon in `src/Assets/WinKeyHelper.ico`.

## Design

The Win-key blocker uses the Windows low-level keyboard hook (`WH_KEYBOARD_LL`) and does not poll the keyboard. The process therefore remains idle between input events.

The Sticky Keys shortcut setting is changed for the current Windows user under:

```text
HKCU\Control Panel\Accessibility\StickyKeys
Flags = 506
```

This utility does not require administrator privileges.

## Build

The repository includes a GitHub Actions workflow that builds a self-contained `win-x64` single-file executable. Self-contained publishing bundles the .NET runtime, so the executable is large. Windows Forms apps are not currently supported by .NET trimming, so the build does not enable trimming.

### Local build

On Windows with .NET 8 SDK installed:

```powershell
dotnet publish WinKeyHelper.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
```

The executable will be written to `publish\WinKeyHelper.exe`.

## License

MIT
