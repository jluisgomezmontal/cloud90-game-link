// Cloud90 Hades Link: the window. Paste the token, open Hades through the
// Link (so it can read the mod's answers), pause effects (panic), and
// uninstall the mod.
using System;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace Cloud90.HadesLink
{
    internal sealed class MainForm : Form
    {
        Settings settings = Settings.Load();
        readonly Link link;
        readonly HadesProcess game = new HadesProcess();
        readonly SynchronizationContext ui;

        readonly Label cloudStatus = new Label();
        readonly Label hadesStatus = new Label();
        readonly Button tokenButton = new Button();
        readonly Button playButton = new Button();
        readonly Button pauseButton = new Button();
        readonly Button folderButton = new Button();
        readonly Button uninstallButton = new Button();
        readonly CheckBox vulkan = new CheckBox();
        readonly TextBox log = new TextBox();

        public MainForm()
        {
            ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
            Text = "Cloud90 Hades Link";
            ClientSize = new Size(560, 420);
            MinimumSize = new Size(480, 360);
            Font = new Font("Segoe UI", 9.5f);
            BackColor = Color.FromArgb(18, 18, 26);
            ForeColor = Color.Gainsboro;

            Label title = new Label();
            title.Text = "Cloud90 Hades Link";
            title.Font = new Font("Segoe UI Semibold", 14f);
            title.ForeColor = Color.White;
            title.SetBounds(16, 12, 520, 30);

            cloudStatus.SetBounds(16, 48, 528, 22);
            hadesStatus.SetBounds(16, 72, 528, 22);

            Style(tokenButton, "Pegar token", 16, 104);
            Style(playButton, "Abrir Hades", 146, 104);
            Style(pauseButton, "Pausar efectos", 276, 104);
            Style(folderButton, "Carpeta de Hades…", 406, 104);
            vulkan.Text = "Usar Vulkan";
            vulkan.SetBounds(16, 146, 140, 22);
            Style(uninstallButton, "Desinstalar mod", 406, 142);

            log.Multiline = true;
            log.ReadOnly = true;
            log.ScrollBars = ScrollBars.Vertical;
            log.BackColor = Color.FromArgb(10, 10, 16);
            log.ForeColor = Color.Silver;
            log.BorderStyle = BorderStyle.None;
            log.Font = new Font("Consolas", 9f);
            log.SetBounds(16, 180, 528, 224);
            log.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;

            Controls.AddRange(new Control[] { title, cloudStatus, hadesStatus, tokenButton, playButton, pauseButton, folderButton, vulkan, uninstallButton, log });

            link = new Link(delegate { return settings; });
            link.Status += delegate (string text) { OnUi(delegate { cloudStatus.Text = "Cloud90: " + text; }); };
            Log.Line += delegate (string line) { OnUi(delegate { AppendLog(line); }); };
            game.ModLine += link.OnModLine;
            game.Exited += delegate { link.OnGameExited(); OnUi(delegate { hadesStatus.Text = "Hades: cerrado"; playButton.Enabled = true; }); };

            tokenButton.Click += delegate { PasteToken(); };
            playButton.Click += delegate { Play(); };
            pauseButton.Click += delegate { link.SetPaused(!link.Paused); pauseButton.Text = link.Paused ? "Reanudar efectos" : "Pausar efectos"; };
            folderButton.Click += delegate { PickFolder(); };
            uninstallButton.Click += delegate { Uninstall(); };

            Load += delegate { Startup(); };
        }

        static void Style(Button b, string text, int x, int y)
        {
            b.Text = text;
            b.SetBounds(x, y, 124, 30);
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderColor = Color.FromArgb(60, 60, 80);
            b.BackColor = Color.FromArgb(30, 30, 44);
            b.ForeColor = Color.White;
        }

        void OnUi(Action action) { ui.Post(delegate { action(); }, null); }

        void AppendLog(string line)
        {
            if (log.TextLength > 60000) log.Text = log.Text.Substring(log.TextLength - 30000);
            log.AppendText(line + Environment.NewLine);
        }

        void Startup()
        {
            Log.Reset();
            Log.Write("Cloud90 Hades Link " + Link.Version);
            if (!HadesFolder.IsHades(settings.HadesDir))
            {
                settings.HadesDir = HadesFolder.Find();
                if (settings.HadesDir.Length > 0) settings.Save();
            }
            PrepareHades();
            cloudStatus.Text = "Cloud90: " + (settings.Token.Length > 0 ? "conectando…" : "sin token");
            link.Start();
        }

        void PrepareHades()
        {
            if (!HadesFolder.IsHades(settings.HadesDir))
            {
                hadesStatus.Text = "Hades: no lo encontré — elige su carpeta con «Carpeta de Hades…»";
                playButton.Enabled = false;
                return;
            }
            try
            {
                ModInstaller.Install(settings.HadesDir);
                link.UseInbox(new Inbox(ModInstaller.Inbox(settings.HadesDir)));
                hadesStatus.Text = "Hades: listo en " + settings.HadesDir;
                playButton.Enabled = !game.Running;
            }
            catch (Exception e)
            {
                hadesStatus.Text = "Hades: no se pudo instalar el mod (" + e.Message + ")";
                playButton.Enabled = false;
            }
        }

        void PasteToken()
        {
            string text = Clipboard.ContainsText() ? Clipboard.GetText().Trim() : "";
            if (!text.StartsWith("c90gl_"))
            {
                MessageBox.Show(this, "Copia primero tu token de Cloud90 (Juegos › Hades › Generar token).", Text);
                return;
            }
            settings.Token = text;
            settings.Save();
            Log.Write("token guardado");
            link.Start();
        }

        void Play()
        {
            if (game.Running) return;
            if (HadesProcess.AnotherRunning())
            {
                MessageBox.Show(this, "Hades ya está abierto. Ciérralo y ábrelo desde aquí: así el Link puede leer lo que responde el juego.", Text);
                return;
            }
            try
            {
                PrepareHades();
                game.Start(settings.HadesDir, vulkan.Checked);
                hadesStatus.Text = "Hades: abierto";
                playButton.Enabled = false;
            }
            catch (Exception e)
            {
                MessageBox.Show(this, "No se pudo abrir Hades: " + e.Message, Text);
            }
        }

        void PickFolder()
        {
            using (FolderBrowserDialog d = new FolderBrowserDialog())
            {
                d.Description = "La carpeta de Hades (la que tiene Content y x64)";
                if (d.ShowDialog(this) != DialogResult.OK) return;
                if (!HadesFolder.IsHades(d.SelectedPath))
                {
                    MessageBox.Show(this, "Esa carpeta no tiene Hades (busca la que contiene Content\\Scripts y x64\\Hades.exe).", Text);
                    return;
                }
                settings.HadesDir = d.SelectedPath;
                settings.Save();
                PrepareHades();
            }
        }

        void Uninstall()
        {
            if (!HadesFolder.IsHades(settings.HadesDir)) return;
            if (game.Running)
            {
                MessageBox.Show(this, "Cierra Hades primero.", Text);
                return;
            }
            if (MessageBox.Show(this, "¿Quitar el mod de Cloud90 de Hades? El juego queda como estaba.", Text, MessageBoxButtons.YesNo) != DialogResult.Yes) return;
            try
            {
                ModInstaller.Uninstall(settings.HadesDir);
                hadesStatus.Text = "Hades: mod desinstalado (se instala otra vez al abrir el Link)";
                playButton.Enabled = false;
            }
            catch (Exception e)
            {
                MessageBox.Show(this, "No se pudo desinstalar: " + e.Message, Text);
            }
        }
    }

    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            bool first;
            using (Mutex single = new Mutex(true, "Cloud90HadesLink", out first))
            {
                if (!first)
                {
                    MessageBox.Show("Cloud90 Hades Link ya está abierto.", "Cloud90 Hades Link");
                    return;
                }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm());
            }
        }
    }
}
