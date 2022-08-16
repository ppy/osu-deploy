// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using Newtonsoft.Json;
using osu.Framework;
using osu.Framework.Extensions;
using osu.Framework.IO.Network;

namespace osu.Desktop.Deploy
{
    internal static class Program
    {
        private static string packages => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");

        private static string nugetPath => getToolPath("NuGet.CommandLine", "NuGet.exe");
        private static string squirrelPath => getToolPath("Clowd.Squirrel", "Squirrel.exe");

        private const string staging_folder = "staging";
        private const string templates_folder = "templates";
        private const string releases_folder = "releases";

        /// <summary>
        /// How many previous build deltas we want to keep when publishing.
        /// </summary>
        private const int keep_delta_count = 4;

        public static string? GitHubAccessToken = ConfigurationManager.AppSettings["GitHubAccessToken"];
        public static bool GitHubUpload = bool.Parse(ConfigurationManager.AppSettings["GitHubUpload"] ?? "false");
        public static string? GitHubUsername = ConfigurationManager.AppSettings["GitHubUsername"];
        public static string? GitHubRepoName = ConfigurationManager.AppSettings["GitHubRepoName"];
        public static string? SolutionName = ConfigurationManager.AppSettings["SolutionName"];
        public static string? ProjectName = ConfigurationManager.AppSettings["ProjectName"];
        public static string? NuSpecName = ConfigurationManager.AppSettings["NuSpecName"];
        public static bool IncrementVersion = bool.Parse(ConfigurationManager.AppSettings["IncrementVersion"] ?? "true");
        public static string? PackageName = ConfigurationManager.AppSettings["PackageName"];
        public static string? IconName = ConfigurationManager.AppSettings["IconName"];
        public static string? CodeSigningCertificate = ConfigurationManager.AppSettings["CodeSigningCertificate"];

        public static string GitHubApiEndpoint => $"https://api.github.com/repos/{GitHubUsername}/{GitHubRepoName}/releases";

        private static string? solutionPath;

        private static string stagingPath => Path.Combine(Environment.CurrentDirectory, staging_folder);
        private static string templatesPath => Path.Combine(Environment.CurrentDirectory, templates_folder);
        private static string releasesPath => Path.Combine(Environment.CurrentDirectory, releases_folder);

        private static string iconPath
        {
            get
            {
                Debug.Assert(solutionPath != null);
                Debug.Assert(ProjectName != null);
                Debug.Assert(IconName != null);

                return Path.Combine(solutionPath, ProjectName, IconName);
            }
        }

        private static string splashImagePath
        {
            get
            {
                Debug.Assert(solutionPath != null);
                return Path.Combine(solutionPath, "assets\\lazer-nuget.png");
            }
        }

        private static readonly Stopwatch stopwatch = new Stopwatch();

        private static bool interactive;

