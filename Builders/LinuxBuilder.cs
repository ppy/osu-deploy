// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.IO;
using osu.Desktop.Deploy.Uploaders;

namespace osu.Desktop.Deploy.Builders
{
    public class LinuxBuilder : Builder
    {
        private const string app_dir = "osu!.AppDir";
        private const string app_name = "osu!";
        private const string os_name = "linux";

        private readonly string stagingTarget;
        private readonly string publishTarget;

        public LinuxBuilder(string version)
            : base(version)
        {
            stagingTarget = Path.Combine(Program.StagingPath, app_dir);
            publishTarget = Path.Combine(stagingTarget, "usr", "bin");

            RuntimeIdentifier = $"{os_name}-x64";

            if (Program.PhotonRelease)
                RuntimeIdentifier += "-photon";
        }

        protected override string TargetFramework => "net8.0";
        protected override string RuntimeIdentifier { get; }

        public override Uploader CreateUploader()
        {
            // Temporarily fix for zstd (current default) not being supported on some systems: https://github.com/ppy/osu/issues/30175
            // Todo: Remove with the next velopack release.
            const string extra_args = " --compression gzip";

            return new LinuxVelopackUploader(app_name, os_name, RuntimeIdentifier, RuntimeIdentifier, extraArgs: extra_args, stagingPath: stagingTarget);
        }

        public override void Build()
        {
            if (Directory.Exists(stagingTarget))
                Directory.Delete(stagingTarget, true);
            Directory.CreateDirectory(stagingTarget);

            foreach (var file in Directory.GetFiles(Path.Combine(Program.TemplatesPath, app_dir)))
                new FileInfo(file).CopyTo(Path.Combine(stagingTarget, Path.GetFileName(file)));

            Program.RunCommand("chmod", $"+x {stagingTarget}/AppRun");

            RunDotnetPublish(outputDir: publishTarget);
        }
    }
}
