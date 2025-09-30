// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Linq;

namespace osu.Desktop.Deploy.Uploaders
{
    public class WindowsVelopackUploader : VelopackUploader
    {
        private readonly string channel;

        public WindowsVelopackUploader(string applicationName, string operatingSystemName, string runtimeIdentifier, string channel, string? extraArgs = null, string? stagingPath = null)
            : base(applicationName, operatingSystemName, runtimeIdentifier, channel, extraArgs, stagingPath)
        {
            this.channel = channel;
        }

        protected override string PackTitle => "osu!(lazer)";

        public override void PublishBuild(string version)
        {
            base.PublishBuild(version);

            if (channel != "win-x64")
                throw new Exception($"Unrecognised channel: {channel}");

            string suffix = channel.Split('-').Last();

            RenameAsset($"{Program.PackageName}-{channel}-Setup.exe", $"install-{suffix}.exe");
        }
    }
}
