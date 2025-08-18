using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
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
        private NotifyIcon _trayIcon;
        private readonly DispatcherTimer _timer;
        private readonly CrosshairConfig _config;

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
            menu.Items.Add("Exit", null, (s, e) =>
            {
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
                Environment.Exit(0);
            });
            _trayIcon.ContextMenuStrip = menu;

            // 載入設定檔
            _config = LoadCrosshairConfig();
            CenterDot.Fill = new SolidColorBrush(_config.color);
            CenterDot.Width = _config.size;
            CenterDot.Height = _config.size;
            BorderEllipse.Width = _config.size + 0.2;
            BorderEllipse.Height = _config.size + 0.2;

            // 動態顯示 Timer
            _timer = new DispatcherTimer();
            _timer.Interval = TimeSpan.FromSeconds(1);
            _timer.Tick += CheckTopWindow;
            _timer.Start();

            this.Hide();
        }

        private CrosshairConfig LoadCrosshairConfig()
        {
            CrosshairConfig config = new CrosshairConfig();
            string currentDir = AppDomain.CurrentDomain.BaseDirectory ?? Environment.CurrentDirectory;
            string configPath = Path.Combine(currentDir, "config.txt");
            if (!File.Exists(configPath))
            {
                MessageBox.Show("config.txt不存在！", "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
                Environment.Exit(0);
            }

            string content = File.ReadAllText(configPath).Trim();
            if (String.IsNullOrEmpty(content))
            {
                MessageBox.Show("config.txt是空的！", "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
                Environment.Exit(0);
            }

            Regex regex = new Regex(@"([a-z0-9_\-\. ]+\.exe) (\d+(?:\.\d+)?) #([a-f|\d]{6})", RegexOptions.IgnoreCase);
            Match match = regex.Match(content);
            if (!match.Success)
            {
                MessageBox.Show("config.txt格式錯誤！", "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
                Environment.Exit(0);
            }

            try
            {
                string hex = match.Groups[3].Value;
                byte r = Convert.ToByte(hex.Substring(0, 2), 16);
                byte g = Convert.ToByte(hex.Substring(2, 2), 16);
                byte b = Convert.ToByte(hex.Substring(4, 2), 16);
                config.color = Color.FromRgb(r, g, b);
                config.size = double.Parse(match.Groups[2].Value);
                config.targetExe = match.Groups[1].Value;
            }
            catch {}

            return config;
        }

        private void CheckTopWindow(object? sender, EventArgs e)
        {
            IntPtr hWnd = GetForegroundWindow();
            if (hWnd == IntPtr.Zero)
            {
                return;
            }

            _ = GetWindowThreadProcessId(hWnd, out uint procId);
            try
            {
                Process proc = Process.GetProcessById((int)procId);
                string procName = proc.MainModule?.FileName ?? "";
                if (procName.EndsWith(_config.targetExe, StringComparison.OrdinalIgnoreCase))
                {
                    this.Show();
                }
                else
                {
                    this.Hide();
                }
            }
            catch {}
        }

        private void Window_SourceInitialized(object sender, EventArgs e)
        {
            var hwnd = new WindowInteropHelper(this).Handle;

            // 設為 Layered + Transparent + ToolWindow (不出現在 Alt+Tab)
            IntPtr ex = GetWindowLongPtr(hwnd, GWL_EXSTYLE);
            long newEx = ex.ToInt64() | WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW;
            SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(newEx));
        }

        // 避免 Alt+F4 關閉
        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            base.OnClosing(e);
            e.Cancel = true;
        }

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
        static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", SetLastError = true)]
        static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
        #endregion
    }

    public class CrosshairConfig
    {
        public Color color { get; set; }
        public double size { get; set; }
        public string targetExe { get; set; }
    }
}
