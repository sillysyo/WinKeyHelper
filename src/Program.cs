using Microsoft.Win32;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace WinKeyHelper;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        using var mutex = new Mutex(true, "WinKeyHelper.SingleInstance", out var createdNew);
        if (!createdNew)
            return;

        StickyKeys.DisableShortcut();
        Application.Run(new TrayApp());
    }
}

internal sealed class TrayApp : ApplicationContext
{
    private readonly NotifyIcon tray;
    private readonly ContextMenuStrip menu;
    private readonly ToolStripMenuItem blockWinItem;
    private readonly ToolStripMenuItem startupItem;
    private readonly WinKeyHook winHook;

    public TrayApp()
    {
        menu = new ContextMenuStrip();

        blockWinItem = new ToolStripMenuItem("屏蔽 Windows 键")
        {
            CheckOnClick = true,
            Checked = true
        };
        blockWinItem.CheckedChanged += (_, _) => winHook.Blocked = blockWinItem.Checked;
        menu.Items.Add(blockWinItem);

        var switchIme = new ToolStripMenuItem("切换英文输入法");
        switchIme.Click += (_, _) => InputMethod.SwitchToEnglish();
        menu.Items.Add(switchIme);

        menu.Items.Add(new ToolStripSeparator());

        startupItem = new ToolStripMenuItem("开机自动启动")
        {
            CheckOnClick = true,
            Checked = Startup.IsEnabled
        };
        startupItem.CheckedChanged += (_, _) => Startup.SetEnabled(startupItem.Checked);
        menu.Items.Add(startupItem);

        var disableSticky = new ToolStripMenuItem("关闭黏滞键快捷键");
        disableSticky.Click += (_, _) =>
        {
            StickyKeys.DisableShortcut();
            MessageBox.Show(
                "已关闭 Shift 连按 5 次的黏滞键快捷键。\n\n该设置写入当前 Windows 用户的注册表，并会长期生效。",
                "WinKeyHelper",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        };
        menu.Items.Add(disableSticky);

        menu.Items.Add(new ToolStripSeparator());

        var exit = new ToolStripMenuItem("退出");
        exit.Click += (_, _) => ExitThread();
        menu.Items.Add(exit);

        tray = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "WinKeyHelper",
            Visible = true,
            ContextMenuStrip = menu
        };
        tray.DoubleClick += (_, _) => winHook.Blocked = !winHook.Blocked;

        winHook = new WinKeyHook { Blocked = true };
        winHook.BlockedChanged += (_, blocked) => blockWinItem.Checked = blocked;
        winHook.Install();
        winHook.RegisterToggleHotkey();
    }

    protected override void ExitThreadCore()
    {
        winHook.UnregisterToggleHotkey();
        winHook.Dispose();
        tray.Visible = false;
        tray.Dispose();
        menu.Dispose();
        base.ExitThreadCore();
    }
}

internal sealed class WinKeyHook : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_HOTKEY = 0x0312;
    private const int MOD_CONTROL = 0x0002;
    private const int MOD_ALT = 0x0001;
    private const int HOTKEY_ID = 0x4242;
    private const int VK_LWIN = 0x5B;
    private const int VK_RWIN = 0x5C;

    private readonly LowLevelKeyboardProc callback;
    private readonly NativeWindow messageWindow;
    private IntPtr hook;
    private bool blocked;

    public bool Blocked
    {
        get => blocked;
        set
        {
            if (blocked == value)
                return;

            blocked = value;
            BlockedChanged?.Invoke(this, blocked);
        }
    }

    public event EventHandler<bool>? BlockedChanged;

    public WinKeyHook()
    {
        callback = HookCallback;
        messageWindow = new HotkeyWindow(this);
    }

    public void Install()
    {
        using var process = Process.GetCurrentProcess();
        using var module = process.MainModule!;

        hook = SetWindowsHookEx(
            WH_KEYBOARD_LL,
            callback,
            GetModuleHandle(module.ModuleName),
            0);

        if (hook == IntPtr.Zero)
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
    }

    public void RegisterToggleHotkey()
    {
        if (!RegisterHotKey(
                messageWindow.Handle,
                HOTKEY_ID,
                MOD_CONTROL | MOD_ALT,
                (uint)'W'))
        {
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }
    }

    public void UnregisterToggleHotkey()
    {
        UnregisterHotKey(messageWindow.Handle, HOTKEY_ID);
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 &&
            Blocked &&
            (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN))
        {
            var info = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);

            if (info.vkCode == VK_LWIN || info.vkCode == VK_RWIN)
                return (IntPtr)1;
        }

        return CallNextHookEx(hook, nCode, wParam, lParam);
    }

    private void Toggle() => Blocked = !Blocked;

    public void Dispose()
    {
        if (hook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(hook);
            hook = IntPtr.Zero;
        }

        messageWindow.DestroyHandle();
    }

    private sealed class HotkeyWindow : NativeWindow
    {
        private readonly WinKeyHook owner;

        public HotkeyWindow(WinKeyHook owner)
        {
            this.owner = owner;
            CreateHandle(new CreateParams());
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY && m.WParam == (IntPtr)HOTKEY_ID)
                owner.Toggle();

            base.WndProc(ref m);
        }
    }

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(
        int idHook,
        LowLevelKeyboardProc lpfn,
        IntPtr hMod,
        uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(
        IntPtr hhk,
        int nCode,
        IntPtr wParam,
        IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(
        IntPtr hWnd,
        int id,
        int fsModifiers,
        uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(
        IntPtr hWnd,
        int id);
}

internal static class InputMethod
{
    private const uint KLF_ACTIVATE = 0x00000001;
    private const uint WM_INPUTLANGCHANGEREQUEST = 0x0050;
    private const uint HWND_BROADCAST = 0xffff;

    public static void SwitchToEnglish()
    {
        var hkl = LoadKeyboardLayout("00000409", KLF_ACTIVATE);

        if (hkl == IntPtr.Zero)
        {
            MessageBox.Show(
                "无法加载 English (US) 输入法。请确认 Windows 已安装英语键盘布局。",
                "WinKeyHelper",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        ActivateKeyboardLayout(hkl, KLF_ACTIVATE);
        PostMessage(
            (IntPtr)HWND_BROADCAST,
            WM_INPUTLANGCHANGEREQUEST,
            IntPtr.Zero,
            hkl);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadKeyboardLayout(
        string pwszKLID,
        uint Flags);

    [DllImport("user32.dll")]
    private static extern IntPtr ActivateKeyboardLayout(
        IntPtr hkl,
        uint Flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessage(
        IntPtr hWnd,
        uint msg,
        IntPtr wParam,
        IntPtr lParam);
}

internal static class StickyKeys
{
    private const string Path = @"Control Panel\Accessibility\StickyKeys";

    public static void DisableShortcut()
    {
        using var key = Registry.CurrentUser.CreateSubKey(Path, writable: true);
        key?.SetValue("Flags", "506", RegistryValueKind.String);
    }
}

internal static class Startup
{
    private const string RunPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string Name = "WinKeyHelper";

    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunPath, writable: false);
            var value = key?.GetValue(Name) as string;
            return !string.IsNullOrWhiteSpace(value);
        }
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunPath, writable: true);
        if (key == null)
            return;

        if (enabled)
            key.SetValue(Name, $"\"{Application.ExecutablePath}\"");
        else
            key.DeleteValue(Name, throwOnMissingValue: false);
    }
}