        public static void Main(string[] args)
        {
            interactive = args.Length == 0;
            displayHeader();

            solutionPath = findSolution(SolutionName);

            if (!Directory.Exists(releases_folder))
            {
                write("WARNING: No release directory found. Make sure you want this!", ConsoleColor.Yellow);
                Directory.CreateDirectory(releases_folder);
            }

            GitHubRelease? lastRelease = null;

            if (canGitHub)
            {
                write("Checking GitHub releases...");
                lastRelease = getLastGithubRelease();

                write(lastRelease == null
                    ? "This is the first GitHub release"
                    : $"Last GitHub release was {lastRelease.Name}.");
            }

            //increment build number until we have a unique one.
            string verBase = DateTime.Now.ToString("yyyy.Mdd.");
            int increment = 0;

            if (lastRelease?.TagName.StartsWith(verBase, StringComparison.InvariantCulture) ?? false)
                increment = int.Parse(lastRelease.TagName.Split('.')[2]) + (IncrementVersion ? 1 : 0);

            string version = $"{verBase}{increment}";

            var targetPlatform = RuntimeInfo.OS;

            if (args.Length > 1 && !string.IsNullOrEmpty(args[1]))
                version = args[1];
            if (args.Length > 2 && !string.IsNullOrEmpty(args[2]))
                Enum.TryParse(args[2], true, out targetPlatform);

            Console.ResetColor();
            Console.WriteLine($"Increment Version:     {IncrementVersion}");
            Console.WriteLine($"Signing Certificate:   {CodeSigningCertificate}");
            Console.WriteLine($"Upload to GitHub:      {GitHubUpload}");
            Console.WriteLine();
            Console.Write($"Ready to deploy version {version} on platform {targetPlatform}!");

            pauseIfInteractive();

            stopwatch.Start();

            refreshDirectory(staging_folder);
            updateAppveyorVersion(version);

            Debug.Assert(solutionPath != null);

            write("Running build process...");

            switch (targetPlatform)
            {
                case RuntimeInfo.Platform.Windows:
                    if (lastRelease != null)
                        getAssetsFromRelease(lastRelease);

                    runCommand("dotnet", $"publish -f net6.0 -r win-x64 {ProjectName} -o {stagingPath} --configuration Release /p:Version={version}");

                    // add icon to dotnet stub
                    runCommand("tools/rcedit-x64.exe", $"\"{stagingPath}\\osu!.exe\" --set-icon \"{iconPath}\"");

                    write("Creating NuGet deployment package...");
                    runCommand(nugetPath, $"pack {NuSpecName} -Version {version} -Properties Configuration=Deploy -OutputDirectory {stagingPath} -BasePath {stagingPath}");

                    // prune once before checking for files so we can avoid erroring on files which aren't even needed for this build.
                    pruneReleases();

                    checkReleaseFiles();

                    write("Running squirrel build...");

                    string codeSigningCmd = string.Empty;

                    if (!string.IsNullOrEmpty(CodeSigningCertificate))
                    {
                        string? codeSigningPassword;

                        if (args.Length > 0)
                        {
                            codeSigningPassword = args[0];
                        }
                        else
                        {
                            Console.Write("Enter code signing password: ");
                            codeSigningPassword = readLineMasked();
                        }

                        string codeSigningCertPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), CodeSigningCertificate);
                        codeSigningCmd = string.IsNullOrEmpty(codeSigningPassword)
                            ? ""
                            : $"--signParams=\"/td sha256 /fd sha256 /f {codeSigningCertPath} /p {codeSigningPassword} /tr http://timestamp.comodoca.com\"";
                    }


                    string nupkgFilename = $"{PackageName}.{version}.nupkg";

                    runCommand(squirrelPath,
                        $"releasify --package={stagingPath}\\{nupkgFilename} --releaseDir={releasesPath} --icon={iconPath} --appIcon={iconPath} --splashImage={splashImagePath} {codeSigningCmd}");

                    // prune again to clean up before upload.
                    pruneReleases();

                    // rename setup to install.
                    File.Copy(Path.Combine(releases_folder, "osulazerSetup.exe"), Path.Combine(releases_folder, "install.exe"), true);
                    File.Delete(Path.Combine(releases_folder, "osulazerSetup.exe"));
                    break;

                case RuntimeInfo.Platform.macOS:
                    string targetArch = "";
                    if (args.Length > 0)
                    {
                        targetArch = args[0];
                    }
                    else if (interactive)
                    {
                        Console.Write("Build for which architecture? [x64/arm64]: ");
                        targetArch = Console.ReadLine() ?? string.Empty;
                    }

                    if (targetArch != "x64" && targetArch != "arm64")
                        error($"Invalid Architecture: {targetArch}");

                    buildForMac(targetArch, version);
                    break;

                case RuntimeInfo.Platform.Linux:
                    const string app_dir = "osu!.AppDir";

                    string stagingTarget = Path.Combine(stagingPath, app_dir);

                    if (Directory.Exists(stagingTarget))
                        Directory.Delete(stagingTarget, true);

                    Directory.CreateDirectory(stagingTarget);

