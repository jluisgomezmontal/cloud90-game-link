// Cloud90 Game Link: the window. A tab per game (Hades; KOF 2002 and Metal Slug 3 in MAME),
// each with its own token, connection, folder, "open the game" and panic.
using System;
using System.Drawing;
using System.IO;
using System.Net;
using System.Runtime.Versioning;
using System.Threading;
using System.Windows.Forms;

// Built with the in-box csc, which marks no framework: without this the
// runtime keeps .NET 4.0's defaults (no TLS 1.2), and Cloud90's server only
// speaks TLS 1.2+.
[assembly: TargetFramework(".NETFramework,Version=v4.8", FrameworkDisplayName = ".NET Framework 4.8")]

namespace Cloud90.GameLink
{
    // What a tab needs from its game.
    internal abstract class GameHost
    {
        public event Action<string> Status;
        protected void ReportStatus(string text) { var handler = Status; if (handler != null) handler(text); }
        public abstract string Id { get; }
        public abstract string Title { get; }
        public abstract string FolderButton { get; }
        public abstract string FolderPrompt { get; }
        // "" when the game is ready to open, else what's missing.
        public abstract string Problem(Settings s);
        public abstract bool UseFolder(Settings s, string dir);
        public abstract void FindFolder(Settings s);
        public abstract string Folder(Settings s);
        // Installs the game side; returns the inbox for the link.
        public abstract Inbox Prepare(Settings s);
        public abstract void Start(Settings s, Link link, Action exited);
        public abstract bool Running { get; }
        public abstract bool AnotherRunning();
        public abstract void Uninstall(Settings s);
    }

    internal sealed class HadesHost : GameHost
    {
        readonly HadesProcess game = new HadesProcess();
        Action onExit;
        public HadesHost() { game.Exited += delegate { if (onExit != null) onExit(); }; }
        public bool Vulkan;
        public override string Id { get { return "hades"; } }
        public override string Title { get { return "Hades"; } }
        public override string FolderButton { get { return "Carpeta de Hades…"; } }
        public override string FolderPrompt { get { return "La carpeta de Hades (la que tiene Content y x64)"; } }
        public override string Problem(Settings s) { return HadesFolder.IsHades(s.HadesDir) ? "" : "no encontré Hades — elige su carpeta"; }
        public override bool UseFolder(Settings s, string dir) { if (!HadesFolder.IsHades(dir)) return false; s.HadesDir = dir; return true; }
        public override void FindFolder(Settings s) { if (!HadesFolder.IsHades(s.HadesDir)) s.HadesDir = HadesFolder.Find(); }
        public override string Folder(Settings s) { return s.HadesDir; }
        public override Inbox Prepare(Settings s) { ModInstaller.Install(s.HadesDir); return new Inbox(ModInstaller.Inbox(s.HadesDir)); }
        public override void Start(Settings s, Link link, Action exited)
        {
            game.ModLine -= link.OnModLine;
            game.ModLine += link.OnModLine;
            onExit = exited;
            game.Start(s.HadesDir, Vulkan);
        }
        public override bool Running { get { return game.Running; } }
        public override bool AnotherRunning() { return HadesProcess.AnotherRunning(); }
        public override void Uninstall(Settings s) { ModInstaller.Uninstall(s.HadesDir); }
    }

