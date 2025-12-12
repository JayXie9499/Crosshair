using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Color = System.Windows.Media.Color;
using MessageBox = System.Windows.MessageBox;

namespace Crosshair
{
    public partial class MainWindow : Window
    {
        // Win32 constants
        const int GWL_EXSTYLE = -20;
        const int WS_EX_TRANSPARENT = 0x00000020;
        const int WS_EX_LAYERED = 0x00080000;
        const int WS_EX_TOOLWINDOW = 0x00000080;
        const int WS_EX_NOACTIVATE = 0x08000000;
        const uint WINEVENT_OUTOFCONTEXT = 0;
        const uint EVENT_SYSTEM_FOREGROUND = 3;
        private NotifyIcon _trayIcon;
        private IntPtr _hHook;
        private readonly CrosshairConfig _config;
        private readonly WinEventDelegate _winEventDelegate;

        public MainWindow()
        {
            InitializeComponent();

            // 建立 Tray Icon
            _trayIcon = new NotifyIcon();
            _trayIcon.Visible = true;
            _trayIcon.Text = "Crosshair Overlay";

            var assembly = Assembly.GetExecutingAssembly();
            Stream? iconStream = assembly.GetManifestResourceStream("Crosshair.icon.ico");
            _trayIcon.Icon = iconStream == null ? SystemIcons.Application : new Icon(iconStream);

            var menu = new ContextMenuStrip();
            var appsMenu = new ToolStripMenuItem("Select Bind Window");
            appsMenu.DropDownOpening += (s1, e1) =>
            {
                int curPid = Process.GetCurrentProcess().Id;
                appsMenu.DropDownItems.Clear();
                foreach (var win in GetWindows())
                {
                    var item = new ToolStripMenuItem(win.Title);
                    item.Tag = win.hWnd;
                    item.Click += (s2, e2) =>
                        _config.Target = (IntPtr)((ToolStripMenuItem)s2).Tag;
                    appsMenu.DropDownItems.Add(item);
                }
            };
            menu.Items.Add(appsMenu);
            menu.Items.Add("Exit", null, (s, e) =>
            {
                UnhookWinEvent(_hHook);
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
                Environment.Exit(0);
            });
            _trayIcon.ContextMenuStrip = menu;

            // 載入設定檔
            _config = LoadCrosshairConfig();
            CenterDot.Fill = new SolidColorBrush(_config.Color);
            CenterDot.Width = _config.Size;
            CenterDot.Height = _config.Size;
            BorderEllipse.Width = _config.Size + 0.2;
            BorderEllipse.Height = _config.Size + 0.2;

            _winEventDelegate = new WinEventDelegate(ForegroundEventProc);
            _hHook = SetWinEventHook(
                EVENT_SYSTEM_FOREGROUND,
                EVENT_SYSTEM_FOREGROUND,
                IntPtr.Zero,
                _winEventDelegate,
                0, 0,
                WINEVENT_OUTOFCONTEXT
            );
        }

        private static CrosshairConfig LoadCrosshairConfig()
        {
            string currentDir = AppDomain.CurrentDomain.BaseDirectory ?? Environment.CurrentDirectory;
            string configPath = Path.Combine(currentDir, "config.txt");
            if (!File.Exists(configPath))
            {
                return new CrosshairConfig(3.5, Colors.Red);
            }

            string content = File.ReadAllText(configPath).Trim();
            Regex regex = new Regex(@"(\d+(?:\.\d+)?) #([a-f|\d]{6})", RegexOptions.IgnoreCase);
            Match match = regex.Match(content);
            if (String.IsNullOrEmpty(content) || !match.Success)
            {
                MessageBox.Show("config.txt格式錯誤！", "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
                Environment.Exit(0);
            }

            try
            {
                string hex = match.Groups[2].Value;
                byte r = Convert.ToByte(hex.Substring(0, 2), 16);
                byte g = Convert.ToByte(hex.Substring(2, 2), 16);
                byte b = Convert.ToByte(hex.Substring(4, 2), 16);
                return new CrosshairConfig(double.Parse(match.Groups[1].Value), Color.FromRgb(r, g, b));
            }
            catch
            {
                MessageBox.Show("config.txt讀取失敗！", "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
                Environment.Exit(0);
                return default;
            }
        }

        private void ForegroundEventProc(IntPtr hWinEventHook, uint eventType, IntPtr hWnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            this.Dispatcher.Invoke(() =>
            {
                if (_config.Target == null)
                {
                    return;
                }
                if (hWnd == _config.Target && !this.IsVisible)
                {
                    this.Show();
                }
                else if (hWnd != _config.Target && this.IsVisible)
                {
                    this.Hide();
                }
            });
        }

        private void Window_SourceInitialized(object sender, EventArgs e)
        {
            var hwnd = new WindowInteropHelper(this).Handle;

            // 設為 Layered + Transparent + ToolWindow (不出現在 Alt+Tab)
            IntPtr ex = GetWindowLongPtr(hwnd, GWL_EXSTYLE);
            long newEx = ex.ToInt64() | WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
            SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(newEx));
        }

        // 避免 Alt+F4 關閉
        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            base.OnClosing(e);
            e.Cancel = true;
        }

        private List<(IntPtr hWnd, string Title)> GetWindows()
        {
            string[] excludedProc =
            [
                "Crosshair",
                "explorer",
                "TextInputHost"
            ];
            var result = new List<(IntPtr, string)>();
            EnumWindows((hWnd, lParam) =>
            {
                _ = GetWindowThreadProcessId(hWnd, out uint pid);
                var proc = Process.GetProcessById((int)pid);
                if (!IsWindowVisible(hWnd) || excludedProc.Contains(proc.ProcessName, StringComparer.OrdinalIgnoreCase))
                {
                    return true;
                }

                StringBuilder sb = new StringBuilder(256);
                GetWindowText(hWnd, sb, sb.Capacity);
                string title = sb.ToString();
                if (!string.IsNullOrWhiteSpace(title))
                {
                    result.Add((hWnd, title));
                }
                return true;
            }, IntPtr.Zero);
            return result;
        }

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        private delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType, IntPtr hWnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

        #region PInvoke (64/32 bit safe)
        static IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex)
        {
            if (IntPtr.Size == 8)
                return GetWindowLongPtr64(hWnd, nIndex);
            else
                return new IntPtr(GetWindowLong32(hWnd, nIndex));
        }

        static IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong)
        {
            if (IntPtr.Size == 8)
                return SetWindowLongPtr64(hWnd, nIndex, dwNewLong);
            else
                return new IntPtr(SetWindowLong32(hWnd, nIndex, dwNewLong.ToInt32()));
        }

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr", SetLastError = true)]
        static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr", SetLastError = true)]
        static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll", EntryPoint = "GetWindowLong", SetLastError = true)]
        static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLong", SetLastError = true)]
        static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll")]
        static extern uint GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

        [DllImport("user32.dll")]
        static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc, WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

        [DllImport("user32.dll")]
        static extern bool UnhookWinEvent(IntPtr hWinEventHook);
        #endregion
    }

    public class CrosshairConfig
    {
        public required double Size { get; init; }
        public required Color Color { get; init; }
        public IntPtr? Target { get; set; }

        [SetsRequiredMembers]
        public CrosshairConfig(double size, Color color) =>
            (Size, Color) = (size, color);
    }
}