                    foreach (var file in Directory.GetFiles(Path.Combine(templatesPath, app_dir)))
                        new FileInfo(file).CopyTo(Path.Combine(stagingTarget, Path.GetFileName(file)));

                    // mark AppRun as executable, as zip does not contains executable information
                    runCommand("chmod", $"+x {stagingTarget}/AppRun");

                    runCommand("dotnet", $"publish -f net6.0 -r linux-x64 {ProjectName} -o {stagingTarget}/usr/bin/ --configuration Release /p:Version={version} --self-contained");

                    // mark output as executable
                    runCommand("chmod", $"+x {stagingTarget}/usr/bin/osu!");

                    // copy png icon (for desktop file)
                    File.Copy(Path.Combine(solutionPath, "assets/lazer.png"), $"{stagingTarget}/osu!.png");

                    // download appimagetool
                    string appImageToolPath = $"{stagingPath}/appimagetool.AppImage";

                    using (var client = new HttpClient())
                    {
                        using (var stream = client.GetStreamAsync("https://github.com/AppImage/AppImageKit/releases/download/continuous/appimagetool-x86_64.AppImage").GetResultSafely())
                        using (var fileStream = new FileStream(appImageToolPath, FileMode.CreateNew))
                        {
                            stream.CopyToAsync(fileStream).WaitSafely();
                        }
                    }

                    // mark appimagetool as executable
                    runCommand("chmod", $"a+x {appImageToolPath}");

                    // create AppImage itself
                    // gh-releases-zsync stands here for GitHub Releases ZSync, that is a way to check for updates
                    // ppy|osu|latest stands for https://github.com/ppy/osu and get the latest release
                    // osu.AppImage.zsync is AppImage update information file, that is generated by the tool
                    // more information there https://docs.appimage.org/packaging-guide/optional/updates.html?highlight=update#using-appimagetool
                    runCommand(appImageToolPath,
                        $"\"{stagingTarget}\" -u \"gh-releases-zsync|ppy|osu|latest|osu.AppImage.zsync\" \"{Path.Combine(Environment.CurrentDirectory, "releases")}/osu.AppImage\" --sign", false);

                    // mark finally the osu! AppImage as executable -> Don't compress it.
                    runCommand("chmod", $"+x \"{Path.Combine(Environment.CurrentDirectory, "releases")}/osu.AppImage\"");

                    // copy update information
                    File.Move(Path.Combine(Environment.CurrentDirectory, "osu.AppImage.zsync"), $"{releases_folder}/osu.AppImage.zsync", true);

                    break;
            }

            if (GitHubUpload)
                uploadBuild(version);

