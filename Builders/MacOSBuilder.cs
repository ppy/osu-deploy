// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using osu.Desktop.Deploy.Uploaders;

namespace osu.Desktop.Deploy.Builders
{
    public class MacOSBuilder : Builder
    {
        private const string app_name = "osu!";
        private const string os_name = "mac";

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
        }

        protected override string TargetFramework => "net8.0";
        protected override string RuntimeIdentifier { get; }

        public override Uploader CreateUploader()
        {
            string extraArgs = $" --icon=\"{IconPath}\"";

            if (!string.IsNullOrEmpty(Program.AppleCodeSignCertName))
                extraArgs += $" --signAppIdentity=\"{Program.AppleCodeSignCertName}\"";
            if (!string.IsNullOrEmpty(Program.AppleInstallSignCertName))
                extraArgs += $" --signInstallIdentity=\"{Program.AppleInstallSignCertName}\"";
            if (!string.IsNullOrEmpty(Program.AppleNotaryProfileName))
                extraArgs += $" --notaryProfile=\"{Program.AppleNotaryProfileName}\"";

            return new VelopackUploader(app_name, os_name, RuntimeIdentifier, RuntimeIdentifier, extraArgs);
        }

        public override void Build() => RunDotnetPublish();
    }
}
