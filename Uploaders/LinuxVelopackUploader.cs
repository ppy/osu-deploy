// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;

namespace osu.Desktop.Deploy.Uploaders
{
    public class LinuxVelopackUploader : VelopackUploader
    {
        private readonly string channel;

        public LinuxVelopackUploader(string applicationName, string operatingSystemName, string runtimeIdentifier, string channel, string? extraArgs = null, string? stagingPath = null)
            : base(applicationName, operatingSystemName, runtimeIdentifier, channel, extraArgs, stagingPath)
        {
            this.channel = channel;
        }

        public override void PublishBuild(string version)
        {
            base.PublishBuild(version);

            if (channel != "linux-x64" && channel != "linux-arm64")
                throw new Exception($"Unrecognised channel: {channel}");

            // Include architecture in asset name while x64 keeps using the old convention without it
            string arch_suffix = "";
            if (channel != "linux-x64")
                arch_suffix = channel.Substring(5);

            RenameAsset($"{Program.PackageName}-{channel}.AppImage", $"osu{arch_suffix}.AppImage");
        }
    }
}
