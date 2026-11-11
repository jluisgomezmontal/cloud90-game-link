using System.Diagnostics;
using System.IO;

namespace Cloud90.GameLink
{
    internal sealed class Rdr2Host : SteamModHost
    {
        const string Package = "Cloud90.RDR2.Diagnostics.asi";
        internal static bool Packaged { get { return HasPackage(Package); } }
        protected override string Plugin { get { return Package; } }
        protected override string SteamAppId { get { return "1174180"; } }
        protected override string ProcessName { get { return "RDR2"; } }
        protected override string BridgeVersion { get { return "cloud90-rdr2-1"; } }
        public override string Id { get { return "rdr2"; } }
        public override string Title { get { return "Red Dead Redemption 2"; } }
        public override string FolderButton { get { return "Carpeta de RDR2…"; } }
        public override string FolderPrompt { get { return "Red Dead Redemption 2 de Steam (RDR2.exe)"; } }
        public override string Folder(Settings settings) { return settings.Rdr2Dir; }
        protected override void SetFolder(Settings settings, string path) { settings.Rdr2Dir = path; }
        public override string Problem(Settings settings)
        {
            string root = Folder(settings);
            if (!IsGame(root)) return "Selecciona la carpeta de Steam que contiene RDR2.exe.";
            var version = FileVersionInfo.GetVersionInfo(Executable(root));
            if (version.FileMajorPart != 1 || version.FileMinorPart != 0 || version.FileBuildPart != 1491 || version.FilePrivatePart != 50)
                return "La vista previa requiere RDR2 de Steam 1.0.1491.50.";
            if (!File.Exists(Path.Combine(root, "ScriptHookRDR2.dll")) || !File.Exists(Path.Combine(root, "dinput8.dll")))
                return "Instala Script Hook RDR2 y su cargador ASI (dinput8.dll) en la carpeta del juego.";
            return "";
        }
    }
}
