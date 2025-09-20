// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
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

        public LinuxBuilder(string version, string? arch)
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
            publishTarget = Path.Combine(stagingTarget, "usr", "bin");
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

        // https://learn.microsoft.com/en-us/dotnet/standard/io/how-to-copy-directories
        private static void copyDirectory(string sourceDir, string destinationDir, bool recursive)
        {
            var dir = new DirectoryInfo(sourceDir);

            if (!dir.Exists)
                throw new DirectoryNotFoundException($"Source directory not found: {dir.FullName}");

            DirectoryInfo[] dirs = dir.GetDirectories();

            Directory.CreateDirectory(destinationDir);

            foreach (FileInfo file in dir.GetFiles())
            {
                string targetFilePath = Path.Combine(destinationDir, file.Name);
                file.CopyTo(targetFilePath);
            }

            if (recursive)
            {
                foreach (DirectoryInfo subDir in dirs)
                {
                    string newDestinationDir = Path.Combine(destinationDir, subDir.Name);
                    copyDirectory(subDir.FullName, newDestinationDir, true);
                }
            }
        }

        public override void Build()
        {
            if (Directory.Exists(stagingTarget))
                Directory.Delete(stagingTarget, true);

            copyDirectory(Path.Combine(Program.TemplatesPath, app_dir), stagingTarget, true);

            File.CreateSymbolicLink(Path.Combine(stagingTarget, ".DirIcon"), "osu.png");

            Program.RunCommand("chmod", $"+x {stagingTarget}/AppRun");

            RunDotnetPublish(outputDir: publishTarget);
            AttachSatoriGC(outputDir: publishTarget);
        }
    }
}