            write("Done!");
            pauseIfInteractive();
        }

        private static void displayHeader()
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine();
            Console.WriteLine("  Please note that OSU! and PPY are registered trademarks and as such covered by trademark law.");
            Console.WriteLine("  Do not distribute builds of this project publicly that make use of these.");
            Console.ResetColor();
            Console.WriteLine();
        }

        private static void buildForMac(string arch, string version)
        {
            // unzip the template app, with all structure existing except for dotnet published content.
            runCommand("unzip", $"\"osu!.app-template.zip\" -d {stagingPath}", false);

            // without touching the app bundle itself, changes to file associations / icons / etc. will be cached at a macOS level and not updated.
            runCommand("touch", $"\"{Path.Combine(stagingPath, "osu!.app")}\" {stagingPath}", false);

            runCommand("dotnet", $"publish -r osx-{arch} {ProjectName} --configuration Release -o {stagingPath}/osu!.app/Contents/MacOS /p:Version={version}");

            string stagingApp = $"{stagingPath}/osu!.app";
            string archLabel = arch == "x64" ? "Intel" : "Apple Silicon";
            string zippedApp = $"{releasesPath}/osu!.app ({archLabel}).zip";

            // correct permissions post-build. dotnet outputs 644 by default; we want 755.
            runCommand("chmod", $"-R 755 {stagingApp}");

            if (!string.IsNullOrEmpty(CodeSigningCertificate))
            {
                // sign using apple codesign
                runCommand("codesign",
                    $"--deep --force --verify --entitlements {Path.Combine(Environment.CurrentDirectory, "osu.entitlements")} -o runtime --verbose --sign \"{CodeSigningCertificate}\" {stagingApp}");

                // check codesign was successful
                runCommand("spctl", $"--assess -vvvv {stagingApp}");
            }

            // package for distribution
            runCommand("ditto", $"-ck --rsrc --keepParent --sequesterRsrc {stagingApp} \"{zippedApp}\"");

            string notarisationUsername = ConfigurationManager.AppSettings["AppleUsername"] ?? string.Empty;

            if (!string.IsNullOrEmpty(notarisationUsername))
            {
                // upload for notarisation
                runCommand("xcrun",
                    $"altool --notarize-app --primary-bundle-id \"sh.ppy.osu.lazer\" --username \"{notarisationUsername}\" --password \"{ConfigurationManager.AppSettings["ApplePassword"]}\" --file \"{zippedApp}\"");
                // TODO: make this actually wait properly
                write("Waiting for notarisation to complete..");
                Thread.Sleep(60000 * 5);

                // staple notarisation result
                runCommand("xcrun", $"stapler staple {stagingApp}");
            }

            File.Delete(zippedApp);

            // repackage for distribution
            runCommand("ditto", $"-ck --rsrc --keepParent --sequesterRsrc {stagingApp} \"{zippedApp}\"");
        }

        /// <summary>
        /// Ensure we have all the files in the release directory which are expected to be there.
        /// This should have been accounted for in earlier steps, and just serves as a verification step.
        /// </summary>
        private static void checkReleaseFiles()
        {
            if (!canGitHub) return;

            var releaseLines = getReleaseLines();

            //ensure we have all files necessary
            foreach (var l in releaseLines)
                if (!File.Exists(Path.Combine(releases_folder, l.Filename)))
                    error($"Local file missing {l.Filename}");
        }

        private static IEnumerable<ReleaseLine> getReleaseLines()
        {
            return File.ReadAllLines(Path.Combine(releases_folder, "RELEASES")).Select(l => new ReleaseLine(l));
        }

        private static void pruneReleases()
        {
            if (!canGitHub) return;

            write("Pruning RELEASES...");

            var releaseLines = getReleaseLines().ToList();

            var fulls = releaseLines.Where(l => l.Filename.Contains("-full")).Reverse().Skip(1);

            //remove any FULL releases (except most recent)
            foreach (var l in fulls)
            {
                write($"- Removing old release {l.Filename}", ConsoleColor.Yellow);
                File.Delete(Path.Combine(releases_folder, l.Filename));
                releaseLines.Remove(l);
            }

            //remove excess deltas
            var deltas = releaseLines.Where(l => l.Filename.Contains("-delta")).ToArray();
            if (deltas.Length > keep_delta_count)
            {
                foreach (var l in deltas.Take(deltas.Length - keep_delta_count))
                {
                    write($"- Removing old delta {l.Filename}", ConsoleColor.Yellow);
                    File.Delete(Path.Combine(releases_folder, l.Filename));
                    releaseLines.Remove(l);
                }
            }

            var lines = new List<string>();
            releaseLines.ForEach(l => lines.Add(l.ToString()));
            File.WriteAllLines(Path.Combine(releases_folder, "RELEASES"), lines);
        }

        private static void uploadBuild(string version)
        {
            if (!canGitHub || string.IsNullOrEmpty(CodeSigningCertificate))
                return;

            write("Publishing to GitHub...");

            var req = new JsonWebRequest<GitHubRelease>($"{GitHubApiEndpoint}")
            {
                Method = HttpMethod.Post,
            };

            GitHubRelease? targetRelease = getLastGithubRelease(true);

            if (targetRelease == null || targetRelease.TagName != version)
            {
                write($"- Creating release {version}...", ConsoleColor.Yellow);
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
                write($"- Adding to existing release {version}...", ConsoleColor.Yellow);
            }

            Debug.Assert(targetRelease.UploadUrl != null);

            var assetUploadUrl = targetRelease.UploadUrl.Replace("{?name,label}", "?name={0}");
            foreach (var a in Directory.GetFiles(releases_folder).Reverse()) //reverse to upload RELEASES first.
            {
                if (Path.GetFileName(a).StartsWith('.'))
                    continue;

                write($"- Adding asset {a}...", ConsoleColor.Yellow);
                var upload = new WebRequest(assetUploadUrl, Path.GetFileName(a))
                {
                    Method = HttpMethod.Post,
                    Timeout = 240000,
                    ContentType = "application/octet-stream",
                };

                upload.AddRaw(File.ReadAllBytes(a));
                upload.AuthenticatedBlockingPerform();
            }

            openGitHubReleasePage();
        }

        private static void openGitHubReleasePage()
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = $"https://github.com/{GitHubUsername}/{GitHubRepoName}/releases",
                UseShellExecute = true //see https://github.com/dotnet/corefx/issues/10361
            });
        }

        private static bool canGitHub => !string.IsNullOrEmpty(GitHubAccessToken);

        private static GitHubRelease? getLastGithubRelease(bool includeDrafts = false)
        {
            var req = new JsonWebRequest<List<GitHubRelease>>($"{GitHubApiEndpoint}");
            req.AuthenticatedBlockingPerform();
            return req.ResponseObject.FirstOrDefault(r => includeDrafts || !r.Draft);
        }

        /// <summary>
        /// Download assets from a previous release into the releases folder.
        /// </summary>
        /// <param name="release"></param>
        private static void getAssetsFromRelease(GitHubRelease release)
        {
            if (!canGitHub) return;

            //there's a previous release for this project.
            var assetReq = new JsonWebRequest<List<GitHubObject>>($"{GitHubApiEndpoint}/{release.Id}/assets");
            assetReq.AuthenticatedBlockingPerform();
            var assets = assetReq.ResponseObject;

            //make sure our RELEASES file is the same as the last build on the server.
            var releaseAsset = assets.FirstOrDefault(a => a.Name == "RELEASES");

            //if we don't have a RELEASES asset then the previous release likely wasn't a Squirrel one.
            if (releaseAsset == null) return;

            bool requireDownload = false;

            if (!File.Exists(Path.Combine(releases_folder, $"{PackageName}-{release.Name}-full.nupkg")))
            {
                write("Last version's package not found locally.", ConsoleColor.Red);
                requireDownload = true;
            }
            else
            {
                var lastReleases = new RawFileWebRequest($"{GitHubApiEndpoint}/assets/{releaseAsset.Id}");
                lastReleases.AuthenticatedBlockingPerform();
                if (File.ReadAllText(Path.Combine(releases_folder, "RELEASES")) != lastReleases.GetResponseString())
                {
                    write("Server's RELEASES differed from ours.", ConsoleColor.Red);
                    requireDownload = true;
                }
            }

            if (!requireDownload) return;

            write("Refreshing local releases directory...");
            refreshDirectory(releases_folder);

            foreach (var a in assets)
            {
                if (a.Name != "RELEASES" && !a.Name.EndsWith(".nupkg", StringComparison.InvariantCulture)) continue;

                write($"- Downloading {a.Name}...", ConsoleColor.Yellow);
                new FileWebRequest(Path.Combine(releases_folder, a.Name), $"{GitHubApiEndpoint}/assets/{a.Id}").AuthenticatedBlockingPerform();
            }
        }

        private static void refreshDirectory(string directory)
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
            Directory.CreateDirectory(directory);
        }

        /// <summary>
        /// Find the base path of the active solution (git checkout location)
        /// </summary>
        private static string findSolution(string? name)
        {
            string? path = Path.GetDirectoryName(Environment.CommandLine.Replace("\"", "").Trim());

            if (string.IsNullOrEmpty(path))
                path = Environment.CurrentDirectory;

            while (true)
            {
                if (File.Exists(Path.Combine(path, $"{name}.sln")))
                    break;

                if (Directory.Exists(Path.Combine(path, "osu")) && File.Exists(Path.Combine(path, "osu", $"{name}.sln")))
                {
                    path = Path.Combine(path, "osu");
                    break;
                }

                path = path.Remove(path.LastIndexOf(Path.DirectorySeparatorChar));
            }

            path += Path.DirectorySeparatorChar;

            return path;
        }

        private static bool runCommand(string command, string args, bool useSolutionPath = true)
        {
            write($"Running {command} {args}...");

            var psi = new ProcessStartInfo(command, args)
            {
                WorkingDirectory = useSolutionPath ? solutionPath : Environment.CurrentDirectory,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            Process? p = Process.Start(psi);
            if (p == null) return false;

            string output = p.StandardOutput.ReadToEnd();
            output += p.StandardError.ReadToEnd();

            p.WaitForExit();

            if (p.ExitCode == 0) return true;

            write(output);
            error($"Command {command} {args} failed!");
            return false;
        }

        private static string? readLineMasked()
        {
            var fg = Console.ForegroundColor;
            Console.ForegroundColor = Console.BackgroundColor;
            var ret = Console.ReadLine();
            Console.ForegroundColor = fg;

            return ret;
        }

        private static string getToolPath(string packageName, string toolExecutable)
        {
            var process = Process.Start(new ProcessStartInfo("dotnet", "list osu.Desktop.Deploy.csproj package")
            {
                RedirectStandardOutput = true
            });

            Debug.Assert(process != null);

            process.WaitForExit();

            string output = process.StandardOutput.ReadToEnd();

            var match = Regex.Matches(output, $@"(?m){packageName.Replace(".", "\\.")}.*\s(\d{{1,3}}\.\d{{1,3}}\.\d.*?)$");

            if (match.Count == 0)
                throw new InvalidOperationException($"Missing tool for {toolExecutable}");

            var toolPath = Path.Combine(packages, packageName.ToLowerInvariant(), match[0].Groups[1].Value.Trim(), "tools", toolExecutable);

            return toolPath;
        }

        private static void error(string message)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"FATAL ERROR: {message}");

            pauseIfInteractive();
            Environment.Exit(-1);
        }

        private static void pauseIfInteractive()
        {
            if (interactive)
                Console.ReadLine();
            else
                Console.WriteLine();
        }

        private static bool updateAppveyorVersion(string version)
        {
            try
            {
                using (PowerShell ps = PowerShell.Create())
                {
                    ps.AddScript($"Update-AppveyorBuild -Version \"{version}\"");
                    ps.Invoke();
                }

                return true;
            }
            catch
            {
                // we don't have appveyor and don't care
            }

            return false;
        }

        private static void write(string message, ConsoleColor col = ConsoleColor.Gray)
        {
            if (stopwatch.ElapsedMilliseconds > 0)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.Write(stopwatch.ElapsedMilliseconds.ToString().PadRight(8));
            }

            Console.ForegroundColor = col;
            Console.WriteLine(message);
        }

        public static void AuthenticatedBlockingPerform(this WebRequest r)
        {
            r.AddHeader("Authorization", $"token {GitHubAccessToken}");
            r.Perform();
        }
    }

    internal class RawFileWebRequest : WebRequest
    {
        public RawFileWebRequest(string url)
            : base(url)
        {
        }

        protected override string Accept => "application/octet-stream";
    }

    internal class ReleaseLine
    {
        public string Hash;
        public string Filename;
        public int Filesize;

        public ReleaseLine(string line)
        {
            var split = line.Split(' ');
            Hash = split[0];
            Filename = split[1];
            Filesize = int.Parse(split[2]);
        }

        public override string ToString()
        {
            return $"{Hash} {Filename} {Filesize}";
        }
    }
}
