using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace AmbilightScreen
{
    static class Program
    {
        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        [STAThread]
        static void Main(string[] args)
        {
            try { SetProcessDPIAware(); } catch { }

            bool isFirstInstance;
            using (var mutex = new System.Threading.Mutex(true, "AmbilightScreen_SingleInstance_Mutex", out isFirstInstance))
            {
                if (!isFirstInstance)
                {
                    MessageBox.Show("Ambilight уже запущен в трее около часов.\nДля включения/выключения нажмите Ctrl + Shift + L.",
                                    "Ambilight", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                bool isSilent = false;
                foreach (var arg in args)
                {
                    if (arg.Equals("--silent", StringComparison.OrdinalIgnoreCase)) isSilent = true;
                }

                Application.Run(new AmbilightAppContext(isSilent));
            }
        }
    }

    public class HotkeyWindow : NativeWindow, IDisposable
    {
        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private const int WM_HOTKEY = 0x0312;
        private const int HOTKEY_ID = 9527;

        private const uint MOD_CONTROL = 0x0002;
        private const uint MOD_SHIFT = 0x0004;

        private Action onHotkey;

        public HotkeyWindow(Action action)
        {
            onHotkey = action;
            CreateHandle(new CreateParams());
            RegisterHotKey(this.Handle, HOTKEY_ID, MOD_CONTROL | MOD_SHIFT, (uint)Keys.L);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY && m.WParam.ToInt32() == HOTKEY_ID)
            {
                if (onHotkey != null) onHotkey();
            }
            base.WndProc(ref m);
        }

        public void Dispose()
        {
            UnregisterHotKey(this.Handle, HOTKEY_ID);
            DestroyHandle();
        }
    }

    public class AmbilightAppContext : ApplicationContext
    {
        private const string RUN_KEY = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string APP_NAME = "AmbilightScreen";

        private NotifyIcon trayIcon;
        private HotkeyWindow hotkeyWindow;
        private AmbilightForm ambientForm;
        private MenuItem toggleItem;
        private MenuItem autoStartItem;

        public AmbilightAppContext(bool isSilent)
        {
            hotkeyWindow = new HotkeyWindow(ToggleAmbilight);
            InitTray();

            if (!isSilent)
            {
                trayIcon.ShowBalloonTip(3000, "Ambilight активен",
                    "Нажмите Ctrl + Shift + L для включения подсветки на втором экране.",
                    ToolTipIcon.Info);
            }
        }

        private void InitTray()
        {
            ContextMenu menu = new ContextMenu();

            toggleItem = new MenuItem("Включить подсветку (Ctrl + Shift + L)", (s, e) => ToggleAmbilight());
            toggleItem.DefaultItem = true;
            menu.MenuItems.Add(toggleItem);

            menu.MenuItems.Add("-");

            autoStartItem = new MenuItem("Запускать вместе с Windows", (s, e) => ToggleAutoStart());
            autoStartItem.Checked = IsAutoStartEnabled();
            menu.MenuItems.Add(autoStartItem);

            MenuItem modeMenu = new MenuItem("Режим подсветки");
            modeMenu.MenuItems.Add("Мягкий свет без деталей (4×3) [По умолчанию]", (s, e) => {
                if (ambientForm != null) ambientForm.SetMode(0);
            });
            modeMenu.MenuItems.Add("Единая лампа (1 цвет на весь экран)", (s, e) => {
                if (ambientForm != null) ambientForm.SetMode(1);
            });
            modeMenu.MenuItems.Add("Зональный свет (6×4)", (s, e) => {
                if (ambientForm != null) ambientForm.SetMode(2);
            });
            menu.MenuItems.Add(modeMenu);

            MenuItem speedMenu = new MenuItem("Скорость реакции");
            speedMenu.MenuItems.Add("Мгновенная (Gaming)", (s, e) => {
                if (ambientForm != null) ambientForm.SetSmoothing(0.85f);
            });
            speedMenu.MenuItems.Add("Быстрая (Рекомендуется)", (s, e) => {
                if (ambientForm != null) ambientForm.SetSmoothing(0.40f);
            });
            speedMenu.MenuItems.Add("Плавная (Кино)", (s, e) => {
                if (ambientForm != null) ambientForm.SetSmoothing(0.20f);
            });
            menu.MenuItems.Add(speedMenu);

            MenuItem brightnessMenu = new MenuItem("Яркость");
            int[] levels = { 20, 35, 50, 65, 80, 100 };
            foreach (var lvl in levels)
            {
                int val = lvl;
                brightnessMenu.MenuItems.Add(val + "%", (s, e) => {
                    if (ambientForm != null) ambientForm.SetBrightness(val / 100f);
                });
            }
            menu.MenuItems.Add(brightnessMenu);

            menu.MenuItems.Add("Поменять мониторы местами (S)", (s, e) => {
                if (ambientForm != null) ambientForm.SwapScreens();
            });

            menu.MenuItems.Add("-");
            menu.MenuItems.Add("Выход", (s, e) => ExitApp());

            trayIcon = new NotifyIcon();
            trayIcon.Text = "Ambilight (Ctrl + Shift + L)";
            trayIcon.Icon = SystemIcons.Application;
            trayIcon.ContextMenu = menu;
            trayIcon.Visible = true;
            trayIcon.DoubleClick += (s, e) => ToggleAmbilight();
        }

        private void ToggleAmbilight()
        {
            if (ambientForm != null && ambientForm.Visible)
            {
                ambientForm.HideForm();
                toggleItem.Text = "Включить подсветку (Ctrl + Shift + L)";
            }
            else
            {
                if (ambientForm == null || ambientForm.IsDisposed)
                {
                    ambientForm = new AmbilightForm(OnFormClosedByUser);
                }
                ambientForm.ShowForm();
                toggleItem.Text = "Выключить подсветку (Ctrl + Shift + L)";
            }
        }

        private void OnFormClosedByUser()
        {
            toggleItem.Text = "Включить подсветку (Ctrl + Shift + L)";
        }

        private bool IsAutoStartEnabled()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RUN_KEY, false))
                {
                    return key != null && key.GetValue(APP_NAME) != null;
                }
            }
            catch { return false; }
        }

        private void ToggleAutoStart()
        {
            try
            {
                bool current = IsAutoStartEnabled();
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RUN_KEY, true))
                {
                    if (key != null)
                    {
                        if (!current)
                        {
                            key.SetValue(APP_NAME, "\"" + Application.ExecutablePath + "\" --silent");
                            autoStartItem.Checked = true;
                            trayIcon.ShowBalloonTip(2000, "Автозапуск", "Ambilight добавлен в автозагрузку Windows.", ToolTipIcon.Info);
                        }
                        else
                        {
                            key.DeleteValue(APP_NAME, false);
                            autoStartItem.Checked = false;
                            trayIcon.ShowBalloonTip(2000, "Автозапуск", "Ambilight удален из автозагрузки Windows.", ToolTipIcon.Info);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Ошибка настройки автозапуска: " + ex.Message, "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ExitApp()
        {
            if (ambientForm != null)
            {
                ambientForm.Close();
                ambientForm.Dispose();
            }

            if (hotkeyWindow != null) hotkeyWindow.Dispose();

            if (trayIcon != null)
            {
                trayIcon.Visible = false;
                trayIcon.Dispose();
            }

            Application.Exit();
        }
    }

    public class AmbilightForm : Form
    {
        #region Win32 P/Invoke
        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("gdi32.dll")]
        private static extern int SetStretchBltMode(IntPtr hdc, int nStretchMode);

        [DllImport("gdi32.dll")]
        private static extern bool SetBrushOrgEx(IntPtr hdc, int nXOrg, int nYOrg, IntPtr lppt);

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern bool StretchBlt(IntPtr hdcDest, int nXOriginDest, int nYOriginDest, int nWidthDest, int nHeightDest,
            IntPtr hdcSrc, int nXOriginSrc, int nYOriginSrc, int nWidthSrc, int nHeightSrc, int dwRop);

        [DllImport("kernel32.dll")]
        private static extern bool SetProcessWorkingSetSize(IntPtr proc, int min, int max);

        private const int HALFTONE = 4;
        private const int SRCCOPY = 0x00CC0020;
        #endregion

        private int currentMode = 0;
        private int sampleW = 4;
        private int sampleH = 3;

        private Bitmap rawSample;
        private Graphics rawSampleGraphics;

        private Bitmap displayBmp;
        private float[,] currentR;
        private float[,] currentG;
        private float[,] currentB;

        private Timer captureTimer;
        private float brightness = 0.50f;
        private float saturation = 1.45f;
        private float smoothing = 0.45f;  // Мгновенный отклик при нормальной частоте
        private bool cropBars = true;

        private Screen sourceScreen;
        private Screen targetScreen;

        private string notificationText = "Ctrl+Shift+L — скрыть | B — режим | ← / → — скорость";
        private int notificationTimer = 90;
        private Action onHideCallback;

        private Font uiFont = new Font("Segoe UI", 12, FontStyle.Regular);
        private Brush uiBgBrush = new SolidBrush(Color.FromArgb(170, 15, 15, 15));
        private Brush uiTextBrush = new SolidBrush(Color.White);
        private int tickCounter = 0;

        public AmbilightForm(Action onHide)
        {
            this.onHideCallback = onHide;

            this.DoubleBuffered = true;
            this.FormBorderStyle = FormBorderStyle.None;
            this.ShowInTaskbar = false;
            this.BackColor = Color.Black;

            InitScreens();
            ReinitGrid(4, 3);

            this.KeyPreview = true;
            this.KeyDown += OnFormKeyDown;
            this.MouseWheel += OnFormMouseWheel;
            this.MouseClick += OnFormMouseClick;

            // 30 FPS (33 мс) вместо 60 FPS — сразу в 2 раза меньше нагрузка на процессор!
            captureTimer = new Timer();
            captureTimer.Interval = 33;
            captureTimer.Tick += OnTick;
        }

        private void InitScreens()
        {
            Screen[] screens = Screen.AllScreens;
            if (screens.Length >= 2)
            {
                foreach (var s in screens)
                {
                    if (s.Primary) sourceScreen = s;
                    else targetScreen = s;
                }
            }
            else
            {
                sourceScreen = Screen.PrimaryScreen;
                targetScreen = Screen.PrimaryScreen;
            }

            ApplyTargetScreenBounds();
        }

        private void ApplyTargetScreenBounds()
        {
            this.StartPosition = FormStartPosition.Manual;
            this.Bounds = targetScreen.Bounds;
        }

        private void ReinitGrid(int w, int h)
        {
            sampleW = w;
            sampleH = h;

            if (rawSampleGraphics != null) rawSampleGraphics.Dispose();
            if (rawSample != null) rawSample.Dispose();
            if (displayBmp != null) displayBmp.Dispose();

            rawSample = new Bitmap(sampleW, sampleH, PixelFormat.Format24bppRgb);
            rawSampleGraphics = Graphics.FromImage(rawSample);
            displayBmp = new Bitmap(sampleW, sampleH, PixelFormat.Format24bppRgb);

            currentR = new float[sampleW, sampleH];
            currentG = new float[sampleW, sampleH];
            currentB = new float[sampleW, sampleH];
        }

        public void SetMode(int mode)
        {
            currentMode = mode;
            if (mode == 0)
            {
                ReinitGrid(4, 3);
                ShowNotification("Режим: Мягкий свет (4×3, без деталей)");
            }
            else if (mode == 1)
            {
                ReinitGrid(1, 1);
                ShowNotification("Режим: Единая лампа (1 цвет на весь экран)");
            }
            else if (mode == 2)
            {
                ReinitGrid(6, 4);
                ShowNotification("Режим: Зональный свет (6×4)");
            }
        }

        public void CycleMode()
        {
            int next = (currentMode + 1) % 3;
            SetMode(next);
        }

        public void ShowForm()
        {
            InitScreens();
            this.Show();
            captureTimer.Start();
            notificationTimer = 90;
        }

        public void HideForm()
        {
            captureTimer.Stop();
            this.Hide();

            if (onHideCallback != null) onHideCallback();

            try
            {
                GC.Collect(0, GCCollectionMode.Optimized);
                SetProcessWorkingSetSize(Process.GetCurrentProcess().Handle, -1, -1);
            }
            catch { }
        }

        public void SetBrightness(float val)
        {
            brightness = Math.Max(0.05f, Math.Min(1.0f, val));
            ShowNotification(string.Format("Яркость: {0}%", (int)(brightness * 100)));
        }

        public void SetSmoothing(float val)
        {
            smoothing = Math.Max(0.1f, Math.Min(1.0f, val));
            string desc;
            if (smoothing >= 0.8f) desc = "Мгновенная (100%)";
            else if (smoothing >= 0.45f) desc = "Быстрая (Рекомендуется)";
            else if (smoothing >= 0.28f) desc = "Средняя";
            else desc = "Плавная (Кино)";
            ShowNotification(string.Format("Скорость реакции: {0}", desc));
        }

        public void AdjustSmoothing(float delta)
        {
            SetSmoothing(smoothing + delta);
        }

        public void ToggleCinemaMode()
        {
            cropBars = !cropBars;
            ShowNotification(cropBars ? "Кино-режим (полосы отрезаны): ВКЛ" : "Кино-режим: ВЫКЛ");
        }

        public void SwapScreens()
        {
            Screen temp = sourceScreen;
            sourceScreen = targetScreen;
            targetScreen = temp;
            ApplyTargetScreenBounds();
            ShowNotification(string.Format("Источник: {0} -> Подсветка: {1}", sourceScreen.DeviceName, targetScreen.DeviceName));
        }

        private void OnTick(object sender, EventArgs e)
        {
            bool hasChanged = CaptureAndSmooth();

            // Перерисовываем ТОЛЬКО если цвет реально изменился
            if (hasChanged || notificationTimer > 0)
            {
                this.Invalidate();
            }

            tickCounter++;
            if (tickCounter % 300 == 0)
            {
                try
                {
                    SetProcessWorkingSetSize(Process.GetCurrentProcess().Handle, -1, -1);
                }
                catch { }
            }
        }

        private bool CaptureAndSmooth()
        {
            IntPtr screenDC = GetDC(IntPtr.Zero);
            if (screenDC != IntPtr.Zero)
            {
                try
                {
                    IntPtr destDC = rawSampleGraphics.GetHdc();
                    try
                    {
                        SetStretchBltMode(destDC, HALFTONE);
                        SetBrushOrgEx(destDC, 0, 0, IntPtr.Zero);

                        Rectangle src = sourceScreen.Bounds;
                        int srcX = src.X;
                        int srcY = src.Y;
                        int srcW = src.Width;
                        int srcH = src.Height;

                        if (cropBars)
                        {
                            int barH = (int)(srcH * 0.08f);
                            srcY += barH;
                            srcH -= barH * 2;
                        }

                        StretchBlt(destDC, 0, 0, sampleW, sampleH,
                                   screenDC, srcX, srcY, srcW, srcH, SRCCOPY);
                    }
                    finally
                    {
                        rawSampleGraphics.ReleaseHdc(destDC);
                    }
                }
                finally
                {
                    ReleaseDC(IntPtr.Zero, screenDC);
                }
            }

            BitmapData srcData = rawSample.LockBits(new Rectangle(0, 0, sampleW, sampleH),
                                                   ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
            BitmapData dstData = displayBmp.LockBits(new Rectangle(0, 0, sampleW, sampleH),
                                                    ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);

            int bufferSize = Math.Abs(srcData.Stride) * sampleH;
            byte[] srcBytes = new byte[bufferSize];
            byte[] dstBytes = new byte[bufferSize];

            Marshal.Copy(srcData.Scan0, srcBytes, 0, bufferSize);

            float maxDelta = 0f;
            int stride = srcData.Stride;

            for (int y = 0; y < sampleH; y++)
            {
                int rowOffset = y * stride;
                for (int x = 0; x < sampleW; x++)
                {
                    int idx = rowOffset + (x * 3);
                    float rawB = srcBytes[idx];
                    float rawG = srcBytes[idx + 1];
                    float rawR = srcBytes[idx + 2];

                    float gray = 0.299f * rawR + 0.587f * rawG + 0.114f * rawB;
                    float targetR = Math.Max(0f, Math.Min(255f, gray + (rawR - gray) * saturation));
                    float targetG = Math.Max(0f, Math.Min(255f, gray + (rawG - gray) * saturation));
                    float targetB = Math.Max(0f, Math.Min(255f, gray + (rawB - gray) * saturation));

                    targetR = Math.Max(targetR, 8f);
                    targetG = Math.Max(targetG, 8f);
                    targetB = Math.Max(targetB, 10f);

                    float dR = (targetR - currentR[x, y]) * smoothing;
                    float dG = (targetG - currentG[x, y]) * smoothing;
                    float dB = (targetB - currentB[x, y]) * smoothing;

                    float stepDiff = Math.Abs(dR) + Math.Abs(dG) + Math.Abs(dB);
                    if (stepDiff > maxDelta) maxDelta = stepDiff;

                    currentB[x, y] += dB;
                    currentG[x, y] += dG;
                    currentR[x, y] += dR;

                    dstBytes[idx] = (byte)Math.Min(255, (int)(currentB[x, y] * brightness));
                    dstBytes[idx + 1] = (byte)Math.Min(255, (int)(currentG[x, y] * brightness));
                    dstBytes[idx + 2] = (byte)Math.Min(255, (int)(currentR[x, y] * brightness));
                }
            }

            Marshal.Copy(dstBytes, 0, dstData.Scan0, bufferSize);

            rawSample.UnlockBits(srcData);
            displayBmp.UnlockBits(dstData);

            return maxDelta > 0.4f;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (displayBmp == null) return;

            if (sampleW == 1 && sampleH == 1)
            {
                Color c = displayBmp.GetPixel(0, 0);
                using (Brush brush = new SolidBrush(c))
                {
                    e.Graphics.FillRectangle(brush, this.ClientRectangle);
                }
            }
            else
            {
                // ТОТ САМЫЙ РЕЖИМ, КОТОРЫЙ ИДЕАЛЬНО ВЫГЛЯДЕЛ:
                // HighQualityBicubic с запасом по краям!
                e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                e.Graphics.PixelOffsetMode = PixelOffsetMode.Half;
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

                Rectangle targetRect = new Rectangle(-40, -40, this.Width + 80, this.Height + 80);
                e.Graphics.DrawImage(displayBmp, targetRect);
            }

            if (notificationTimer > 0)
            {
                notificationTimer--;
                SizeF size = e.Graphics.MeasureString(notificationText, uiFont);
                int x = (this.Width - (int)size.Width) / 2;
                int y = this.Height - (int)size.Height - 40;
                e.Graphics.FillRectangle(uiBgBrush, x - 18, y - 8, size.Width + 36, size.Height + 16);
                e.Graphics.DrawString(notificationText, uiFont, uiTextBrush, x, y);
            }
        }

        private void AdjustBrightness(float delta)
        {
            SetBrightness(brightness + delta);
        }

        private void ShowNotification(string text)
        {
            notificationText = text;
            notificationTimer = 75;
            this.Invalidate();
        }

        private void OnFormKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape || e.KeyCode == Keys.Q)
            {
                HideForm();
            }
            else if (e.KeyCode == Keys.B)
            {
                CycleMode();
            }
            else if (e.KeyCode == Keys.Up)
            {
                AdjustBrightness(0.05f);
            }
            else if (e.KeyCode == Keys.Down)
            {
                AdjustBrightness(-0.05f);
            }
            else if (e.KeyCode == Keys.Right)
            {
                AdjustSmoothing(0.08f);
            }
            else if (e.KeyCode == Keys.Left)
            {
                AdjustSmoothing(-0.08f);
            }
            else if (e.KeyCode == Keys.C)
            {
                ToggleCinemaMode();
            }
            else if (e.KeyCode == Keys.S)
            {
                SwapScreens();
            }
        }

        private void OnFormMouseWheel(object sender, MouseEventArgs e)
        {
            if (e.Delta > 0)
                AdjustBrightness(0.05f);
            else if (e.Delta < 0)
                AdjustBrightness(-0.05f);
        }

        private void OnFormMouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                ContextMenu menu = new ContextMenu();
                menu.MenuItems.Add("Мягкий свет (4×3, без деталей)", (s, ev) => SetMode(0));
                menu.MenuItems.Add("Единая лампа (1 цвет на весь экран)", (s, ev) => SetMode(1));
                menu.MenuItems.Add("Зональный свет (6×4)", (s, ev) => SetMode(2));
                menu.MenuItems.Add("-");
                menu.MenuItems.Add(string.Format("Яркость: {0}% (Колесико мыши или ↑↓)", (int)(brightness * 100)));
                menu.MenuItems.Add("Скорость реакции быстрее (→)", (s, ev) => AdjustSmoothing(0.1f));
                menu.MenuItems.Add("Скорость реакции плавнее (←)", (s, ev) => AdjustSmoothing(-0.1f));
                menu.MenuItems.Add("-");
                menu.MenuItems.Add(cropBars ? "Кино-режим (полосы отрезаны)" : "Обычный режим", (s, ev) => ToggleCinemaMode());
                menu.MenuItems.Add("Поменять мониторы местами (S)", (s, ev) => SwapScreens());
                menu.MenuItems.Add("-");
                menu.MenuItems.Add("Скрыть подсветку (Esc / Ctrl+Shift+L)", (s, ev) => HideForm());
                menu.Show(this, e.Location);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (captureTimer != null)
                {
                    captureTimer.Stop();
                    captureTimer.Dispose();
                }

                if (rawSampleGraphics != null) rawSampleGraphics.Dispose();
                if (rawSample != null) rawSample.Dispose();
                if (displayBmp != null) displayBmp.Dispose();

                if (uiFont != null) uiFont.Dispose();
                if (uiBgBrush != null) uiBgBrush.Dispose();
                if (uiTextBrush != null) uiTextBrush.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
