// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Diagnostics;
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
            var req = new JsonWebRequest<GitHubRelease>($"{Program.GitHubApiEndpoint}")
            {
                Method = HttpMethod.Post,
            };

            GitHubRelease? targetRelease = Program.GetLastGithubRelease(true);

            if (targetRelease == null || targetRelease.TagName != version)
            {
                Logger.Write($"- Creating release {version}...", ConsoleColor.Yellow);
                req.AddRaw(JsonConvert.SerializeObject(new GitHubRelease
                {
                    Name = version,
                    Draft = true,
                }));
                req.AuthenticatedBlockingPerform();

                targetRelease = req.ResponseObject;
            }
            else
            {
                Logger.Write($"- Adding to existing release {version}...", ConsoleColor.Yellow);
            }

            Debug.Assert(targetRelease.UploadUrl != null);

            var assetUploadUrl = targetRelease.UploadUrl.Replace("{?name,label}", "?name={0}");

            foreach (var a in Directory.GetFiles(Program.RELEASES_FOLDER).Reverse()) //reverse to upload RELEASES first.
            {
                if (Path.GetFileName(a).StartsWith('.'))
                    continue;

                Logger.Write($"- Adding asset {a}...", ConsoleColor.Yellow);
                var upload = new WebRequest(assetUploadUrl, Path.GetFileName(a))
                {
                    Method = HttpMethod.Post,
                    Timeout = 240000,
                    ContentType = "application/octet-stream",
                };

                upload.AddRaw(File.ReadAllBytes(a));
                upload.AuthenticatedBlockingPerform();
            }
        }
    }
}
