using System.IO;

namespace Cloud90.GameLink
{
    internal sealed class CelesteHost : SteamModHost
    {
        const string Package = "Cloud90.Celeste.zip";
        internal static bool Packaged { get { return HasPackage(Package); } }
        protected override string Plugin { get { return Package; } }
        protected override string SteamAppId { get { return "504230"; } }
        protected override string ProcessName { get { return "Celeste"; } }
        protected override string BridgeVersion { get { return "cloud90-celeste-1"; } }
        protected override bool ReportsPause { get { return true; } }
        protected override string PluginFolder(string root) { return CupheadInstaller.SafePath(root, "Mods"); }
        public override string Id { get { return "celeste"; } }
        public override string Title { get { return "Celeste"; } }
        public override string FolderButton { get { return "Carpeta de Celeste…"; } }
        public override string FolderPrompt { get { return "Celeste de Steam con Everest"; } }
        public override string Folder(Settings settings) { return settings.CelesteDir; }
        protected override void SetFolder(Settings settings, string path) { settings.CelesteDir = path; }
        public override string Problem(Settings settings)
        {
            string root = Folder(settings);
            if (!IsGame(root)) return "Selecciona la carpeta de Steam con Celeste.exe.";
            if (!File.Exists(Path.Combine(root, "Celeste.dll")) || !File.Exists(Path.Combine(root, "MMHOOK_Celeste.dll")) ||
                !Directory.Exists(Path.Combine(root, "Mods")))
                return "Instala Everest 1.6662.0 o compatible desde Olympus, en esta carpeta de Steam.";
            return "";
        }
    }
}
