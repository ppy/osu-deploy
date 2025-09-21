// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.IO;
using System.Linq;
using osu.Desktop.Deploy.Uploaders;

namespace osu.Desktop.Deploy.Builders
{
    public class WindowsBuilder : Builder
    {
        private const string app_name = "osu!.exe";
        private const string os_name = "win";
        private readonly string channel = "win";

        public WindowsBuilder(string version, string? arch)
            : base(version)
        {
            if (string.IsNullOrEmpty(arch))
            {
                Console.Write("Build for which architecture? [x64/arm64]: ");
                arch = Console.ReadLine() ?? string.Empty;
            }

            if (arch != "x64" && arch != "arm64")
                Logger.Error($"Invalid Architecture: {arch}");

            RuntimeIdentifier = $"{os_name}-{arch}";

            // Include architecture in channel while x64 keeps using the old convention without it
            if (arch != "x64")
                this.channel = RuntimeIdentifier;
        }

        protected override string TargetFramework => "net8.0";
        protected override string RuntimeIdentifier { get; }

        public override Uploader CreateUploader()
        {
            string installIcon = Path.Combine(Environment.CurrentDirectory, "install.ico");
            string extraArgs = $" --splashImage=\"{SplashImagePath}\""
                               + $" --icon=\"{installIcon}\""
                               + $" --noPortable";

            if (!string.IsNullOrEmpty(Program.WindowsCodeSigningMetadataPath))
            {
                string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages", "microsoft.trusted.signing.client");
                const string dll_name = "Azure.CodeSigning.Dlib.dll";

                string? dllPath = Directory.GetFiles(path, dll_name, SearchOption.AllDirectories).LastOrDefault(p => p.Contains("x64"));

                if (dllPath == null)
                    Logger.Error("Could not find path for Dlib.dll");

                // We're using `signTemplate` here as we need to prefer the system `signtool.exe` in order for it to
                // run azure code signing correctly on older windows versions.
                //
                // This can be changed back to `signParams` if velopack changes the signtool logic to fix this.
                string signToolPath = Directory.GetDirectories(@"C:\Program Files (x86)\Windows Kits\10\bin", "*", SearchOption.AllDirectories)
                                               .Select(dir => Path.Combine(dir, @"x64\signtool.exe"))
                                               .Where(File.Exists)
                                               .Last();

                extraArgs +=
                    $" --signTemplate=\"\\\"{signToolPath}\\\" sign /td sha256 /fd sha256 /dlib \\\"{dllPath}\\\" /dmdf \\\"{Path.GetFullPath(Program.WindowsCodeSigningMetadataPath)}\\\" /tr http://timestamp.acs.microsoft.com {{{{file...}}}}";
            }

            return new WindowsVelopackUploader(app_name, os_name, RuntimeIdentifier, channel, extraArgs: extraArgs);
        }

        public override void Build()
        {
            RunDotnetPublish();
            AttachSatoriGC();

            bool rcEditCommand =
                Program.RunCommand("tools/rcedit-x64.exe", $"\"{Path.Combine(Program.StagingPath, "osu!.exe")}\""
                                                           + $" --set-icon \"{IconPath}\"",
                    exitOnFail: false);

            if (!rcEditCommand)
            {
                // Retry again with wine
                // TODO: Should probably change this to use RuntimeInfo.OS checks instead of fail values
                bool wineRcEditCommand =
                    Program.RunCommand("wine", $"\"{Path.GetFullPath("tools/rcedit-x64.exe")}\""
                                               + $" \"{Path.Combine(Program.StagingPath, "osu!.exe")}\""
                                               + $" --set-icon \"{IconPath}\"",
                        exitOnFail: false);

                if (!wineRcEditCommand)
                    Logger.Error("Failed to set icon on osu!.exe");
            }
        }
    }
}
