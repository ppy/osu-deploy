// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;

namespace osu.Desktop.Deploy.Uploaders
{
    public class MacOSVelopackUploader : VelopackUploader
    {
        private readonly string channel;

        public MacOSVelopackUploader(string applicationName, string operatingSystemName, string runtimeIdentifier, string channel, string? extraArgs = null, string? stagingPath = null)
            : base(applicationName, operatingSystemName, runtimeIdentifier, channel, extraArgs, stagingPath)
        {
            this.channel = channel;
        }

        public override void PublishBuild(string version)
        {
            base.PublishBuild(version);

            string suffix;

            switch (channel)
            {
                case "osx-arm64":
                    suffix = "Apple.Silicon";
                    break;

                case "osx-x64":
                    suffix = "Intel";
                    break;

                default:
                    throw new Exception($"Unrecognised channel: {channel}");
            }

            RenameAsset($"{Program.PackageName}-{channel}-Portable.zip", $"osu.app.{suffix}.zip");
        }
    }
}
