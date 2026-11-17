using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace Cloud90.GameLink
{
    internal sealed class Tlou2Host : SteamModHost
    {
        const string Package = "Cloud90.Tlou2.dll";
        internal const string BuildHashResource = "Cloud90.Tlou2.GameSha256";
        string executableHash;

        internal static bool Packaged
        {
            get { return HasPackage(Package) && HasPackage(BuildHashResource); }
        }

        protected override string Plugin { get { return Package; } }
        protected override string SteamAppId { get { return "2531310"; } }
        protected override string ProcessName { get { return "tlou-ii"; } }
        protected override string BridgeVersion { get { return "cloud90-tlou2-1"; } }
        public override string Id { get { return "tlou2"; } }
        public override string Title { get { return "The Last of Us Part II Remastered"; } }
        public override string FolderButton { get { return "Carpeta de The Last of Us Part II"; } }
        public override string FolderPrompt { get { return "Carpeta de Steam que contiene tlou-ii.exe"; } }
        public override string Folder(Settings settings) { return settings.Tlou2Dir; }
        protected override void SetFolder(Settings settings, string path) { settings.Tlou2Dir = path; }

        public override string Problem(Settings settings)
        {
            return IsGame(Folder(settings)) ? "" : "Selecciona la carpeta que contiene tlou-ii.exe.";
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
            executableHash = actual;
            return base.Prepare(settings);
        }

        protected override bool AcceptStatus(Dictionary<string, object> status)
        {
            object hash, mode;
            return status.TryGetValue("gameSha256", out hash)
                && string.Equals(hash as string, executableHash, StringComparison.OrdinalIgnoreCase)
                && status.TryGetValue("mode", out mode)
                && ((string)mode == "campaign" || (string)mode == "no_return" || !(bool)status["inPlay"]);
        }
    }
}
