using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace Crosshair
{
    public partial class MainWindow : Window
    {
        // Win32 constants
        const int GWL_EXSTYLE = -20;
        const int WS_EX_TRANSPARENT = 0x00000020;
        const int WS_EX_LAYERED = 0x00080000;
        const int WS_EX_TOOLWINDOW = 0x00000080;

        public MainWindow()
        {
            InitializeComponent();

            CrosshairStyle style = GetCrosshairStyle();
            CenterDot.Fill = new SolidColorBrush(style.color);
            CenterDot.Width = style.width;
            CenterDot.Height = style.height;
            BorderEllipse.Width = style.width + 0.2;
            BorderEllipse.Height = style.height + 0.2;
        }

        private CrosshairStyle GetCrosshairStyle()
        {
            CrosshairStyle style = new CrosshairStyle(Colors.Red, 3.5, 3.5);
            string currentDir = AppDomain.CurrentDomain.BaseDirectory ?? Environment.CurrentDirectory;
            string styleFilePath = Path.Combine(currentDir, "style.txt");
            if (!File.Exists(styleFilePath))
            {
                return style;
            }

            string content = File.ReadAllText(styleFilePath).Trim();
            if (String.IsNullOrEmpty(content))
            {
                return style;
            }

            Regex regex = new Regex(@"(\d+(?:\.\d+)?) (\d+(?:\.\d+)?) #([a-f|\d]{6})", RegexOptions.IgnoreCase);
            Match match = regex.Match(content);
            if (!match.Success)
            {
                return style;
            }

            try
            {
                string hex = match.Groups[3].Value;
                byte r = Convert.ToByte(hex.Substring(0, 2), 16);
                byte g = Convert.ToByte(hex.Substring(2, 2), 16);
                byte b = Convert.ToByte(hex.Substring(4, 2), 16);
                style.color = Color.FromRgb(r, g, b);
                style.width = double.Parse(match.Groups[1].Value);
                style.height = double.Parse(match.Groups[2].Value);
            }
            catch {}

            return style;
        }

        private void Window_SourceInitialized(object sender, EventArgs e)
        {
            var hwnd = new WindowInteropHelper(this).Handle;

            // 設為 Layered + Transparent + ToolWindow (不出現在 Alt+Tab)
            IntPtr ex = GetWindowLongPtr(hwnd, GWL_EXSTYLE);
            long newEx = ex.ToInt64() | WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW;
            SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(newEx));
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
        #endregion
    }

    public class CrosshairStyle
    {
        public Color color { get; set; }
        public double width { get; set; }
        public double height { get; set; }

        public CrosshairStyle(Color color, double width, double height)
        {
            this.color = color;
            this.width = width;
            this.height = height;
        }
    }
}
