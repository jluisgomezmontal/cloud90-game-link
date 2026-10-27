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

        public string TabTitle { get { return host.Title + (host is MameHost ? " (MAME)" : ""); } }

        public GamePanel(MainForm form, GameHost host)
        {
            this.form = form;
            this.host = host;
            BackColor = Color.FromArgb(18, 18, 26);
            link = new Link(delegate { return form.Settings; }, host.Id, host.Title);
            link.Status += delegate (string text) { form.OnUi(delegate { cloudStatus.Text = "Cloud90: " + text; }); };

            cloudStatus.SetBounds(8, 8, 520, 22);
            gameStatus.SetBounds(8, 32, 520, 36);
            MainForm.Style(tokenButton, "Pegar token", 8, 72);
            MainForm.Style(playButton, "Abrir " + host.Title, 138, 72);
            MainForm.Style(pauseButton, "Pausar efectos", 268, 72);
            MainForm.Style(folderButton, host.FolderButton, 398, 72);
            MainForm.Style(uninstallButton, "Desinstalar", 398, 108);
            Controls.AddRange(new Control[] { cloudStatus, gameStatus, tokenButton, playButton, pauseButton, folderButton, uninstallButton });
            if (host is HadesHost)
            {
                CheckBox vulkan = new CheckBox();
                vulkan.Text = "Usar Vulkan";
                vulkan.SetBounds(8, 112, 140, 22);
                vulkan.CheckedChanged += delegate { ((HadesHost)host).Vulkan = vulkan.Checked; };
                Controls.Add(vulkan);
            }

            tokenButton.Click += delegate { PasteToken(); };
            playButton.Click += delegate { Play(); };
            pauseButton.Click += delegate { link.SetPaused(!link.Paused); pauseButton.Text = link.Paused ? "Reanudar efectos" : "Pausar efectos"; };
            folderButton.Click += delegate { PickFolder(); };
            uninstallButton.Click += delegate { Uninstall(); };
        }

        public void Startup()
        {
            host.FindFolder(form.Settings);
            form.Settings.Save();
            Prepare();
            cloudStatus.Text = "Cloud90: " + (form.Settings.TokenFor(host.Id).Length > 0 ? "conectando…" : "sin token");
            link.Start();
        }

        public void Stop() { link.Stop(); }

        void Prepare()
        {
            string problem = host.Problem(form.Settings);
            if (problem.Length > 0)
            {
                gameStatus.Text = host.Title + ": " + problem;
                playButton.Enabled = false;
                return;
            }
            try
            {
                link.UseInbox(host.Prepare(form.Settings));
                gameStatus.Text = host.Title + ": listo (" + host.Folder(form.Settings) + ")";
                playButton.Enabled = !host.Running;
            }
            catch (Exception e)
            {
                gameStatus.Text = host.Title + ": no se pudo preparar (" + e.Message + ")";
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
                    form.OnUi(delegate { gameStatus.Text = host.Title + ": cerrado"; playButton.Enabled = true; });
                });
                gameStatus.Text = host.Title + ": abierto";
                playButton.Enabled = false;
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
        readonly TabControl tabs = new TabControl();
        readonly TextBox log = new TextBox();
        readonly GamePanel[] panels;

        public MainForm()
        {
            ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
            Text = "Cloud90 Game Link";
            ClientSize = new Size(580, 470);
            MinimumSize = new Size(520, 420);
            Font = new Font("Segoe UI", 9.5f);
            BackColor = Color.FromArgb(18, 18, 26);
            ForeColor = Color.Gainsboro;

            Label title = new Label();
            title.Text = "Cloud90 Game Link";
            title.Font = new Font("Segoe UI Semibold", 14f);
            title.ForeColor = Color.White;
            title.SetBounds(16, 10, 540, 30);

            tabs.SetBounds(16, 44, 548, 180);
            tabs.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            panels = new GamePanel[]
            {
                new GamePanel(this, new HadesHost()),
                new GamePanel(this, new MameHost("kof2002", "neogeo", "KOF 2002")),
                new GamePanel(this, new MameHost("mslug3", "neogeo", "Metal Slug 3")),
                new GamePanel(this, new CtrHost()),
                new GamePanel(this, new CupheadHost()),
            };
            for (int i = 0; i < panels.Length; i++)
            {
                TabPage page = new TabPage(panels[i].TabTitle);
                page.BackColor = Color.FromArgb(18, 18, 26);
                panels[i].Dock = DockStyle.Fill;
                page.Controls.Add(panels[i]);
                tabs.TabPages.Add(page);
            }

            log.Multiline = true;
            log.ReadOnly = true;
            log.ScrollBars = ScrollBars.Vertical;
            log.BackColor = Color.FromArgb(10, 10, 16);
            log.ForeColor = Color.Silver;
            log.BorderStyle = BorderStyle.None;
            log.Font = new Font("Consolas", 9f);
            log.SetBounds(16, 232, 548, 226);
            log.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;

            Controls.AddRange(new Control[] { title, tabs, log });
            Log.Line += delegate (string line) { OnUi(delegate { AppendLog(line); }); };
            Load += delegate
            {
                Log.Reset();
                Log.Write("Cloud90 Game Link " + Link.Version);
                foreach (GamePanel p in panels) p.Startup();
            };
            FormClosed += delegate { foreach (GamePanel p in panels) p.Stop(); };
        }

        public static void Style(Button b, string text, int x, int y)
        {
            b.Text = text;
            b.SetBounds(x, y, 124, 30);
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderColor = Color.FromArgb(60, 60, 80);
            b.BackColor = Color.FromArgb(30, 30, 44);
            b.ForeColor = Color.White;
        }

        public void OnUi(Action action) { ui.Post(delegate { action(); }, null); }

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
