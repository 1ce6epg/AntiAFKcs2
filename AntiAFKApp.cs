using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace AntiAFKcs2
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new AntiAfkForm());
        }
    }

    internal sealed class AntiAfkForm : Form
    {
        private const int MenuHotkeyId = 0x1CE6;
        private const int ToggleHotkeyId = 0x1CE7;
        private const int WmHotkey = 0x0312;
        private const uint ModNone = 0x0000;
        private const uint EsContinuous = 0x80000000;
        private const uint EsSystemRequired = 0x00000001;
        private const uint EsDisplayRequired = 0x00000002;
        private const int WmNclbuttondown = 0xA1;
        private const int HtCaption = 0x2;
        private const string DeveloperUrl = "https://github.com/1ce6epg";
        private const string WalkoudUrl = "https://github.com/Walkoud";
        private const string DefaultSequence = "WA,SD,T,AZ,ER";
        private static readonly string ConfigPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.ini");
        private static readonly Color FormBaseColor = Color.FromArgb(10, 13, 15);
        private static readonly Color AccentOffColor = Color.FromArgb(92, 102, 112);
        private static readonly Color AccentOnColor = Color.FromArgb(110, 255, 163);

        private readonly Panel accentPanel;
        private readonly ToggleSwitch toggleSwitch;
        private readonly Label statusValueLabel;
        private readonly TextBox intervalInput;
        private readonly TextBox keySequenceInput;
        private readonly Label hotkeyValueLabel;
        private readonly Button bindHotkeyButton;
        private readonly LinkLabel developerLink;
        private readonly LinkLabel walkoudLink;

        private string[] actionGroups = new[] { "WA", "SD", "T", "AZ", "ER" };
        private string currentSequenceText = DefaultSequence;
        private int currentIntervalMs = 450;
        private int actionGroupIndex;
        private Keys currentToggleHotkey = Keys.None;
        private bool menuHotkeyRegistered;
        private bool toggleHotkeyRegistered;
        private bool captureHotkeyMode;
        private volatile bool antiAfkEnabled;
        private Thread activityThread;
        private string activePressedGroup = string.Empty;
        private readonly object settingsLock = new object();

        public AntiAfkForm()
        {
            accentPanel = new Panel();
            toggleSwitch = new ToggleSwitch();
            statusValueLabel = new Label();
            intervalInput = new TextBox();
            keySequenceInput = new TextBox();
            hotkeyValueLabel = new Label();
            bindHotkeyButton = new Button();
            developerLink = new LinkLabel();
            walkoudLink = new LinkLabel();

            SuspendLayout();
            ConfigureForm();
            LoadSettings();
            BuildUi();
            ResumeLayout(false);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            RegisterHotkeys();
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            UnregisterHotkeys();

            base.OnHandleDestroyed(e);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (!captureHotkeyMode)
            {
                return base.ProcessCmdKey(ref msg, keyData);
            }

            Keys keyCode = keyData & Keys.KeyCode;
            if (keyCode == Keys.Escape)
            {
                captureHotkeyMode = false;
                bindHotkeyButton.Text = "Bind";
                hotkeyValueLabel.Text = FormatHotkey(currentToggleHotkey);
                return true;
            }

            if (keyCode == Keys.Delete || keyCode == Keys.Back)
            {
                ClearHotkey();
                return true;
            }

            if (!IsBindableHotkey(keyCode))
            {
                bindHotkeyButton.Text = "Retry";
                return true;
            }

            ApplyHotkey(keyCode);
            return true;
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WmHotkey && m.WParam.ToInt32() == MenuHotkeyId)
            {
                ToggleVisibility();
                return;
            }

            if (m.Msg == WmHotkey && m.WParam.ToInt32() == ToggleHotkeyId)
            {
                ToggleAntiAfkState();
                return;
            }

            base.WndProc(ref m);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            StopAntiAfkWorker();
            base.OnFormClosing(e);
        }

        private void ConfigureForm()
        {
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Color.FromArgb(44, 49, 55);
            ClientSize = new Size(448, 382);
            DoubleBuffered = true;
            Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            FormBorderStyle = FormBorderStyle.None;
            MaximizeBox = false;
            MinimizeBox = false;
            Opacity = 0.94D;
            ShowIcon = true;
            ShowInTaskbar = true;
            StartPosition = FormStartPosition.CenterScreen;
            Text = "AntiAFK";
            TopMost = true;
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);

            MouseDown += DragForm;
            Paint += DrawFormBackground;
        }

        private void BuildUi()
        {
            accentPanel.BackColor = AccentOffColor;
            accentPanel.Location = new Point(0, 0);
            accentPanel.Size = new Size(6, ClientSize.Height);
            Controls.Add(accentPanel);

            Label titleLabel = new Label();
            titleLabel.AutoSize = true;
            titleLabel.BackColor = Color.Transparent;
            titleLabel.Font = new Font("Segoe UI Semibold", 18F, FontStyle.Bold, GraphicsUnit.Point);
            titleLabel.ForeColor = Color.FromArgb(242, 245, 247);
            titleLabel.Location = new Point(22, 18);
            titleLabel.Text = "ANTI-AFK";
            titleLabel.MouseDown += DragForm;
            Controls.Add(titleLabel);

            Button minimizeButton = CreateHeaderButton("-", new Point(374, 14));
            minimizeButton.Click += delegate { WindowState = FormWindowState.Minimized; };
            Controls.Add(minimizeButton);

            Button closeButton = CreateHeaderButton("×", new Point(406, 14));
            closeButton.Click += delegate { Close(); };
            Controls.Add(closeButton);

            Panel cardPanel = new Panel();
            cardPanel.BackColor = Color.FromArgb(39, 44, 50);
            cardPanel.Location = new Point(22, 72);
            cardPanel.Size = new Size(404, 248);
            cardPanel.Paint += DrawCardBorder;
            Controls.Add(cardPanel);

            Label statusLabel = CreateMutedLabel("Status", new Point(18, 24));
            cardPanel.Controls.Add(statusLabel);

            statusValueLabel.AutoSize = true;
            statusValueLabel.Font = new Font("Segoe UI Semibold", 11F, FontStyle.Bold, GraphicsUnit.Point);
            statusValueLabel.ForeColor = Color.FromArgb(191, 199, 208);
            statusValueLabel.Location = new Point(18, 46);
            statusValueLabel.Text = "Disabled";
            cardPanel.Controls.Add(statusValueLabel);

            Label hotkeyLabel = CreateMutedLabel("AFK Hotkey", new Point(224, 24));
            cardPanel.Controls.Add(hotkeyLabel);

            Panel hotkeyShell = CreateInputShell(new Rectangle(224, 40, 156, 32));
            cardPanel.Controls.Add(hotkeyShell);

            hotkeyValueLabel.AutoSize = false;
            hotkeyValueLabel.Font = new Font("Segoe UI Semibold", 10.5F, FontStyle.Bold, GraphicsUnit.Point);
            hotkeyValueLabel.ForeColor = Color.FromArgb(241, 247, 244);
            hotkeyValueLabel.Location = new Point(12, 6);
            hotkeyValueLabel.Size = new Size(76, 20);
            hotkeyValueLabel.Text = FormatHotkey(currentToggleHotkey);
            hotkeyValueLabel.TextAlign = ContentAlignment.MiddleLeft;
            hotkeyShell.Controls.Add(hotkeyValueLabel);

            bindHotkeyButton.BackColor = Color.FromArgb(64, 101, 84);
            bindHotkeyButton.FlatAppearance.BorderSize = 0;
            bindHotkeyButton.FlatStyle = FlatStyle.Flat;
            bindHotkeyButton.Font = new Font("Segoe UI Semibold", 8.5F, FontStyle.Bold, GraphicsUnit.Point);
            bindHotkeyButton.ForeColor = Color.FromArgb(237, 248, 241);
            bindHotkeyButton.Location = new Point(94, 3);
            bindHotkeyButton.Size = new Size(58, 24);
            bindHotkeyButton.Text = "Bind";
            bindHotkeyButton.Click += BindHotkeyButtonClick;
            bindHotkeyButton.MouseEnter += delegate { bindHotkeyButton.BackColor = Color.FromArgb(84, 132, 108); };
            bindHotkeyButton.MouseLeave += delegate { bindHotkeyButton.BackColor = Color.FromArgb(64, 101, 84); };
            hotkeyShell.Controls.Add(bindHotkeyButton);

            Label intervalLabel = CreateMutedLabel("Interval", new Point(18, 90));
            cardPanel.Controls.Add(intervalLabel);

            Panel intervalShell = CreateInputShell(new Rectangle(18, 114, 108, 32));
            cardPanel.Controls.Add(intervalShell);

            intervalInput.BackColor = Color.FromArgb(25, 29, 33);
            intervalInput.BorderStyle = BorderStyle.None;
            intervalInput.Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold, GraphicsUnit.Point);
            intervalInput.ForeColor = Color.FromArgb(235, 244, 239);
            intervalInput.Location = new Point(12, 6);
            intervalInput.MaxLength = 4;
            intervalInput.Size = new Size(72, 18);
            intervalInput.TextAlign = HorizontalAlignment.Center;
            intervalInput.Text = currentIntervalMs.ToString();
            intervalInput.TextChanged += IntervalInputChanged;
            intervalInput.Leave += IntervalInputLeave;
            intervalShell.Controls.Add(intervalInput);

            Label intervalUnitLabel = CreateMutedLabel("ms", new Point(134, 122));
            intervalUnitLabel.ForeColor = Color.FromArgb(154, 164, 175);
            cardPanel.Controls.Add(intervalUnitLabel);

            Label keySequenceLabel = CreateMutedLabel("Keys", new Point(18, 152));
            cardPanel.Controls.Add(keySequenceLabel);

            Panel keySequenceShell = CreateInputShell(new Rectangle(18, 176, 224, 32));
            cardPanel.Controls.Add(keySequenceShell);

            keySequenceInput.BackColor = Color.FromArgb(25, 29, 33);
            keySequenceInput.BorderStyle = BorderStyle.None;
            keySequenceInput.CharacterCasing = CharacterCasing.Upper;
            keySequenceInput.Font = new Font("Segoe UI Semibold", 9.5F, FontStyle.Regular, GraphicsUnit.Point);
            keySequenceInput.ForeColor = Color.FromArgb(235, 244, 239);
            keySequenceInput.Location = new Point(12, 7);
            keySequenceInput.Size = new Size(198, 18);
            keySequenceInput.Text = currentSequenceText;
            keySequenceInput.TextChanged += KeySequenceInputChanged;
            keySequenceShell.Controls.Add(keySequenceInput);

            toggleSwitch.Location = new Point(268, 176);
            toggleSwitch.Size = new Size(104, 30);
            toggleSwitch.Font = new Font("Segoe UI Semibold", 8.5F, FontStyle.Bold, GraphicsUnit.Point);
            toggleSwitch.CheckedChanged += ToggleSwitchChanged;
            cardPanel.Controls.Add(toggleSwitch);

            Panel footerLine = new Panel();
            footerLine.BackColor = Color.FromArgb(58, 94, 79);
            footerLine.Location = new Point(22, 338);
            footerLine.Size = new Size(404, 1);
            Controls.Add(footerLine);

            Label developerLabel = CreateMutedLabel("Developers", new Point(24, 346));
            developerLabel.BackColor = Color.Transparent;
            Controls.Add(developerLabel);

            developerLink.ActiveLinkColor = Color.FromArgb(133, 241, 193);
            developerLink.AutoSize = true;
            developerLink.BackColor = Color.Transparent;
            developerLink.LinkColor = Color.FromArgb(133, 241, 193);
            developerLink.Location = new Point(112, 346);
            developerLink.Text = "1ce6epg";
            developerLink.VisitedLinkColor = Color.FromArgb(133, 241, 193);
            developerLink.LinkClicked += DeveloperLinkClicked;
            Controls.Add(developerLink);

            Label developerSeparator = CreateMutedLabel("·", new Point(164, 346));
            developerSeparator.BackColor = Color.Transparent;
            developerSeparator.ForeColor = Color.FromArgb(101, 125, 116);
            Controls.Add(developerSeparator);

            walkoudLink.ActiveLinkColor = Color.FromArgb(133, 241, 193);
            walkoudLink.AutoSize = true;
            walkoudLink.BackColor = Color.Transparent;
            walkoudLink.LinkColor = Color.FromArgb(133, 241, 193);
            walkoudLink.Location = new Point(178, 346);
            walkoudLink.Text = "Walkoud";
            walkoudLink.VisitedLinkColor = Color.FromArgb(133, 241, 193);
            walkoudLink.LinkClicked += WalkoudLinkClicked;
            Controls.Add(walkoudLink);
        }

        private Button CreateHeaderButton(string text, Point location)
        {
            Button button = new Button();
            button.FlatAppearance.BorderSize = 0;
            button.FlatStyle = FlatStyle.Flat;
            button.Font = new Font("Segoe UI", 11.5F, FontStyle.Bold, GraphicsUnit.Point);
            button.ForeColor = Color.FromArgb(155, 187, 172);
            button.Location = location;
            button.Size = new Size(28, 28);
            button.Text = text;
            button.UseVisualStyleBackColor = false;
            button.BackColor = Color.Transparent;
            button.MouseEnter += delegate { button.ForeColor = Color.FromArgb(221, 255, 234); };
            button.MouseLeave += delegate { button.ForeColor = Color.FromArgb(155, 187, 172); };
            return button;
        }

        private Label CreateMutedLabel(string text, Point location)
        {
            Label label = new Label();
            label.AutoSize = true;
            label.ForeColor = Color.FromArgb(129, 150, 143);
            label.Location = location;
            label.Text = text;
            return label;
        }

        private Panel CreateInputShell(Rectangle bounds)
        {
            Panel shell = new Panel();
            shell.BackColor = Color.FromArgb(25, 29, 33);
            shell.Location = bounds.Location;
            shell.Size = bounds.Size;
            shell.Padding = new Padding(0);
            shell.Paint += DrawInputShellBorder;
            return shell;
        }

        private void BindHotkeyButtonClick(object sender, EventArgs e)
        {
            captureHotkeyMode = true;
            hotkeyValueLabel.Text = "PRESS";
            bindHotkeyButton.Text = "Wait";
            Activate();
        }

        private void ToggleSwitchChanged(object sender, EventArgs e)
        {
            SetAntiAfkState(toggleSwitch.Checked);
        }

        private void IntervalInputChanged(object sender, EventArgs e)
        {
            string digitsOnly = SanitizeDigits(intervalInput.Text);
            if (digitsOnly != intervalInput.Text)
            {
                int caret = intervalInput.SelectionStart;
                intervalInput.TextChanged -= IntervalInputChanged;
                intervalInput.Text = digitsOnly;
                intervalInput.SelectionStart = Math.Min(caret, intervalInput.Text.Length);
                intervalInput.TextChanged += IntervalInputChanged;
            }

            int parsedInterval;
            if (int.TryParse(digitsOnly, out parsedInterval))
            {
                lock (settingsLock)
                {
                    currentIntervalMs = Math.Max(100, Math.Min(5000, parsedInterval));
                }

                SaveSettings();
            }
        }

        private void IntervalInputLeave(object sender, EventArgs e)
        {
            intervalInput.Text = currentIntervalMs.ToString();
        }

        private void KeySequenceInputChanged(object sender, EventArgs e)
        {
            string sanitized = SanitizeGroupInput(keySequenceInput.Text);
            string[] parsedGroups = ParseGroups(sanitized);
            if (parsedGroups.Length == 0)
            {
                return;
            }

            if (sanitized != keySequenceInput.Text)
            {
                int caret = keySequenceInput.SelectionStart;
                keySequenceInput.TextChanged -= KeySequenceInputChanged;
                keySequenceInput.Text = sanitized;
                keySequenceInput.SelectionStart = Math.Min(caret, keySequenceInput.Text.Length);
                keySequenceInput.TextChanged += KeySequenceInputChanged;
            }

            lock (settingsLock)
            {
                actionGroups = parsedGroups;
                currentSequenceText = sanitized;
                if (actionGroupIndex >= actionGroups.Length)
                {
                    actionGroupIndex = 0;
                }
            }

            SaveSettings();
        }

        private void UpdateUiState(bool enabled)
        {
            statusValueLabel.Text = enabled ? "Enabled" : "Disabled";
            statusValueLabel.ForeColor = enabled
                ? Color.FromArgb(141, 255, 183)
                : Color.FromArgb(191, 199, 208);
            accentPanel.BackColor = enabled
                ? AccentOnColor
                : AccentOffColor;
        }

        private void ToggleVisibility()
        {
            if (Visible)
            {
                Hide();
            }
            else
            {
                Show();
                WindowState = FormWindowState.Normal;
                Activate();
            }
        }

        private void ToggleAntiAfkState()
        {
            SetAntiAfkState(!antiAfkEnabled);
        }

        private void SetAntiAfkState(bool enabled)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<bool>(SetAntiAfkState), enabled);
                return;
            }

            if (antiAfkEnabled == enabled)
            {
                if (toggleSwitch.Checked != enabled)
                {
                    toggleSwitch.CheckedChanged -= ToggleSwitchChanged;
                    toggleSwitch.Checked = enabled;
                    toggleSwitch.CheckedChanged += ToggleSwitchChanged;
                }
                return;
            }

            antiAfkEnabled = enabled;

            toggleSwitch.CheckedChanged -= ToggleSwitchChanged;
            toggleSwitch.Checked = enabled;
            toggleSwitch.CheckedChanged += ToggleSwitchChanged;

            if (enabled)
            {
                actionGroupIndex = 0;
                SetThreadExecutionState(EsContinuous | EsSystemRequired | EsDisplayRequired);
                StartAntiAfkWorker();
            }
            else
            {
                StopAntiAfkWorker();
            }

            UpdateUiState(enabled);
        }

        private void ApplyHotkey(Keys newHotkey)
        {
            captureHotkeyMode = false;

            if (toggleHotkeyRegistered)
            {
                UnregisterHotKey(Handle, ToggleHotkeyId);
                toggleHotkeyRegistered = false;
            }

            if (newHotkey == Keys.None)
            {
                currentToggleHotkey = Keys.None;
                hotkeyValueLabel.Text = FormatHotkey(currentToggleHotkey);
                bindHotkeyButton.Text = "Bind";
                SaveSettings();
                return;
            }

            if (!RegisterHotKey(Handle, ToggleHotkeyId, ModNone, (uint)newHotkey))
            {
                if (currentToggleHotkey != Keys.None)
                {
                    RegisterHotKey(Handle, ToggleHotkeyId, ModNone, (uint)currentToggleHotkey);
                    toggleHotkeyRegistered = true;
                }

                hotkeyValueLabel.Text = FormatHotkey(currentToggleHotkey);
                bindHotkeyButton.Text = "Busy";
                MessageBox.Show(
                    "This hotkey is already used by another app. Pick a different one.",
                    "Hotkey busy",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            currentToggleHotkey = newHotkey;
            toggleHotkeyRegistered = true;
            hotkeyValueLabel.Text = FormatHotkey(currentToggleHotkey);
            bindHotkeyButton.Text = "Bind";
            SaveSettings();
        }

        private void RegisterHotkeys()
        {
            menuHotkeyRegistered = RegisterHotKey(Handle, MenuHotkeyId, ModNone, (uint)Keys.Home);
            if (currentToggleHotkey != Keys.None)
            {
                toggleHotkeyRegistered = RegisterHotKey(Handle, ToggleHotkeyId, ModNone, (uint)currentToggleHotkey);
            }
        }

        private void UnregisterHotkeys()
        {
            if (menuHotkeyRegistered)
            {
                UnregisterHotKey(Handle, MenuHotkeyId);
                menuHotkeyRegistered = false;
            }

            if (toggleHotkeyRegistered)
            {
                UnregisterHotKey(Handle, ToggleHotkeyId);
                toggleHotkeyRegistered = false;
            }
        }

        private void StartAntiAfkWorker()
        {
            if (activityThread != null && activityThread.IsAlive)
            {
                return;
            }

            activityThread = new Thread(ActivityLoop);
            activityThread.IsBackground = true;
            activityThread.Start();
        }

        private void StopAntiAfkWorker()
        {
            antiAfkEnabled = false;
            SetThreadExecutionState(EsContinuous);

            if (activityThread == null)
            {
                return;
            }

            if (activityThread.IsAlive)
            {
                activityThread.Join(1000);
            }

            activityThread = null;

            if (!string.IsNullOrEmpty(activePressedGroup))
            {
                ReleaseGroup(activePressedGroup);
                activePressedGroup = string.Empty;
            }
        }

        private void ActivityLoop()
        {
            while (antiAfkEnabled)
            {
                string group;
                int intervalMs;

                lock (settingsLock)
                {
                    if (actionGroups.Length == 0)
                    {
                        group = "W";
                    }
                    else
                    {
                        group = actionGroups[actionGroupIndex];
                        actionGroupIndex++;
                        if (actionGroupIndex >= actionGroups.Length)
                        {
                            actionGroupIndex = 0;
                        }
                    }

                    intervalMs = currentIntervalMs;
                }

                SetThreadExecutionState(EsContinuous | EsSystemRequired | EsDisplayRequired);
                activePressedGroup = group;
                PressGroup(group);
                if (!SleepInterruptible(intervalMs))
                {
                    ReleaseGroup(group);
                    activePressedGroup = string.Empty;
                    break;
                }

                ReleaseGroup(group);
                activePressedGroup = string.Empty;

                if (!SleepInterruptible(60))
                {
                    break;
                }
            }
        }

        private bool SleepInterruptible(int totalMs)
        {
            int remainingMs = totalMs;
            while (antiAfkEnabled && remainingMs > 0)
            {
                int chunkMs = Math.Min(remainingMs, 25);
                Thread.Sleep(chunkMs);
                remainingMs -= chunkMs;
            }

            return antiAfkEnabled;
        }

        private static void PressGroup(string group)
        {
            foreach (char key in group)
            {
                SendKeyDown(key);
            }
        }

        private static void ReleaseGroup(string group)
        {
            foreach (char key in group)
            {
                SendKeyUp(key);
            }
        }

        private static bool IsBindableHotkey(Keys keyCode)
        {
            if (keyCode == Keys.None)
            {
                return true;
            }

            if (keyCode >= Keys.A && keyCode <= Keys.Z)
            {
                return true;
            }

            if (keyCode >= Keys.D0 && keyCode <= Keys.D9)
            {
                return true;
            }

            if (keyCode >= Keys.F1 && keyCode <= Keys.F12)
            {
                return true;
            }

            return keyCode == Keys.Home || keyCode == Keys.Insert || keyCode == Keys.Delete || keyCode == Keys.End;
        }

        private static string FormatHotkey(Keys key)
        {
            return key == Keys.None ? "NONE" : key.ToString().ToUpperInvariant();
        }

        private static string SanitizeDigits(string input)
        {
            StringBuilder builder = new StringBuilder();
            foreach (char c in input)
            {
                if (char.IsDigit(c))
                {
                    builder.Append(c);
                }
            }

            return builder.ToString();
        }

        private static string SanitizeGroupInput(string input)
        {
            StringBuilder builder = new StringBuilder();
            foreach (char c in input.ToUpperInvariant())
            {
                if (char.IsLetterOrDigit(c) || c == ',' || c == ' ')
                {
                    builder.Append(c);
                }
            }

            return builder.ToString().Replace(" ", string.Empty);
        }

        private static string[] ParseGroups(string input)
        {
            string[] tokens = input.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            StringBuilder cleanToken = new StringBuilder();
            System.Collections.Generic.List<string> groups = new System.Collections.Generic.List<string>();

            foreach (string token in tokens)
            {
                cleanToken.Clear();
                foreach (char c in token)
                {
                    if (char.IsLetterOrDigit(c))
                    {
                        cleanToken.Append(c);
                    }
                }

                if (cleanToken.Length > 0)
                {
                    groups.Add(cleanToken.ToString());
                }
            }

            return groups.ToArray();
        }

        private void LoadSettings()
        {
            actionGroups = ParseGroups(DefaultSequence);
            currentSequenceText = DefaultSequence;
            currentIntervalMs = 450;
            currentToggleHotkey = Keys.None;

            if (!File.Exists(ConfigPath))
            {
                return;
            }

            try
            {
                foreach (string line in File.ReadAllLines(ConfigPath))
                {
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }

                    int separatorIndex = line.IndexOf('=');
                    if (separatorIndex <= 0)
                    {
                        continue;
                    }

                    string key = line.Substring(0, separatorIndex).Trim().ToLowerInvariant();
                    string value = line.Substring(separatorIndex + 1).Trim();

                    if (key == "interval")
                    {
                        int parsedInterval;
                        if (int.TryParse(value, out parsedInterval))
                        {
                            currentIntervalMs = Math.Max(100, Math.Min(5000, parsedInterval));
                        }
                        continue;
                    }

                    if (key == "sequence")
                    {
                        string sanitized = SanitizeGroupInput(value);
                        string[] parsedGroups = ParseGroups(sanitized);
                        if (parsedGroups.Length > 0)
                        {
                            currentSequenceText = sanitized;
                            actionGroups = parsedGroups;
                        }
                        continue;
                    }

                    if (key == "toggle_hotkey")
                    {
                        Keys parsedHotkey;
                        if (TryParseHotkey(value, out parsedHotkey) && IsBindableHotkey(parsedHotkey))
                        {
                            currentToggleHotkey = parsedHotkey;
                        }
                    }
                }
            }
            catch
            {
                currentSequenceText = DefaultSequence;
                currentIntervalMs = 450;
                currentToggleHotkey = Keys.None;
                actionGroups = ParseGroups(DefaultSequence);
            }
        }

        private void ClearHotkey()
        {
            ApplyHotkey(Keys.None);
        }

        private void SaveSettings()
        {
            string sequenceSnapshot;
            int intervalSnapshot;
            Keys hotkeySnapshot;

            lock (settingsLock)
            {
                sequenceSnapshot = currentSequenceText;
                intervalSnapshot = currentIntervalMs;
                hotkeySnapshot = currentToggleHotkey;
            }

            StringBuilder configBuilder = new StringBuilder();
            configBuilder.AppendLine("interval=" + intervalSnapshot);
            configBuilder.AppendLine("sequence=" + sequenceSnapshot);
            configBuilder.AppendLine("toggle_hotkey=" + hotkeySnapshot);

            try
            {
                File.WriteAllText(ConfigPath, configBuilder.ToString());
            }
            catch
            {
            }
        }

        private static bool TryParseHotkey(string value, out Keys parsedHotkey)
        {
            try
            {
                parsedHotkey = (Keys)Enum.Parse(typeof(Keys), value, true);
                return true;
            }
            catch
            {
                parsedHotkey = Keys.None;
                return false;
            }
        }

        private void DeveloperLinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            Process.Start(new ProcessStartInfo(DeveloperUrl) { UseShellExecute = true });
        }

        private void WalkoudLinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            Process.Start(new ProcessStartInfo(WalkoudUrl) { UseShellExecute = true });
        }

        private static void SendKeyDown(char key)
        {
            byte vk = (byte)char.ToUpperInvariant(key);
            keybd_event(vk, 0, 0, UIntPtr.Zero);
        }

        private static void SendKeyUp(char key)
        {
            byte vk = (byte)char.ToUpperInvariant(key);
            keybd_event(vk, 0, 0x0002, UIntPtr.Zero);
        }

        private void DragForm(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left)
            {
                return;
            }

            ReleaseCapture();
            SendMessage(Handle, WmNclbuttondown, HtCaption, 0);
        }

        private static void DrawCardBorder(object sender, PaintEventArgs e)
        {
            Control panel = (Control)sender;
            using (Pen pen = new Pen(Color.FromArgb(66, 92, 84)))
            {
                Rectangle border = new Rectangle(0, 0, panel.Width - 1, panel.Height - 1);
                e.Graphics.DrawRectangle(pen, border);
            }
        }

        private static void DrawInputShellBorder(object sender, PaintEventArgs e)
        {
            Control panel = (Control)sender;
            using (Pen pen = new Pen(Color.FromArgb(58, 103, 85)))
            {
                Rectangle border = new Rectangle(0, 0, panel.Width - 1, panel.Height - 1);
                e.Graphics.DrawRectangle(pen, border);
            }
        }

        private void DrawFormBackground(object sender, PaintEventArgs e)
        {
            Graphics graphics = e.Graphics;
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;

            using (SolidBrush baseBrush = new SolidBrush(FormBaseColor))
            {
                graphics.FillRectangle(baseBrush, ClientRectangle);
            }

            DrawGlow(graphics, new Rectangle(-80, 220, 260, 220), Color.FromArgb(70, 167, 255, 85));
            DrawGlow(graphics, new Rectangle(250, 40, 220, 260), Color.FromArgb(60, 43, 221, 183));
            DrawGlow(graphics, new Rectangle(165, 150, 180, 180), Color.FromArgb(42, 75, 235, 196));

            using (Pen gridPen = new Pen(Color.FromArgb(16, 190, 238, 214)))
            {
                for (int x = 0; x < Width; x += 52)
                {
                    if (x < 18 || x > Width - 22)
                    {
                        graphics.DrawLine(gridPen, x, 0, x, Height);
                        continue;
                    }

                    graphics.DrawLine(gridPen, x, 72, x, 320);
                }

                for (int y = 0; y < Height; y += 52)
                {
                    if (y >= 72 && y <= 320)
                    {
                        graphics.DrawLine(gridPen, 18, y, Width - 22, y);
                    }
                }
            }
        }

        private static void DrawGlow(Graphics graphics, Rectangle bounds, Color color)
        {
            using (System.Drawing.Drawing2D.GraphicsPath path = new System.Drawing.Drawing2D.GraphicsPath())
            {
                path.AddEllipse(bounds);
                using (System.Drawing.Drawing2D.PathGradientBrush brush = new System.Drawing.Drawing2D.PathGradientBrush(path))
                {
                    brush.CenterColor = color;
                    brush.SurroundColors = new[] { Color.FromArgb(0, color) };
                    graphics.FillPath(brush, path);
                }
            }
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("kernel32.dll")]
        private static extern uint SetThreadExecutionState(uint esFlags);

        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);
    }

    internal sealed class ToggleSwitch : CheckBox
    {
        private readonly System.Windows.Forms.Timer animationTimer;
        private float animationProgress;
        private float targetProgress;

        public ToggleSwitch()
        {
            AutoSize = false;
            Cursor = Cursors.Hand;
            DoubleBuffered = true;
            MinimumSize = new Size(104, 30);
            animationTimer = new System.Windows.Forms.Timer();
            animationTimer.Interval = 15;
            animationTimer.Tick += AnimateToggle;
            animationProgress = Checked ? 1F : 0F;
            targetProgress = animationProgress;
        }

        protected override void OnCheckedChanged(EventArgs e)
        {
            base.OnCheckedChanged(e);
            targetProgress = Checked ? 1F : 0F;
            animationTimer.Start();
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs pevent)
        {
            pevent.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            pevent.Graphics.Clear(Parent.BackColor);

            Rectangle track = new Rectangle(0, 0, Width - 1, Height - 1);
            Color trackColor = InterpolateColor(
                Color.FromArgb(92, 102, 112),
                Color.FromArgb(126, 231, 135),
                animationProgress);
            using (Brush trackBrush = new SolidBrush(trackColor))
            {
                pevent.Graphics.FillRoundedRectangle(trackBrush, track, 15);
            }

            int knobX = (int)Math.Round(4 + ((Width - 34) * animationProgress));
            Rectangle knob = new Rectangle(knobX, 4, 26, 22);
            using (Brush knobBrush = new SolidBrush(Color.FromArgb(230, 237, 243)))
            {
                pevent.Graphics.FillEllipse(knobBrush, knob);
            }

            string stateText = Checked ? "ON" : "OFF";
            Color textColor = InterpolateColor(
                Color.FromArgb(230, 237, 243),
                Color.FromArgb(18, 22, 27),
                animationProgress);
            using (Brush textBrush = new SolidBrush(textColor))
            {
                using (StringFormat format = new StringFormat())
                {
                    format.Alignment = StringAlignment.Near;
                    format.LineAlignment = StringAlignment.Center;
                    Rectangle textRect = new Rectangle(36, 0, 38, Height);
                    pevent.Graphics.DrawString(stateText, Font, textBrush, textRect, format);
                }
            }
        }

        private void AnimateToggle(object sender, EventArgs e)
        {
            animationProgress += (targetProgress - animationProgress) * 0.28F;

            if (Math.Abs(targetProgress - animationProgress) < 0.015F)
            {
                animationProgress = targetProgress;
                animationTimer.Stop();
            }

            Invalidate();
        }

        private static Color InterpolateColor(Color from, Color to, float progress)
        {
            int a = (int)Math.Round(from.A + ((to.A - from.A) * progress));
            int r = (int)Math.Round(from.R + ((to.R - from.R) * progress));
            int g = (int)Math.Round(from.G + ((to.G - from.G) * progress));
            int b = (int)Math.Round(from.B + ((to.B - from.B) * progress));
            return Color.FromArgb(a, r, g, b);
        }
    }

    internal static class GraphicsExtensions
    {
        public static void FillRoundedRectangle(this Graphics graphics, Brush brush, Rectangle bounds, int radius)
        {
            using (System.Drawing.Drawing2D.GraphicsPath path = CreateRoundedRectanglePath(bounds, radius))
            {
                graphics.FillPath(brush, path);
            }
        }

        private static System.Drawing.Drawing2D.GraphicsPath CreateRoundedRectanglePath(Rectangle bounds, int radius)
        {
            int diameter = radius * 2;
            Rectangle arc = new Rectangle(bounds.Location, new Size(diameter, diameter));
            System.Drawing.Drawing2D.GraphicsPath path = new System.Drawing.Drawing2D.GraphicsPath();

            path.AddArc(arc, 180, 90);
            arc.X = bounds.Right - diameter;
            path.AddArc(arc, 270, 90);
            arc.Y = bounds.Bottom - diameter;
            path.AddArc(arc, 0, 90);
            arc.X = bounds.Left;
            path.AddArc(arc, 90, 90);
            path.CloseFigure();

            return path;
        }
    }
}
