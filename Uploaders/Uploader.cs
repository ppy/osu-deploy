// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

namespace osu.Desktop.Deploy.Uploaders
{
    public abstract class Uploader
    {
        public abstract void RestoreBuild();

        public abstract void PublishBuild(string version);
    }
}
