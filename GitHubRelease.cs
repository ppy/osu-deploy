// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using Newtonsoft.Json;

namespace osu.Desktop.Deploy
{
    public class GitHubRelease
    {
        [JsonProperty(@"id")]
        public int Id;

        [JsonProperty(@"tag_name")]
        public string TagName => $"{Name}";

        [JsonProperty(@"name")]
        public string Name;

        [JsonProperty(@"draft")]
        public bool Draft;

        [JsonProperty(@"prerelease")]
        public bool PreRelease;

        [JsonProperty(@"upload_url")]
        public string UploadUrl;
    }
}