    internal sealed class MameHost : GameHost
    {
        readonly MameProcess game = new MameProcess();
        readonly string set, bios, title;
        Action onExit;
        public MameHost(string set, string bios, string title)
        {
            this.set = set;
            this.bios = bios;
            this.title = title;
            game.Exited += delegate { if (onExit != null) onExit(); };
        }
        public override string Id { get { return set; } }
        public override string Title { get { return title; } }
        public override string FolderButton { get { return "Carpeta de MAME…"; } }
        public override string FolderPrompt { get { return "La carpeta de MAME (la que tiene mame.exe y roms)"; } }
        public override string Problem(Settings s)
        {
            if (!MameFolder.IsMame(s.MameDir)) return "no encontré MAME — instálalo (mamedev.org) y elige su carpeta";
            return MameFolder.Missing(s.MameDir, set, bios);
        }
        public override bool UseFolder(Settings s, string dir) { if (!MameFolder.IsMame(dir)) return false; s.MameDir = dir; return true; }
        public override void FindFolder(Settings s) { if (!MameFolder.IsMame(s.MameDir)) s.MameDir = MameFolder.Find(); }
        public override string Folder(Settings s) { return s.MameDir; }
        public override Inbox Prepare(Settings s) { MamePlugin.Install(s.MameDir); return new Inbox(MameProcess.InboxPath(set)); }
        public override void Start(Settings s, Link link, Action exited)
        {
            game.GameLine -= link.OnModLine;
            game.GameLine += link.OnModLine;
            onExit = exited;
            game.Start(s.MameDir, set);
        }
        public override bool Running { get { return game.Running; } }
        public override bool AnotherRunning() { return MameProcess.AnotherRunning(); }
        public override void Uninstall(Settings s) { MamePlugin.Uninstall(s.MameDir); }
    }

    // One game's tab.
    internal sealed class GamePanel : Panel
    {
        readonly MainForm form;
        readonly GameHost host;
        readonly Link link;
        readonly Label cloudStatus = new Label();
        readonly Label gameStatus = new Label();
        readonly Button tokenButton = new Button();
        readonly Button playButton = new Button();
        readonly Button pauseButton = new Button();
        readonly Button folderButton = new Button();
        readonly Button uninstallButton = new Button();
        readonly Label folderPath = new Label();
        readonly Label connectionHint = new Label();
        readonly ToolTip details = new ToolTip();

        public string TabTitle { get { return host.Title + (host is MameHost ? " (MAME)" : ""); } }
        public string GameId { get { return host.Id; } }

