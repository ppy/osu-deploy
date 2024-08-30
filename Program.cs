// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Linq;
using osu.Desktop.Deploy.Builders;
using osu.Desktop.Deploy.Uploaders;
using osu.Framework;
using osu.Framework.IO.Network;

namespace osu.Desktop.Deploy
{
    internal static class Program
    {
        public const string STAGING_FOLDER = "staging";
        public const string TEMPLATES_FOLDER = "templates";
        public const string RELEASES_FOLDER = "releases";

        public static string StagingPath => Path.Combine(Environment.CurrentDirectory, STAGING_FOLDER);
        public static string TemplatesPath => Path.Combine(Environment.CurrentDirectory, TEMPLATES_FOLDER);
        public static string ReleasesPath => Path.Combine(Environment.CurrentDirectory, RELEASES_FOLDER);

        public static string SolutionName => GetConfiguration("SolutionName");
        public static string ProjectName => GetConfiguration("ProjectName");
        public static string PackageName => GetConfiguration("PackageName");
        public static string IconName => GetConfiguration("IconName");

        public static bool GitHubUpload => bool.Parse(ConfigurationManager.AppSettings["GitHubUpload"] ?? "false");
        public static string? GitHubUsername => ConfigurationManager.AppSettings["GitHubUsername"];
        public static string? GitHubRepoName => ConfigurationManager.AppSettings["GitHubRepoName"];
        public static string? GitHubAccessToken => ConfigurationManager.AppSettings["GitHubAccessToken"];
        public static string GitHubApiEndpoint => $"https://api.github.com/repos/{GitHubUsername}/{GitHubRepoName}/releases";
        public static string GitHubRepoUrl => $"https://github.com/{GitHubUsername}/{GitHubRepoName}";
        public static bool CanGitHub => !string.IsNullOrEmpty(GitHubAccessToken);

        public static string? WindowsCodeSigningCertPath => ConfigurationManager.AppSettings["WindowsCodeSigningCertPath"];
        public static string? AndroidCodeSigningCertPath => ConfigurationManager.AppSettings["AndroidCodeSigningCertPath"];
        public static string? AppleCodeSignCertName => ConfigurationManager.AppSettings["AppleCodeSignCertName"];
        public static string? AppleInstallSignCertName => ConfigurationManager.AppSettings["AppleInstallSignCertName"];
        public static string? AppleNotaryProfileName => ConfigurationManager.AppSettings["AppleNotaryProfileName"];
        public static string? AppleKeyChainPath => ConfigurationManager.AppSettings["AppleKeyChainPath"];

        public static bool IncrementVersion => bool.Parse(ConfigurationManager.AppSettings["IncrementVersion"] ?? "true");

        public static string SolutionPath { get; private set; } = null!;

        private static bool interactive;

        /// <summary>
        /// args[0]: code signing passphrase
        /// args[1]: version
        /// args[2]: platform
        /// args[3]: arch
        /// </summary>
        public static void Main(string[] args)
        {
            interactive = args.Length == 0;
            displayHeader();

            findSolutionPath();

            if (!Directory.Exists(RELEASES_FOLDER))
            {
                Logger.Write("WARNING: No release directory found. Make sure you want this!", ConsoleColor.Yellow);
                Directory.CreateDirectory(RELEASES_FOLDER);
            }

            GitHubRelease? lastRelease = null;

            if (CanGitHub)
            {
                Logger.Write("Checking GitHub releases...");
                lastRelease = GetLastGithubRelease();

                Logger.Write(lastRelease == null
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
            Console.WriteLine($"Signing Certificate:   {WindowsCodeSigningCertPath}");
            Console.WriteLine($"Upload to GitHub:      {GitHubUpload}");
            Console.WriteLine();
            Console.Write($"Ready to deploy version {version} on platform {targetPlatform}!");

            PauseIfInteractive();

            Builder builder;

            switch (targetPlatform)
            {
                case RuntimeInfo.Platform.Windows:
                    builder = new WindowsBuilder(version, getArg(0));
                    break;

                case RuntimeInfo.Platform.Linux:
                    builder = new LinuxBuilder(version);
                    break;

                case RuntimeInfo.Platform.macOS:
                    builder = new MacOSBuilder(version, getArg(3));
                    break;

                case RuntimeInfo.Platform.iOS:
                    builder = new IOSBuilder(version);
                    break;

                case RuntimeInfo.Platform.Android:
                    builder = new AndroidBuilder(version, getArg(0));
                    break;

                default:
                    throw new PlatformNotSupportedException(targetPlatform.ToString());
            }

            Uploader uploader = builder.CreateUploader();

            Logger.Write("Restoring previous build...");
            uploader.RestoreBuild();

            Logger.Write("Running build...");
            builder.Build();

            Logger.Write("Creating release...");
            uploader.PublishBuild(version);

            if (CanGitHub && GitHubUpload)
                openGitHubReleasePage();

            Logger.Write("Done!");
            PauseIfInteractive();
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

        private static void openGitHubReleasePage()
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = GitHubApiEndpoint,
                UseShellExecute = true //see https://github.com/dotnet/corefx/issues/10361
            });
        }

