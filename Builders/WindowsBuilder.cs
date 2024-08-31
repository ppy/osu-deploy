// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.IO;
using osu.Desktop.Deploy.Uploaders;

namespace osu.Desktop.Deploy.Builders
{
    public class WindowsBuilder : Builder
    {
        private const string app_name = "osu!.exe";
        private const string os_name = "win";
        private const string channel = "win";

        private readonly string? codeSigningPassword;

        public WindowsBuilder(string version, string? codeSigningPassword)
            : base(version)
        {
            if (!string.IsNullOrEmpty(Program.WindowsCodeSigningCertPath))
                this.codeSigningPassword = codeSigningPassword ?? Program.ReadLineMasked("Enter code signing password: ");
        }

        protected override string TargetFramework => "net8.0";
        protected override string RuntimeIdentifier => $"{os_name}-x64";

        public override Uploader CreateUploader()
        {
            string extraArgs = $" --splashImage=\"{SplashImagePath}\""
                               + $" --icon=\"{IconPath}\""
                               + $" --noPortable";

            if (!string.IsNullOrEmpty(Program.WindowsCodeSigningCertPath))
                extraArgs += $" --signParams=\"/td sha256 /fd sha256 /f {Program.WindowsCodeSigningCertPath} /p {codeSigningPassword} /tr http://timestamp.comodoca.com\"";

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
