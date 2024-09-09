// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.IO;
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
                // TODO: resolve from .nuget (or just include locally...)
                string dlibPath = "Azure.CodeSigning.Dlib.dll";

                extraArgs += $" --signParams=\"/td sha256 /fd sha256 /dlib {dlibPath} /dmdf {Program.WindowsCodeSigningMetadataPath} /tr http://timestamp.acs.microsoft.com\"";
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