        public static GitHubRelease? GetLastGithubRelease(bool includeDrafts = false)
        {
            var req = new JsonWebRequest<List<GitHubRelease>>($"{GitHubApiEndpoint}");
            req.AuthenticatedBlockingPerform();
            return req.ResponseObject.FirstOrDefault(r => includeDrafts || !r.Draft);
        }

        /// <summary>
        /// Find the base path of the active solution (git checkout location)
        /// </summary>
        private static void findSolutionPath()
        {
            string? path = Path.GetDirectoryName(Environment.CommandLine.Replace("\"", "").Trim());

            if (string.IsNullOrEmpty(path))
                path = Environment.CurrentDirectory;

            while (true)
            {
                if (File.Exists(Path.Combine(path, $"{Program.SolutionName}.sln")))
                    break;

                if (Directory.Exists(Path.Combine(path, "osu")) && File.Exists(Path.Combine(path, "osu", $"{Program.SolutionName}.sln")))
                {
                    path = Path.Combine(path, "osu");
                    break;
                }

                path = path.Remove(path.LastIndexOf(Path.DirectorySeparatorChar));
            }

            SolutionPath = path + Path.DirectorySeparatorChar;
        }

        public static bool RunCommand(string command, string args, bool useSolutionPath = true, Dictionary<string, string>? environmentVariables = null, bool throwIfNonZero = true,
                                      bool exitOnFail = true)
        {
            Logger.Write($"Running {command} {args}...");

            var psi = new ProcessStartInfo(command, args)
            {
                WorkingDirectory = useSolutionPath ? SolutionPath : Environment.CurrentDirectory,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            if (environmentVariables != null)
            {
                foreach (var pair in environmentVariables)
                    psi.EnvironmentVariables.Add(pair.Key, pair.Value);
            }

            try
            {
                Process? p = Process.Start(psi);
                if (p == null) return false;

                string output = p.StandardOutput.ReadToEnd();
                output += p.StandardError.ReadToEnd();

                p.WaitForExit();

                if (p.ExitCode == 0) return true;

                Logger.Write(output);
            }
            catch (Exception e)
            {
                Logger.Write(e.Message);
            }

            if (!throwIfNonZero) return false;

            if (exitOnFail)
                Logger.Error($"Command {command} {args} failed!");
            else
                Logger.Write($"Command {command} {args} failed!");
            return false;
        }

        public static string? ReadLineMasked(string prompt)
        {
            Console.WriteLine(prompt);

            var fg = Console.ForegroundColor;
            Console.ForegroundColor = Console.BackgroundColor;

            var ret = Console.ReadLine();
            Console.ForegroundColor = fg;

            return ret;
        }

        public static void PauseIfInteractive()
        {
            if (interactive)
                Console.ReadLine();
            else
                Console.WriteLine();
        }

        public static string GetConfiguration(string key)
            => ConfigurationManager.AppSettings[key] ?? throw new Exception($"Configuration key '{key}' not found.");

        public static void AuthenticatedBlockingPerform(this WebRequest r)
        {
            r.AddHeader("Authorization", $"token {GitHubAccessToken}");
            r.Perform();
        }

        private static string? getArg(int index)
        {
            string[] args = Environment.GetCommandLineArgs();
            return args.Length > ++index ? args[index] : null;
        }
    }
}
