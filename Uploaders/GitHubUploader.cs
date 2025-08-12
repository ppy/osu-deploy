// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using Newtonsoft.Json;
using osu.Framework.IO.Network;

namespace osu.Desktop.Deploy.Uploaders
{
    public class GitHubUploader : Uploader
    {
        public override void RestoreBuild()
        {
        }

        public override void PublishBuild(string version)
        {
            if (!Program.CanGitHub || !Program.GitHubUpload)
                return;

            GitHubRelease? targetRelease = Program.GetLastGithubRelease(true);

            if (targetRelease == null || targetRelease.TagName != version)
            {
                Logger.Write($"- Creating release {version}...", ConsoleColor.Yellow);
                targetRelease = createRelease(version);
            }
            else
                Logger.Write($"- Adding to existing release {version}...", ConsoleColor.Yellow);

            if (!targetRelease.Draft)
                throw new Exception("Cannot upload to a non-draft release");

            foreach (var assetPath in Directory.GetFiles(Program.RELEASES_FOLDER).Reverse()) //reverse to upload RELEASES first.
            {
                string assetName = Path.GetFileName(assetPath);

                if (assetName.StartsWith('.'))
                    continue;

                GitHubAsset? existing = targetRelease.Assets.SingleOrDefault(releaseAsset => releaseAsset.Name == assetName);

                if (existing != null)
                {
                    Logger.Write($"- Deleting existing asset {existing.Name}...", ConsoleColor.Yellow);
                    deleteAsset(existing.Url);
                }

                Logger.Write($"- Uploading asset {assetName}...", ConsoleColor.Yellow);
                uploadAsset(targetRelease.UploadUrl.Replace("{?name,label}", "?name={0}"), assetName, assetPath);
            }
        }

        private GitHubRelease createRelease(string name)
        {
            var req = new JsonWebRequest<GitHubRelease>($"{Program.GitHubApiEndpoint}")
            {
                Method = HttpMethod.Post,
            };

            req.AddRaw(JsonConvert.SerializeObject(new GitHubRelease
            {
                Name = name,
                Draft = true
            }));

            req.AuthenticatedBlockingPerform();

            return req.ResponseObject;
        }

        private void deleteAsset(string url)
        {
            var req = new WebRequest(url)
            {
                Method = HttpMethod.Delete
            };

            req.AuthenticatedBlockingPerform();
        }

        private void uploadAsset(string url, string name, string path)
        {
            var req = new WebRequest(url, name)
            {
                Method = HttpMethod.Post,
                Timeout = 240000,
                ContentType = "application/octet-stream",
            };

            req.AddRaw(File.ReadAllBytes(path));
            req.AuthenticatedBlockingPerform();
        }
    }
}
