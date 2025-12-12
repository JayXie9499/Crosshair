using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Reflection;
using System.Resources.Extensions;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using Color = System.Windows.Media.Color;
using MessageBox = System.Windows.MessageBox;
using ColorConverter = System.Windows.Media.ColorConverter;

namespace Crosshair
{
    public enum CrosshairType
    {
        Dot,
        Cross
    }

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

            _config = LoadCrosshairConfig();
            ApplyCrosshairStyle();

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
            string configPath = Path.Combine(currentDir, "config.yml");
            if (!File.Exists(configPath))
            {
                AppConfig defaultConfig = new AppConfig();
                SaveConfig(configPath, defaultConfig);
                return new CrosshairConfig(defaultConfig);
            }

            try
            {
                string content = File.ReadAllText(configPath).Trim();
                var deserializer = new DeserializerBuilder()
                    .WithNamingConvention(CamelCaseNamingConvention.Instance)
                    .Build();
                AppConfig appConfig = deserializer.Deserialize<AppConfig>(content);
                return new CrosshairConfig(appConfig);

            }
            catch (Exception ex)
            {
                MessageBox.Show($"config.yml 讀取失敗！\n錯誤: {ex.Message}", "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
                return new CrosshairConfig(new AppConfig());
            }
        }

        private static void SaveConfig(string path, AppConfig config)
        {
            try
            {
                var serializer = new SerializerBuilder()
                    .WithNamingConvention(CamelCaseNamingConvention.Instance)
                    .Build();
                string yaml = serializer.Serialize(config);
                File.WriteAllText(path, yaml);
            }
            catch { }
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

        private void ApplyCrosshairStyle()
        {
            SolidColorBrush mainBrush = new SolidColorBrush(_config.Color);
            if (_config.Type == CrosshairType.Dot)
            {
                CrossRoot.Visibility = Visibility.Collapsed;
                DotRoot.Visibility = Visibility.Visible;
                DotCore.Fill = mainBrush;
                DotCore.Width = _config.Size;
                DotCore.Height = _config.Size;
                DotBorder.Width = _config.Size + _config.BorderWidth;
                DotBorder.Height = _config.Size + _config.BorderWidth;
            }
            else if (_config.Type == CrosshairType.Cross)
            {
                DotRoot.Visibility = Visibility.Collapsed;
                CrossRoot.Visibility = Visibility.Visible;
                CrossCore.Stroke = mainBrush;
                CrossCore.Width = _config.Size;
                CrossCore.Height = _config.Size;
                CrossCore.StrokeThickness = _config.Thickness;
                CrossBorder.StrokeThickness = _config.Thickness + (_config.BorderWidth * 2);

                double borderSize = _config.Size + (_config.BorderWidth * 2);
                CrossBorder.Width = borderSize;
                CrossBorder.Height = borderSize;
            }
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

    public class AppConfig
    {
        public string Type { get; set; } = "Dot";
        public double Size { get; set; } = 3.5;
        public double Thickness { get; set; } = 2.0;
        public double BorderWidth { get; set; } = 1;
        public string Color { get; set; } = "#FF0000";
    }

    public class CrosshairConfig
    {
        public required CrosshairType Type { get; init; }
        public required double Size { get; init; }
        public required double Thickness { get; init; }
        public required double BorderWidth { get; init; }
        public required Color Color { get; init; }
        public IntPtr? Target { get; set; }

        [SetsRequiredMembers]
        public CrosshairConfig(AppConfig config)
        {
            if (Enum.TryParse(config.Type, true, out CrosshairType parsedType))
            {
                Type = parsedType;
            }
            else
            {
                Type = CrosshairType.Dot;
            }

            Size = config.Size;
            Thickness = config.Thickness;
            BorderWidth = config.BorderWidth;
            try
            {
                Color = (Color)ColorConverter.ConvertFromString(config.Color);
            }
            catch
            {
                Color = Colors.Red;
            }
        }
    }
}
