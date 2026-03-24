using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace NanoBin
{
    public class TrayApp : ApplicationContext
    {
        private NotifyIcon trayIcon = null!;
        private System.Windows.Forms.Timer monitorTimer = null!;
        private System.Windows.Forms.Timer animTimer = null!;
        private ContextMenuStrip menu = null!;
        private ToolStripMenuItem? startupItem;

        private string themeMode = "auto";
        private bool isDarkTheme = true;

        // Animation
        private string animMode = "default"; // "default", "smooth", "off"
        private bool isAnimating = false;
        private int animFrame = 0;
        private const int AnimFrames = 12;

        // State tracking - icon only changes when state changes
        private bool lastHasFiles = false;
        private bool lastIsDark = false;
        private bool stateInitialized = false;

        private const string RegPath = @"Software\NanoBin";
        private const string RegThemeKey = "ThemeMode";
        private const string RegAnimKey = "AnimMode";

        [DllImport("shell32.dll")]
        private static extern int SHEmptyRecycleBin(IntPtr hwnd, string? pszRootPath, uint dwFlags);

        [StructLayout(LayoutKind.Sequential)]
        private struct SHQUERYRBINFO
        {
            public int cbSize;
            public long i64Size;
            public long i64NumItems;
        }

        [DllImport("shell32.dll")]
        private static extern int SHQueryRecycleBin(string? pszRootPath, ref SHQUERYRBINFO info);

        public TrayApp()
        {
            LoadPreferences();
            DetectTheme();

            menu = new ContextMenuStrip();
            BuildMenu();
            UpdateMenuTheme();

            trayIcon = new NotifyIcon()
            {
                ContextMenuStrip = menu,
                Visible = true
            };

            // Force initial icon + tooltip
            lastHasFiles = RecycleBinHasFiles();
            lastIsDark = isDarkTheme;
            stateInitialized = true;
            ApplyIcon();
            UpdateTooltip();

            // Monitor timer: check recycle bin state every 3 seconds
            monitorTimer = new System.Windows.Forms.Timer();
            monitorTimer.Interval = 3000;
            monitorTimer.Tick += OnMonitorTick;
            monitorTimer.Start();

            // Animation timer (used only during transitions)
            animTimer = new System.Windows.Forms.Timer();
            animTimer.Interval = 80;
            animTimer.Tick += OnAnimTick;

            // Listen for system theme changes
            SystemEvents.UserPreferenceChanged += (s, e) =>
            {
                if (e.Category == UserPreferenceCategory.General && themeMode == "auto")
                {
                    bool oldTheme = isDarkTheme;
                    DetectTheme();
                    if (oldTheme != isDarkTheme)
                    {
                        lastIsDark = isDarkTheme;
                        ApplyIcon();
                        UpdateMenuTheme();
                        BuildMenu();
                        UpdateMenuTheme();
                    }
                }
            };

            /* // Left single click: show balloon tip
            trayIcon.MouseClick += (s, e) =>
            {
                if (e.Button == MouseButtons.Left)
                {
                    trayIcon.BalloonTipTitle = "NanoBin";
                    trayIcon.BalloonTipText = "Clique duplo para esvaziar e clique direito para opções";
                    trayIcon.BalloonTipIcon = ToolTipIcon.Info;
                    trayIcon.ShowBalloonTip(3000);
                }
            }; */

            // Left double click: empty recycle bin
            trayIcon.MouseDoubleClick += (s, e) =>
            {
                if (e.Button == MouseButtons.Left)
                    EmptyRecycleBin(s, e);
            };
        }

        // ===== Monitor =====

        private void OnMonitorTick(object? sender, EventArgs e)
        {
            bool hasFiles = RecycleBinHasFiles();
            bool themeChanged = false;

            if (themeMode == "auto")
            {
                bool oldDark = isDarkTheme;
                DetectTheme();
                if (oldDark != isDarkTheme)
                {
                    themeChanged = true;
                    lastIsDark = isDarkTheme;
                    UpdateMenuTheme();
                }
            }

            // Only update icon when state actually changes
            if (hasFiles != lastHasFiles || themeChanged)
            {
                bool oldHasFiles = lastHasFiles;
                lastHasFiles = hasFiles;

                // Animate only on meaningful transitions (files added/removed)
                if (oldHasFiles != hasFiles && animMode != "off")
                {
                    StartAnimation();
                }
                else
                {
                    ApplyIcon();
                }

                UpdateTooltip();
            }
        }

        // ===== Animation =====

        private void StartAnimation()
        {
            if (isAnimating) return;
            isAnimating = true;
            animFrame = 0;

            if (animMode == "smooth")
                animTimer.Interval = 60;
            else
                animTimer.Interval = 80;

            animTimer.Start();
        }

        private void OnAnimTick(object? sender, EventArgs e)
        {
            animFrame++;

            if (animFrame >= AnimFrames)
            {
                // Animation complete
                animTimer.Stop();
                isAnimating = false;
                ApplyIcon();
                return;
            }

            // Create animated icon effect by toggling opacity simulation
            if (animMode == "smooth")
            {
                // Sine wave smooth animation: alternate icons with sine timing
                double t = (double)animFrame / AnimFrames;
                double sinVal = Math.Sin(t * Math.PI);
                // Show alternate icon at peak of sine
                bool showAlt = sinVal > 0.5;
                ApplyAnimFrame(showAlt);
            }
            else
            {
                // Default animation: simple blink
                bool showAlt = animFrame % 2 == 0;
                ApplyAnimFrame(showAlt);
            }
        }

        private void ApplyAnimFrame(bool alternate)
        {
            // Toggle between current and opposite theme icon for visual effect
            string iconName;
            if (alternate)
                iconName = isDarkTheme ? "icon_dark.ico" : "icon_light.ico";
            else
                iconName = isDarkTheme ? "icon_light.ico" : "icon_dark.ico";

            string iconPath = Path.Combine(AppContext.BaseDirectory, iconName);
            SetIconFromPath(iconPath);
        }

        // ===== Icon Management =====

        private void ApplyIcon()
        {
            string iconName = isDarkTheme ? "icon_light.ico" : "icon_dark.ico";
            string iconPath = Path.Combine(AppContext.BaseDirectory, iconName);
            SetIconFromPath(iconPath);
        }

        private void SetIconFromPath(string iconPath)
        {
            if (File.Exists(iconPath))
            {
                try
                {
                    using var stream = new FileStream(iconPath, FileMode.Open, FileAccess.Read);
                    trayIcon.Icon = new Icon(stream);
                }
                catch
                {
                    trayIcon.Icon = SystemIcons.Application;
                }
            }
            else
            {
                trayIcon.Icon = SystemIcons.Application;
            }
        }

        private void UpdateTooltip()
        {
            trayIcon.Text = lastHasFiles
                ? "Há arquivos na lixeira, clique duplo para limpar, clique direito para opções"
                : "Não há arquivos na Lixeira";
        }

        // ===== Recycle Bin =====

        private bool RecycleBinHasFiles()
        {
            var info = new SHQUERYRBINFO();
            info.cbSize = Marshal.SizeOf(typeof(SHQUERYRBINFO));
            int result = SHQueryRecycleBin(null, ref info);
            return result == 0 && info.i64NumItems > 0;
        }

        private void OpenRecycleBin(object? sender, EventArgs e)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = "shell:RecycleBinFolder",
                UseShellExecute = true
            });
        }

        private void EmptyRecycleBin(object? sender, EventArgs e)
        {
            if (!RecycleBinHasFiles())
            {
                trayIcon.BalloonTipTitle = "NanoBin";
                trayIcon.BalloonTipText = "A Lixeira já está vazia!";
                trayIcon.BalloonTipIcon = ToolTipIcon.Info;
                trayIcon.ShowBalloonTip(2000);
                return;
            }

            var result = MessageBox.Show(
                "Tem certeza que deseja esvaziar a lixeira?",
                "NanoBin - Confirmação",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning
            );

            if (result == DialogResult.Yes)
            {
                SHEmptyRecycleBin(IntPtr.Zero, null, 0);
                lastHasFiles = false;

                if (animMode != "off")
                    StartAnimation();
                else
                    ApplyIcon();

                UpdateTooltip();
            }
        }

        // ===== Menu =====

        private void BuildMenu()
        {
            menu.Items.Clear();

            // Theme submenu
            var themeMenu = new ToolStripMenuItem("Tema");
            var autoItem = new ToolStripMenuItem("Automático (Sistema)");
            var darkItem = new ToolStripMenuItem("Escuro");
            var lightItem = new ToolStripMenuItem("Claro");

            autoItem.Checked = themeMode == "auto";
            darkItem.Checked = themeMode == "dark";
            lightItem.Checked = themeMode == "light";

            autoItem.Click += (s, e) => SetTheme("auto");
            darkItem.Click += (s, e) => SetTheme("dark");
            lightItem.Click += (s, e) => SetTheme("light");

            themeMenu.DropDownItems.AddRange(new ToolStripItem[] { autoItem, darkItem, lightItem });

            // Animation submenu
            var animMenu = new ToolStripMenuItem("Animação");
            var animDefault = new ToolStripMenuItem("Padrão");
            var animSmooth = new ToolStripMenuItem("Suave");
            var animOff = new ToolStripMenuItem("Desligada");

            animDefault.Checked = animMode == "default";
            animSmooth.Checked = animMode == "smooth";
            animOff.Checked = animMode == "off";

            animDefault.Click += (s, e) => SetAnimMode("default");
            animSmooth.Click += (s, e) => SetAnimMode("smooth");
            animOff.Click += (s, e) => SetAnimMode("off");

            animMenu.DropDownItems.AddRange(new ToolStripItem[] { animDefault, animSmooth, animOff });

            // Startup
            startupItem = new ToolStripMenuItem("Iniciar com o Windows");
            startupItem.Checked = IsStartupEnabled();
            startupItem.Click += (s, e) =>
            {
                startupItem.Checked = !startupItem.Checked;
                ToggleStartup(startupItem.Checked);
            };

            menu.Items.Add("Abrir lixeira", null, OpenRecycleBin);
            menu.Items.Add("Esvaziar lixeira", null, EmptyRecycleBin);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(themeMenu);
            menu.Items.Add(animMenu);
            menu.Items.Add(startupItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Sobre o NanoBin", null, About);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Sair", null, Exit);
        }

        // ===== Theme =====

        private void SetTheme(string mode)
        {
            themeMode = mode;
            SavePreferences();
            DetectTheme();
            lastIsDark = isDarkTheme;
            ApplyIcon();
            UpdateMenuTheme();
            BuildMenu();
            UpdateMenuTheme();
        }

        private void SetAnimMode(string mode)
        {
            animMode = mode;
            SavePreferences();
            BuildMenu();
            UpdateMenuTheme();
        }

        private void DetectTheme()
        {
            if (themeMode == "dark")
                isDarkTheme = true;
            else if (themeMode == "light")
                isDarkTheme = false;
            else
                isDarkTheme = IsSystemDarkTheme();
        }

        private bool IsSystemDarkTheme()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                var val = key?.GetValue("AppsUseLightTheme");
                if (val is int i)
                    return i == 0;
            }
            catch { }
            return false;
        }

        // ===== Persistence =====

        private void LoadPreferences()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RegPath);
                var theme = key?.GetValue(RegThemeKey) as string;
                if (theme == "dark" || theme == "light" || theme == "auto")
                    themeMode = theme;

                var anim = key?.GetValue(RegAnimKey) as string;
                if (anim == "default" || anim == "smooth" || anim == "off")
                    animMode = anim;
            }
            catch { }
        }

        private void SavePreferences()
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(RegPath);
                key?.SetValue(RegThemeKey, themeMode);
                key?.SetValue(RegAnimKey, animMode);
            }
            catch { }
        }

        // ===== Startup =====

        private void ToggleStartup(bool enable)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
                if (key == null) return;

                if (enable)
                    key.SetValue("NanoBin", $"\"{Application.ExecutablePath}\"");
                else
                    key.DeleteValue("NanoBin", false);
            }
            catch { }
        }

        private bool IsStartupEnabled()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
                return key?.GetValue("NanoBin") != null;
            }
            catch { return false; }
        }

        // ===== Menu Theme =====

        private void UpdateMenuTheme()
        {
            if (isDarkTheme)
            {
                menu.Renderer = new DarkMenuRenderer();
                menu.ForeColor = Color.White;
                menu.BackColor = Color.FromArgb(30, 30, 30);
            }
            else
            {
                menu.Renderer = new LightMenuRenderer();
                menu.ForeColor = Color.FromArgb(30, 30, 30);
                menu.BackColor = Color.FromArgb(245, 245, 245);
            }
        }

        // ===== About =====

        private void About(object? sender, EventArgs e)
        {
            var form = new Form
            {
                Width = 380,
                Height = 540,
                Text = "Sobre o NanoBin",
                StartPosition = FormStartPosition.CenterScreen,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false,
                BackColor = isDarkTheme ? Color.FromArgb(30, 30, 30) : Color.White
            };

            var titleLabel = new Label
            {
                Text = "NanoBin v3.0",
                Font = new Font("Segoe UI", 14, FontStyle.Bold),
                ForeColor = isDarkTheme ? Color.White : Color.FromArgb(30, 30, 30),
                Dock = DockStyle.Top,
                TextAlign = ContentAlignment.MiddleCenter,
                Height = 40,
                Padding = new Padding(0, 8, 0, 0)
            };

            var descLabel = new Label
            {
                Text = "Gerenciador simples da lixeira do Windows",
                Font = new Font("Segoe UI", 9),
                ForeColor = isDarkTheme ? Color.LightGray : Color.Gray,
                Dock = DockStyle.Top,
                TextAlign = ContentAlignment.MiddleCenter,
                Height = 25
            };

            var copyrightLabel = new Label
            {
                Text = "© 2026 Overclock Informática",
                Font = new Font("Segoe UI", 8),
                ForeColor = isDarkTheme ? Color.Gray : Color.DarkGray,
                Dock = DockStyle.Top,
                TextAlign = ContentAlignment.MiddleCenter,
                Height = 25
            };

            var linkInstagram = new LinkLabel
            {
                Text = "Instagram: @oc.informatica",
                Font = new Font("Segoe UI", 9),
                Dock = DockStyle.Top,
                TextAlign = ContentAlignment.MiddleCenter,
                Height = 25,
                LinkColor = isDarkTheme ? Color.FromArgb(100, 180, 255) : Color.Blue,
                ActiveLinkColor = isDarkTheme ? Color.FromArgb(150, 200, 255) : Color.DarkBlue
            };
            linkInstagram.Click += (s, ev) =>
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://www.instagram.com/oc.informatica/",
                    UseShellExecute = true
                });
            };

            var linkRecomendacoes = new LinkLabel
            {
                Text = "Minhas recomendações",
                Font = new Font("Segoe UI", 9),
                Dock = DockStyle.Top,
                TextAlign = ContentAlignment.MiddleCenter,
                Height = 25,
                LinkColor = isDarkTheme ? Color.FromArgb(100, 180, 255) : Color.Blue,
                ActiveLinkColor = isDarkTheme ? Color.FromArgb(150, 200, 255) : Color.DarkBlue
            };
            linkRecomendacoes.Click += (s, ev) =>
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://linkme.bio/overclockinformatica",
                    UseShellExecute = true
                });
            };

            var qrLabel = new Label
            {
                Text = "Faça uma doação ao desenvolvedor",
                Font = new Font("Segoe UI", 9, FontStyle.Italic),
                ForeColor = isDarkTheme ? Color.LightGray : Color.Gray,
                Dock = DockStyle.Top,
                TextAlign = ContentAlignment.MiddleCenter,
                Height = 30,
                Padding = new Padding(0, 8, 0, 0)
            };

            var qrPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 220,
                Padding = new Padding(60, 5, 60, 5)
            };

            var qrPicture = new PictureBox
            {
                Dock = DockStyle.Fill,
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Transparent
            };

            string qrPath = Path.Combine(AppContext.BaseDirectory, "qrcode_donation.png");
            if (File.Exists(qrPath))
            {
                try { qrPicture.Image = Image.FromFile(qrPath); }
                catch { }
            }

            qrPanel.Controls.Add(qrPicture);

            form.Controls.Add(qrPanel);
            form.Controls.Add(qrLabel);
            form.Controls.Add(linkRecomendacoes);
            form.Controls.Add(linkInstagram);
            form.Controls.Add(copyrightLabel);
            form.Controls.Add(descLabel);
            form.Controls.Add(titleLabel);

            form.ShowDialog();
        }

        // ===== Exit =====

        private void Exit(object? sender, EventArgs e)
        {
            trayIcon.Visible = false;
            monitorTimer.Stop();
            animTimer.Stop();
            Application.Exit();
        }
    }

    // ===== Dark Theme Renderer =====

    internal class DarkMenuRenderer : ToolStripProfessionalRenderer
    {
        public DarkMenuRenderer() : base(new DarkColorTable()) { }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = Color.White;
            base.OnRenderItemText(e);
        }
    }

    internal class DarkColorTable : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Color.FromArgb(30, 30, 30);
        public override Color MenuItemSelected => Color.FromArgb(55, 55, 55);
        public override Color MenuItemBorder => Color.FromArgb(55, 55, 55);
        public override Color MenuBorder => Color.FromArgb(50, 50, 50);
        public override Color ImageMarginGradientBegin => Color.FromArgb(30, 30, 30);
        public override Color ImageMarginGradientMiddle => Color.FromArgb(30, 30, 30);
        public override Color ImageMarginGradientEnd => Color.FromArgb(30, 30, 30);
        public override Color SeparatorDark => Color.FromArgb(60, 60, 60);
        public override Color SeparatorLight => Color.FromArgb(60, 60, 60);
    }

    // ===== Light Theme Renderer =====

    internal class LightMenuRenderer : ToolStripProfessionalRenderer
    {
        public LightMenuRenderer() : base(new LightColorTable()) { }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = Color.FromArgb(30, 30, 30);
            base.OnRenderItemText(e);
        }
    }

    internal class LightColorTable : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Color.FromArgb(245, 245, 245);
        public override Color MenuItemSelected => Color.FromArgb(220, 220, 220);
        public override Color MenuItemBorder => Color.FromArgb(200, 200, 200);
        public override Color MenuBorder => Color.FromArgb(200, 200, 200);
        public override Color ImageMarginGradientBegin => Color.FromArgb(245, 245, 245);
        public override Color ImageMarginGradientMiddle => Color.FromArgb(245, 245, 245);
        public override Color ImageMarginGradientEnd => Color.FromArgb(245, 245, 245);
        public override Color SeparatorDark => Color.FromArgb(210, 210, 210);
        public override Color SeparatorLight => Color.FromArgb(230, 230, 230);
    }
}
