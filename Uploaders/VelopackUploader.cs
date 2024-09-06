// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Linq;
using System.Net.Http;
using osu.Framework.IO.Network;

namespace osu.Desktop.Deploy.Uploaders
{
    public class VelopackUploader : Uploader
    {
        private readonly string applicationName;
        private readonly string operatingSystemName;
        private readonly string runtimeIdentifier;
        private readonly string channel;
        private readonly string? extraArgs;
        private readonly string stagingPath;

        public VelopackUploader(string applicationName, string operatingSystemName, string runtimeIdentifier, string channel, string? extraArgs = null, string? stagingPath = null)
        {
            this.applicationName = applicationName;
            this.operatingSystemName = operatingSystemName;
            this.runtimeIdentifier = runtimeIdentifier;
            this.channel = channel;
            this.extraArgs = extraArgs;
            this.stagingPath = stagingPath ?? Program.StagingPath;
        }

        public override void RestoreBuild()
        {
            if (Program.CanGitHub)
            {
                Program.RunCommand("dotnet", $"vpk download github"
                                             + $" --repoUrl=\"{Program.GitHubRepoUrl}\""
                                             + $" --token=\"{Program.GitHubAccessToken}\""
                                             + $" --channel=\"{channel}\""
                                             + $" --outputDir=\"{Program.ReleasesPath}\"",
                    throwIfNonZero: false,
                    useSolutionPath: false);
            }
        }

        public override void PublishBuild(string version)
        {
            Program.RunCommand("dotnet", $"vpk [{operatingSystemName}] pack"
                                         + $" --packTitle=\"{Program.PackageTitle}\""
                                         + $" --packId=\"{Program.PackageName}\""
                                         + $" --packVersion=\"{version}\""
                                         + $" --runtime=\"{runtimeIdentifier}\""
                                         + $" --outputDir=\"{Program.ReleasesPath}\""
                                         + $" --mainExe=\"{applicationName}\""
                                         + $" --packDir=\"{stagingPath}\""
                                         + $" --channel=\"{channel}\""
                                         + $" {extraArgs}",
                useSolutionPath: false);

            if (Program.CanGitHub && Program.GitHubUpload)
            {
                Program.RunCommand("dotnet", $"vpk upload github"
                                             + $" --repoUrl=\"{Program.GitHubRepoUrl}\""
                                             + $" --token=\"{Program.GitHubAccessToken}\""
                                             + $" --outputDir=\"{Program.ReleasesPath}\""
                                             + $" --tag=\"{version}\""
                                             + $" --releaseName=\"{version}\""
                                             + $" --merge"
                                             + $" --channel=\"{channel}\"",
                    useSolutionPath: false);
            }
        }

        protected void RenameAsset(string fromName, string toName)
        {
            if (!Program.CanGitHub || !Program.GitHubUpload)
                return;

            Logger.Write($"Renaming asset '{fromName}' to '{toName}'");

            GitHubRelease targetRelease = Program.GetLastGithubRelease(true)
                                          ?? throw new Exception("Release not found.");

            GitHubAsset asset = targetRelease.Assets.SingleOrDefault(a => a.Name == fromName)
                                ?? throw new Exception($"Asset '{fromName}' not found in the release.");

            var req = new WebRequest(asset.Url)
            {
                Method = HttpMethod.Patch,
            };

            req.AddRaw(
                $$"""
                  { "name": "{{toName}}" }
                  """);

            req.AuthenticatedBlockingPerform();
        }
    }
}
