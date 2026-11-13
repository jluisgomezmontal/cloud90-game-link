using System.IO;

namespace Cloud90.GameLink
{
    internal sealed class WukongHost : SteamModHost
    {
        const string Package = "Cloud90.Wukong.dll";
        internal static bool Packaged { get { return HasPackage(Package); } }
        protected override string Plugin { get { return Package; } }
        protected override string SteamAppId { get { return "2358720"; } }
        protected override string ProcessName { get { return "b1-Win64-Shipping"; } }
        protected override string BridgeVersion { get { return "cloud90-wukong-1"; } }
        protected override string Executable(string path) { return Path.Combine(path, @"b1\Binaries\Win64\b1-Win64-Shipping.exe"); }
        protected override string PluginFolder(string root)
        {
            string folder = CupheadInstaller.SafePath(root, @"b1\Binaries\Win64\CSharpLoader\Mods\Cloud90.Wukong");
            Directory.CreateDirectory(folder);
            return folder;
        }
        public override string Id { get { return "wukong"; } }
        public override string Title { get { return "Black Myth: Wukong"; } }
        public override string FolderButton { get { return "Carpeta de Wukong…"; } }
        public override string FolderPrompt { get { return "Carpeta de Black Myth: Wukong de Steam (contiene b1)"; } }
        public override string Folder(Settings settings) { return settings.WukongDir; }
        protected override void SetFolder(Settings settings, string path) { settings.WukongDir = path; }
        public override string Problem(Settings settings)
        {
            string root = Folder(settings);
            if (!IsGame(root)) return "Selecciona la carpeta de Steam que contiene b1.";
            string bin = Path.GetDirectoryName(Executable(root));
            if (!Directory.Exists(Path.Combine(bin, "CSharpLoader")))
                return "Instala un B1CSharpLoader compatible en la carpeta de Wukong.";
            return "";
        }
    }
}
