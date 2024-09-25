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
        private const string channel = "win";

        public WindowsBuilder(string version)
            : base(version)
        {
        }

        protected override string TargetFramework => "net8.0";
        protected override string RuntimeIdentifier => $"{os_name}-x64";

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

                string signToolPath = Directory.GetDirectories(@"C:\Program Files (x86)\Windows Kits\10\bin", "*", SearchOption.AllDirectories)
                                               .Select(dir => Path.Combine(dir, @"x64\signtool.exe"))
                                               .Where(File.Exists)
                                               .Last();

                extraArgs += $" --signTemplate=\"\\\"{signToolPath}\\\" sign /td sha256 /fd sha256 /dlib \\\"{dllPath}\\\" /dmdf \\\"{Path.GetFullPath(Program.WindowsCodeSigningMetadataPath)}\\\" /tr http://timestamp.acs.microsoft.com {{{{file...}}}}";
            }

            return new WindowsVelopackUploader(app_name, os_name, RuntimeIdentifier, channel, extraArgs: extraArgs);
        }

        public override void Build()
        {
            RunDotnetPublish();

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
