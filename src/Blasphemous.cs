using System.IO;

namespace Cloud90.GameLink
{
    internal sealed class BlasphemousHost : SteamModHost
    {
        const string Package = "Cloud90.Blasphemous.dll";
        internal static bool Packaged { get { return HasPackage(Package); } }
        protected override string Plugin { get { return Package; } }
        protected override string SteamAppId { get { return "774361"; } }
        protected override string ProcessName { get { return "Blasphemous"; } }
        protected override string BridgeVersion { get { return "cloud90-blasphemous-1"; } }
        protected override bool ReportsPause { get { return true; } }
        protected override string PluginFolder(string root) {
            string folder = CupheadInstaller.SafePath(root, @"BepInEx\plugins");
            Directory.CreateDirectory(folder);
            return folder;
        }
        public override string Id { get { return "blasphemous"; } }
        public override string Title { get { return "Blasphemous"; } }
        public override string FolderButton { get { return "Carpeta de Blasphemous…"; } }
        public override string FolderPrompt { get { return "Blasphemous 1 de Steam (Blasphemous.exe)"; } }
        public override string Folder(Settings settings) { return settings.BlasphemousDir; }
        protected override void SetFolder(Settings settings, string path) { settings.BlasphemousDir = path; }
        public override string Problem(Settings settings) {
            string root = Folder(settings);
            if (!IsGame(root)) return "Selecciona la carpeta con Blasphemous.exe.";
            string assembly = Path.Combine(root, @"Blasphemous_Data\Managed\Assembly-CSharp.dll");
            if (!File.Exists(assembly) || CupheadInstaller.HashFile(assembly) != "f12c2f1d40aef47287f07e910a16904deeb11bb2d007331f30e50327c128a4ce")
                return "La vista previa requiere Blasphemous de Steam build 20206233.";
            if (!File.Exists(Path.Combine(root, @"BepInEx\core\BepInEx.dll")) || !File.Exists(Path.Combine(root, "winhttp.dll")))
                return "Instala BepInEx 5.4.23.4 para Windows x64 en la carpeta de Blasphemous.";
            return "";
        }
    }
}
