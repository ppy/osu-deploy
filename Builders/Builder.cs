// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.IO;
using System.IO.Compression;
using osu.Desktop.Deploy.Uploaders;
using osu.Framework.IO.Network;

namespace osu.Desktop.Deploy.Builders
{
    public abstract class Builder
    {
        protected abstract string TargetFramework { get; }
        protected abstract string RuntimeIdentifier { get; }

        protected string SplashImagePath => Path.Combine(Environment.CurrentDirectory, "lazer-velopack.jpg");
        protected string IconPath => Path.Combine(Program.SolutionPath, Program.ProjectName, Program.IconName);

        protected readonly string Version;

        protected Builder(string version)
        {
            Version = version;

            refreshDirectory(Program.STAGING_FOLDER);
        }

        public abstract Uploader CreateUploader();

        public abstract void Build();

        protected void RunDotnetPublish(string? extraArgs = null, string? outputDir = null)
        {
            extraArgs ??= string.Empty;
            outputDir ??= Program.StagingPath;

            Program.RunCommand("dotnet", $"publish"
                                         + $" -f {TargetFramework}"
                                         + $" -r {RuntimeIdentifier}"
                                         + $" -c Release"
                                         + $" -o \"{outputDir}\""
                                         + $" -p:Version={Version}"
                                         + $" --self-contained"
                                         + $" {extraArgs}"
                                         + $" {Program.ProjectName}");
        }

        protected void AttachSatoriGC(string? outputDir = null)
        {
            outputDir ??= Program.StagingPath;

            if (Program.UseSatoriGC)
            {
                Logger.Write("Downloading Satori GC release...");
                string satoriArchivePath = Path.GetTempFileName();
                using (var req = new FileWebRequest(satoriArchivePath, $"https://github.com/ppy/Satori/releases/latest/download/{RuntimeIdentifier}.zip"))
                    req.Perform();

                Logger.Write("Extracting Satori GC into staging folder...");

                using (var stream = File.OpenRead(satoriArchivePath))
                using (var archive = new ZipArchive(stream))
                {
                    foreach (var entry in archive.Entries)
                        entry.ExtractToFile(Path.Combine(outputDir, entry.Name), true);
                }
            }
        }

        private static void refreshDirectory(string directory)
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
            Directory.CreateDirectory(directory);
        }
    }
}
