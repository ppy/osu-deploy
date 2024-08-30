// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.IO;
using osu.Desktop.Deploy.Uploaders;

namespace osu.Desktop.Deploy.Builders
{
    public class MacOSBuilder : Builder
    {
        private const string app_dir = "osu!.app";
        private const string app_name = "osu!";
        private const string os_name = "osx";

        private readonly string stagingTarget;
        private readonly string publishTarget;

        public MacOSBuilder(string version, string? arch)
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

            stagingTarget = Path.Combine(Program.StagingPath, app_dir);
            publishTarget = Path.Combine(stagingTarget, "Contents", "MacOS");
        }

        protected override string TargetFramework => "net8.0";
        protected override string RuntimeIdentifier { get; }

        public override Uploader CreateUploader()
        {
            string extraArgs = $" --signEntitlements=\"{Path.Combine(Environment.CurrentDirectory, "osu.entitlements")}\""
                               + $" --noInst";

            if (!string.IsNullOrEmpty(Program.AppleCodeSignCertName))
                extraArgs += $" --signAppIdentity=\"{Program.AppleCodeSignCertName}\"";
            if (!string.IsNullOrEmpty(Program.AppleInstallSignCertName))
                extraArgs += $" --signInstallIdentity=\"{Program.AppleInstallSignCertName}\"";
            if (!string.IsNullOrEmpty(Program.AppleNotaryProfileName))
                extraArgs += $" --notaryProfile=\"{Program.AppleNotaryProfileName}\"";
            if (!string.IsNullOrEmpty(Program.AppleKeyChainPath))
                extraArgs += $" --keychain=\"{Program.AppleKeyChainPath}\"";

            return new VelopackUploader(app_name, os_name, RuntimeIdentifier, RuntimeIdentifier, extraArgs: extraArgs, stagingPath: stagingTarget);
        }

        public override void Build()
        {
            if (Directory.Exists(stagingTarget))
                Directory.Delete(stagingTarget, true);

            Program.RunCommand("cp", $"-r \"{Path.Combine(Program.TemplatesPath, app_dir)}\" \"{stagingTarget}\"");

            RunDotnetPublish(outputDir: publishTarget);

            // without touching the app bundle itself, changes to file associations / icons / etc. will be cached at a macOS level and not updated.
            Program.RunCommand("touch", $"\"{stagingTarget}\" {Program.StagingPath}", false);
        }
    }
}
