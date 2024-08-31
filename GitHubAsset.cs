// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using Newtonsoft.Json;

namespace osu.Desktop.Deploy
{
    public class GitHubAsset
    {
        [JsonProperty("url")]
        public string Url = string.Empty;

        [JsonProperty("id")]
        public int Id;

        [JsonProperty("name")]
        public string Name = string.Empty;
    }
}
