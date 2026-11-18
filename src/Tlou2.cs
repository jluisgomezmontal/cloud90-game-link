using System;
using System.IO;
using System.Reflection;

namespace Cloud90.GameLink
{
    internal sealed class Tlou2Host : SteamModHost
    {
        const string Package = "Cloud90.Tlou2.asi";
        internal const string BuildHashResource = "Cloud90.Tlou2.GameSha256";
        readonly Tlou2GiftPipe giftPipe = new Tlou2GiftPipe();

        protected override bool UsesExternalMod(Settings settings)
        {
            return true;
        }

        protected override bool PollExternalMod(int gamePid, string session, Inbox commands, Link link)
        {
            bool ready = giftPipe.Poll(gamePid, session, commands, link);
            Publish(link, giftPipe.Epoch, ready, ready ? giftPipe.Capabilities() : new string[0]);
            Progress(ready ? "Gift Spawner listo para invocar enemigos." : "Esperando una partida disponible en Gift Spawner.");
            return true;
        }

        protected override void CloseExternalMod() { giftPipe.Dispose(); }

        internal static bool Packaged
        {
            get { return HasPackage(BuildHashResource); }
        }

        protected override string Plugin { get { return Package; } }
        protected override string SteamAppId { get { return "2531310"; } }
        protected override string ProcessName { get { return "tlou-ii"; } }
        protected override string BridgeVersion { get { return "cloud90-tlou2-1"; } }
        public override string Id { get { return "tlou2"; } }
        public override bool AllowsAttach { get { return true; } }
        public override string Title { get { return "The Last of Us Part II Remastered"; } }
        public override string FolderButton { get { return "Carpeta de The Last of Us Part II"; } }
        public override string FolderPrompt { get { return "Carpeta que contiene tlou-ii.exe"; } }
        public override string Folder(Settings settings) { return settings.Tlou2Dir; }
        protected override void SetFolder(Settings settings, string path) { settings.Tlou2Dir = path; }

        public override string Problem(Settings settings)
        {
            string folder = Folder(settings);
            if (!IsGame(folder)) return "Selecciona la carpeta que contiene tlou-ii.exe.";
            if (!File.Exists(Path.Combine(folder, "TLOU2GiftSpawner.dll")))
                return "Instala TLOU2 Gift Spawner 1.2.0 en la carpeta del juego.";
            return "";
        }

        public override Inbox Prepare(Settings settings)
        {
            string problem = Problem(settings);
            if (problem.Length != 0) throw new InvalidOperationException(problem);
            string expected;
            using (var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream(BuildHashResource))
            using (var reader = new StreamReader(resource)) expected = reader.ReadToEnd().Trim();
            string actual = CupheadInstaller.HashFile(Executable(Folder(settings)));
            if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Esta compilación de tlou-ii.exe no es compatible con el adaptador de Game Link.");
            return base.Prepare(settings);
        }
    }
}
