// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.IO;
using osu.Desktop.Deploy.Uploaders;

namespace osu.Desktop.Deploy.Builders
{
    public class IOSBuilder : Builder
    {
        public IOSBuilder(string version)
            : base(version)
        {
        }

        protected override string TargetFramework => "net8.0-ios";
        protected override string RuntimeIdentifier => "ios-arm64";

        public override Uploader CreateUploader() => new GitHubUploader();

        public override void Build()
        {
            RunDotnetPublish("-p:ApplicationDisplayVersion=1.0");

            File.Move(Path.Combine(Program.StagingPath, "osu.iOS.ipa"), Path.Combine(Program.ReleasesPath, "osu.iOS.ipa"), true);
        }
    }
}