        public GamePanel(MainForm form, GameHost host)
        {
            this.form = form;
            this.host = host;
            BackColor = Design.Background;
            AutoScroll = true;
            link = new Link(delegate { return form.Settings; }, host.Id, host.Title);
            host.Status += delegate(string text) { form.OnUi(delegate { gameStatus.Text = text; gameStatus.ForeColor = Design.Muted; }); };
            link.Status += delegate (string text) { form.OnUi(delegate {
                cloudStatus.Text = text;
                pauseButton.Text = link.Paused ? "Reanudar efectos" : "Pausar efectos";
                pauseButton.AccessibleName = pauseButton.Text;
                pauseButton.BackColor = link.Paused ? Color.FromArgb(68, 35, 60) : Design.Raised;
                pauseButton.FlatAppearance.BorderColor = link.Paused ? Design.Pink : Design.Border;
            }); };

            var layout = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, RowCount = 5, Padding = new Padding(24, 0, 24, 20), BackColor = Design.Background };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 218));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 172));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 110));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 110));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
            Controls.Add(layout);

            var banner = new GameBanner(host.Id) { Size = new Size(828, 200), Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 18) };
            Label edition = Design.Label(host is MameHost ? "Arcade / MAME" : "PC / Windows", 10, Color.FromArgb(196, 167, 255));
            edition.SetBounds(24, 28, 200, 24);
            Label heading = Design.Label(host.Title, 27, Design.Text);
            heading.Font = new Font("Segoe UI Semibold", 27);
            heading.SetBounds(22, 64, 610, 54); heading.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right;
            Label description = Design.Label(Design.Description(host.Id), 11, Design.Muted);
            description.SetBounds(24, 128, 510, 50); description.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right;
            var instructions = new Button(); Design.Button(instructions, "Cómo &conectar y jugar\r\nVer requisitos, conexión del token y pasos de este juego", false);
            instructions.Dock = DockStyle.Fill;
            instructions.Margin = new Padding(0, 8, 0, 4);
            instructions.Padding = new Padding(16, 8, 16, 8);
            instructions.TextAlign = ContentAlignment.MiddleLeft;
            instructions.BackColor = Color.FromArgb(14, 40, 49);
            instructions.ForeColor = Color.FromArgb(165, 243, 252);
            instructions.FlatAppearance.BorderColor = Design.Cyan;
            instructions.FlatAppearance.MouseOverBackColor = Color.FromArgb(18, 57, 68);
            instructions.FlatAppearance.MouseDownBackColor = Color.FromArgb(22, 70, 83);
            instructions.AccessibleName = "Cómo conectar y jugar: instrucciones de " + host.Title;
            instructions.AccessibleDescription = "Abre la guía de requisitos, token, inicio de partida y efectos de este juego.";
            instructions.Click += delegate { using (var guide = new GameInstructions(host.Id, host.Title)) { guide.Icon = form.Icon; guide.ShowDialog(form); } };
            banner.Controls.AddRange(new Control[] { edition, heading, description });
            layout.Controls.Add(banner, 0, 0);

            var actions = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3, Margin = Padding.Empty };
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60)); actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
            actions.RowStyles.Add(new RowStyle(SizeType.Absolute, 48)); actions.RowStyles.Add(new RowStyle(SizeType.Absolute, 78)); actions.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            Design.Button(playButton, "Abrir juego", true); Design.Button(pauseButton, "Pausar efectos", false);
            playButton.Dock = DockStyle.Fill; playButton.Margin = new Padding(0, 0, 8, 4);
            pauseButton.Dock = DockStyle.Fill; pauseButton.Margin = new Padding(8, 0, 0, 4);
            Label playNote = Design.Label(Design.PlayNote(host.Id), 9, Design.Muted); playNote.Dock = DockStyle.Fill; playNote.Margin = new Padding(0, 7, 0, 0);
            actions.Controls.Add(playButton, 0, 0); actions.Controls.Add(pauseButton, 1, 0);
            actions.Controls.Add(instructions, 0, 1); actions.SetColumnSpan(instructions, 2);
            actions.Controls.Add(playNote, 0, 2); actions.SetColumnSpan(playNote, 2);
            layout.Controls.Add(actions, 0, 1);

            var connection = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Design.Surface, Padding = new Padding(16, 12, 16, 10), ColumnCount = 2, RowCount = 3, Margin = new Padding(0, 0, 0, 12) };
            connection.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); connection.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 145));
            connection.RowStyles.Add(new RowStyle(SizeType.Absolute, 23)); connection.RowStyles.Add(new RowStyle(SizeType.Absolute, 24)); connection.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            Label connectionTitle = Design.Label("Conexión con Cloud90", 10, Design.Text); connectionTitle.Dock = DockStyle.Fill;
            cloudStatus.ForeColor = Design.Cyan; cloudStatus.AutoEllipsis = true; cloudStatus.Dock = DockStyle.Fill;
            connectionHint.ForeColor = Design.Muted; connectionHint.AutoEllipsis = true; connectionHint.Dock = DockStyle.Fill; connectionHint.Font = new Font("Segoe UI", 9);
            connectionHint.Text = "Genera tu token en Cloud90 › Juegos › " + host.Title + ".";
            Design.Button(tokenButton, "Pegar token", false); tokenButton.Dock = DockStyle.Fill; tokenButton.Margin = new Padding(8, 8, 0, 8);
            connection.Controls.Add(connectionTitle, 0, 0); connection.Controls.Add(cloudStatus, 0, 1); connection.Controls.Add(connectionHint, 0, 2); connection.Controls.Add(tokenButton, 1, 0); connection.SetRowSpan(tokenButton, 3);
            layout.Controls.Add(connection, 0, 2);

            var installation = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Design.Surface, Padding = new Padding(16, 12, 16, 10), ColumnCount = 2, RowCount = 3, Margin = new Padding(0, 0, 0, 12) };
            installation.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); installation.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 145));
            installation.RowStyles.Add(new RowStyle(SizeType.Absolute, 23)); installation.RowStyles.Add(new RowStyle(SizeType.Absolute, 24)); installation.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            Label installationTitle = Design.Label("Instalación del juego", 10, Design.Text); installationTitle.Dock = DockStyle.Fill;
            gameStatus.Dock = DockStyle.Fill; gameStatus.AutoEllipsis = true; gameStatus.ForeColor = Design.Muted;
            folderPath.Dock = DockStyle.Fill; folderPath.AutoEllipsis = true; folderPath.ForeColor = Design.Muted; folderPath.Font = new Font("Segoe UI", 9);
            Design.Button(folderButton, "Elegir carpeta", false); folderButton.Dock = DockStyle.Fill; folderButton.Margin = new Padding(8, 8, 0, 8);
            installation.Controls.Add(installationTitle, 0, 0); installation.Controls.Add(gameStatus, 0, 1); installation.Controls.Add(folderPath, 0, 2); installation.Controls.Add(folderButton, 1, 0); installation.SetRowSpan(folderButton, 3);
            gameStatus.TextChanged += delegate { details.SetToolTip(gameStatus, gameStatus.Text); };
            folderPath.TextChanged += delegate { details.SetToolTip(folderPath, folderPath.Text); };
            details.SetToolTip(pauseButton, "Detiene los efectos de este juego. Puedes reanudarlos cuando quieras.");
            details.SetToolTip(uninstallButton, "Quita la integración de Cloud90; conserva el juego y tus partidas.");
            layout.Controls.Add(installation, 0, 3);

            var options = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = Padding.Empty };
            Design.Button(uninstallButton, "Quitar integración…", false); uninstallButton.Width = 180;
            uninstallButton.FlatAppearance.BorderSize = 0; uninstallButton.BackColor = Design.Background; uninstallButton.ForeColor = Design.Muted;
            options.Controls.Add(uninstallButton); layout.Controls.Add(options, 0, 4);
            if (host is HadesHost)
            {
                CheckBox vulkan = new CheckBox();
                vulkan.Text = "Usar Vulkan";
                vulkan.AutoSize = true; vulkan.Margin = new Padding(16, 12, 0, 0);
                vulkan.CheckedChanged += delegate { ((HadesHost)host).Vulkan = vulkan.Checked; };
                options.Controls.Add(vulkan);
            }

            tokenButton.Click += delegate { PasteToken(); };
            playButton.Click += delegate { Play(); };
            pauseButton.Click += delegate { link.SetPaused(!link.Paused); pauseButton.Text = link.Paused ? "Reanudar efectos" : "Pausar efectos"; };
            folderButton.Click += delegate { PickFolder(); };
            uninstallButton.Click += delegate { Uninstall(); };
        }

        protected override void Dispose(bool disposing) { if (disposing) details.Dispose(); base.Dispose(disposing); }

        public void Startup()
        {
            host.FindFolder(form.Settings);
            form.Settings.Save();
            Prepare();
            cloudStatus.Text = form.Settings.TokenFor(host.Id).Length > 0 ? "Conectando…" : "Falta conectar tu cuenta";
            link.Start();
        }

        public void Stop() { link.Stop(); }

        void Prepare()
        {
            folderPath.Text = host.Folder(form.Settings);
            string problem = host.Problem(form.Settings);
            if (problem.Length > 0)
            {
                gameStatus.Text = problem;
                gameStatus.ForeColor = Color.FromArgb(251, 191, 36);
                playButton.Enabled = false;
                return;
            }
            try
            {
                link.UseInbox(host.Prepare(form.Settings));
                gameStatus.Text = "Integración lista";
                gameStatus.ForeColor = Design.Success;
                playButton.Enabled = !host.Running;
            }
            catch (Exception e)
            {
                gameStatus.Text = "No se pudo preparar: " + e.Message;
                gameStatus.ForeColor = Color.FromArgb(248, 113, 113);
                playButton.Enabled = false;
            }
        }

        void PasteToken()
        {
            string text = Clipboard.ContainsText() ? Clipboard.GetText().Trim() : "";
            if (!text.StartsWith("c90gl_"))
            {
                MessageBox.Show(form, "Copia primero tu token de Cloud90 (Juegos › " + host.Title + " › Generar token).", form.Text);
                return;
            }
            form.Settings.Tokens[host.Id] = text;
            form.Settings.Save();
            connectionHint.Text = "Token guardado. Puedes sustituirlo con Pegar token.";
            Log.Write(host.Title + ": token guardado");
            link.Start();
        }

        void Play()
        {
            if (host.Running) return;
            if (host.AnotherRunning())
            {
                MessageBox.Show(form, host.Title + " ya está abierto. Ciérralo y ábrelo desde aquí: así Cloud90 Game Link puede leer lo que responde el juego.", form.Text);
                return;
            }
            try
            {
                Prepare();
                if (!playButton.Enabled) return;
                host.Start(form.Settings, link, delegate
                {
                    link.OnGameExited();
                    form.OnUi(delegate { gameStatus.Text = "Juego cerrado. Puedes abrirlo de nuevo."; playButton.Text = "Abrir juego"; playButton.Enabled = true; folderButton.Enabled = true; });
                });
                gameStatus.Text = "Juego abierto desde Game Link";
                playButton.Text = "Juego abierto";
                playButton.Enabled = false;
                folderButton.Enabled = false;
            }
            catch (Exception e)
            {
                MessageBox.Show(form, "No se pudo abrir " + host.Title + ": " + e.Message, form.Text);
            }
        }

        void PickFolder()
        {
            using (FolderBrowserDialog d = new FolderBrowserDialog())
            {
                d.Description = host.FolderPrompt;
                if (d.ShowDialog(form) != DialogResult.OK) return;
                if (!host.UseFolder(form.Settings, d.SelectedPath))
                {
                    MessageBox.Show(form, "Esa carpeta no es la que busco: " + host.FolderPrompt + ".", form.Text);
                    return;
                }
                form.Settings.Save();
                Prepare();
            }
        }

        void Uninstall()
        {
            if (host.Problem(form.Settings).StartsWith("no encontré")) return;
            if (host.Running)
            {
                MessageBox.Show(form, "Cierra " + host.Title + " primero.", form.Text);
                return;
            }
            if (MessageBox.Show(form, "¿Quitar lo de Cloud90 de " + host.Title + "? El juego queda como estaba.", form.Text, MessageBoxButtons.YesNo) != DialogResult.Yes) return;
            try
            {
                host.Uninstall(form.Settings);
                gameStatus.Text = host.Title + ": desinstalado (se instala otra vez al abrir Cloud90 Game Link)";
                playButton.Enabled = false;
            }
            catch (Exception e)
            {
                MessageBox.Show(form, "No se pudo desinstalar: " + e.Message, form.Text);
            }
        }
    }

    internal sealed class MainForm : Form
    {
        public Settings Settings = Settings.Load();
        readonly SynchronizationContext ui;
        readonly Panel workspace = new Panel();
        readonly Panel activity = new Panel();
        readonly Button activityToggle = new Button();
        readonly TextBox log = new TextBox();
        readonly GamePanel[] panels;
        readonly GameNavigationButton[] navigation;
        bool loaded;

        public MainForm()
        {
            ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
            Text = "Cloud90 Game Link";
            ClientSize = new Size(1120, 800);
            MinimumSize = new Size(980, 740);
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleDimensions = new SizeF(96, 96);
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Segoe UI", 9.5f);
            BackColor = Design.Background;
            ForeColor = Design.Text;
            using (Stream icon = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("Art.cloud90.ico"))
                if (icon != null) Icon = new Icon(icon);

            var shell = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Margin = Padding.Empty, Padding = Padding.Empty };
            shell.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 244)); shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 88)); shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            Controls.Add(shell);

            var header = new Panel { Size = new Size(1120, 88), Dock = DockStyle.Fill, Margin = Padding.Empty, BackColor = Design.Background };
            var logo = new PictureBox { Image = Design.Art("cloud90.png"), SizeMode = PictureBoxSizeMode.Zoom, BackColor = Design.Background };
            logo.SetBounds(24, 19, 46, 46); logo.AccessibleName = "Cloud90 Live";
            Label brand = Design.Label("Cloud90 Live", 15, Design.Text); brand.Font = new Font("Segoe UI Semibold", 15); brand.SetBounds(82, 18, 165, 28);
            Label product = Design.Label("Game Link", 10, Design.Muted); product.SetBounds(83, 47, 150, 24);
            Label purpose = Design.Label("Conecta tu chat con tus juegos", 11, Design.Muted); purpose.SetBounds(268, 32, 400, 26);
            var dashboard = new Button(); Design.Button(dashboard, "Abrir Cloud90", false); dashboard.SetBounds(922, 24, 172, 40); dashboard.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            dashboard.Click += delegate { try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://cloud90.dev/juegos") { UseShellExecute = true }); } catch (Exception e) { MessageBox.Show(this, "No se pudo abrir Cloud90: " + e.Message, Text); } };
            header.Controls.AddRange(new Control[] { logo, brand, product, purpose, dashboard });
            shell.Controls.Add(header, 0, 0); shell.SetColumnSpan(header, 2);

            var sidebar = new Panel { Dock = DockStyle.Fill, BackColor = Design.Surface, Margin = new Padding(0, 0, 0, 0), Padding = new Padding(12, 14, 12, 12) };
            Label libraryTitle = Design.Label("Tu biblioteca", 12, Design.Text); libraryTitle.Font = new Font("Segoe UI Semibold", 12); libraryTitle.Dock = DockStyle.Top; libraryTitle.Height = 36;
            var library = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = Padding.Empty };
            var footer = new Panel { Dock = DockStyle.Bottom, Height = 75 };
            Label help = Design.Label("Cada juego usa su propio token.\nConfigura los regalos en Cloud90.", 9, Design.Muted); help.Dock = DockStyle.Top; help.Height = 44;
            Label version = Design.Label("Game Link " + Link.Version.Replace("cloud90-game-link-", ""), 9, Design.Muted); version.Dock = DockStyle.Bottom; version.Height = 22;
            footer.Controls.AddRange(new Control[] { help, version });
            sidebar.Controls.Add(library); sidebar.Controls.Add(libraryTitle); sidebar.Controls.Add(footer);
            shell.Controls.Add(sidebar, 0, 1);

            var right = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty, Padding = Padding.Empty };
            workspace.Dock = DockStyle.Fill; workspace.BackColor = Design.Background;
            activity.Dock = DockStyle.Bottom; activity.Height = 48; activity.BackColor = Design.Surface; activity.Padding = new Padding(24, 0, 24, 0);
            var activityHeader = new Panel { Size = new Size(828, 44), Dock = DockStyle.Top, Height = 44 };
            Label activityLabel = Design.Label("Actividad de Game Link", 10, Design.Muted); activityLabel.SetBounds(0, 12, 300, 24);
            Design.Button(activityToggle, "Mostrar registro", false); activityToggle.SetBounds(660, 5, 168, 34); activityToggle.Anchor = AnchorStyles.Top | AnchorStyles.Right; activityToggle.FlatAppearance.BorderSize = 0; activityToggle.BackColor = Design.Surface;
            activityToggle.Click += delegate {
                log.Visible = !log.Visible;
                activity.Height = log.Visible ? 180 : 48;
                activityToggle.Text = log.Visible ? "Ocultar registro" : "Mostrar registro";
                activityToggle.AccessibleName = activityToggle.Text;
            };
            activityHeader.Controls.AddRange(new Control[] { activityLabel, activityToggle });
            log.Multiline = true; log.ReadOnly = true; log.ScrollBars = ScrollBars.Vertical; log.WordWrap = false;
            log.BackColor = Design.Surface; log.ForeColor = Design.Muted; log.BorderStyle = BorderStyle.None; log.Font = new Font("Consolas", 9f);
            log.Dock = DockStyle.Fill; log.Visible = false; log.AccessibleName = "Registro de conexiones y efectos";
            activity.Controls.Add(log); activity.Controls.Add(activityHeader);
            right.Controls.Add(workspace); right.Controls.Add(activity); shell.Controls.Add(right, 1, 1);
            panels = new GamePanel[]
            {
                new GamePanel(this, new HadesHost()),
                new GamePanel(this, new MameHost("kof2002", "neogeo", "KOF 2002")),
                new GamePanel(this, new MameHost("mslug3", "neogeo", "Metal Slug 3")),
                new GamePanel(this, new CtrHost()),
                new GamePanel(this, new CupheadHost()),
                new GamePanel(this, new GtaSaHost()),
            };
            navigation = new GameNavigationButton[panels.Length];
            for (int i = 0; i < panels.Length; i++)
            {
                int index = i;
                panels[i].Dock = DockStyle.Fill;
                panels[i].Visible = false;
                workspace.Controls.Add(panels[i]);
                navigation[i] = new GameNavigationButton(panels[i].GameId, panels[i].GameId == "kof2002" ? "KOF 2002" : panels[i].GameId == "ctrnf" ? "CTR Nitro-Fueled" : panels[i].TabTitle, panels[i].TabTitle.Contains("MAME") ? "Arcade / MAME" : "PC / Windows") { Width = 216, Height = 78, Margin = new Padding(0, 0, 0, 6) };
                navigation[i].Click += delegate { SelectGame(index); };
                navigation[i].KeyDown += delegate(object sender, KeyEventArgs e) {
                    if (e.KeyCode != Keys.Up && e.KeyCode != Keys.Down) return;
                    int next = (index + (e.KeyCode == Keys.Down ? 1 : panels.Length - 1)) % panels.Length;
                    SelectGame(next); navigation[next].Focus(); e.Handled = true;
                };
                library.Controls.Add(navigation[i]);
            }
            int selected = Array.FindIndex(panels, delegate(GamePanel p) { return p.GameId == Settings.SelectedGame; });
            SelectGame(selected < 0 ? panels.Length - 1 : selected);
            Log.Line += delegate (string line) { OnUi(delegate { AppendLog(line); }); };
            Load += delegate
            {
                Log.Reset();
                Log.Write("Cloud90 Game Link " + Link.Version);
                foreach (GamePanel p in panels) p.Startup();
                loaded = true;
            };
            FormClosed += delegate { foreach (GamePanel p in panels) p.Stop(); };
        }

        void SelectGame(int index)
        {
            SuspendLayout();
            for (int i = 0; i < panels.Length; i++) { panels[i].Visible = i == index; navigation[i].Selected = i == index; }
            panels[index].BringToFront();
            ResumeLayout();
            Settings.SelectedGame = panels[index].GameId;
            if (loaded) Settings.Save();
        }

        public static void Style(Button b, string text, int x, int y)
        {
            Design.Button(b, text, false);
            b.SetBounds(x, y, 124, 30);
        }

        public void OnUi(Action action) { ui.Post(delegate { if (!IsDisposed && !Disposing) action(); }, null); }

        void AppendLog(string line)
        {
            if (log.TextLength > 60000) log.Text = log.Text.Substring(log.TextLength - 30000);
            log.AppendText(line + Environment.NewLine);
        }
    }

    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            bool first;
            using (Mutex single = new Mutex(true, "Cloud90GameLink", out first))
            {
                if (!first)
                {
                    MessageBox.Show("Cloud90 Game Link ya está abierto.", "Cloud90 Game Link");
                    return;
                }
                // TLS 1.2 at least, whatever the machine's .NET defaults are.
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm());
            }
        }
    }
}
