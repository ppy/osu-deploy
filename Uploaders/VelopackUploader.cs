// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

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
    }
}
