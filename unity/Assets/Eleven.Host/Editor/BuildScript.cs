using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Eleven.Host.Editor
{
    /// <summary>
    /// Batch-mode build entry point:
    /// <c>Unity -batchmode -projectPath unity -executeMethod Eleven.Host.Editor.BuildScript.BuildWeb</c>.
    ///
    /// Arguments: <c>-customBuildPath</c> (output folder; GameCI passes it), <c>-elevenBuildNumber</c>
    /// and <c>-elevenCommit</c> (shown on screen as the build id), <c>-elevenReport</c> (a text
    /// file the build summary and every error are written to, so CI can surface them without
    /// access to the Unity log).
    /// </summary>
    public static class BuildScript
    {
        public const string ScenePath = "Assets/Generated/Bootstrap.unity";

        public static void BuildWeb()
        {
            string[] args = Environment.GetCommandLineArgs();
            string output = Argument(args, "-customBuildPath") ?? "../build/WebGL/WebGL";
            string reportPath = Argument(args, "-elevenReport");
            string buildId = BuildId(Argument(args, "-elevenBuildNumber") ?? "dev", Argument(args, "-elevenCommit") ?? "local");

            var report = new StringBuilder();
            void OnLog(string message, string stackTrace, LogType type)
            {
                if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                {
                    report.AppendLine($"error: {Flatten(message)}");
                }
            }

            Application.logMessageReceived += OnLog;
            bool succeeded = false;
            try
            {
                ProjectSetup.Apply(buildId);
                ProjectSetup.CreateBootstrapScene(ScenePath);

                var options = new BuildPlayerOptions
                {
                    scenes = new[] { ScenePath },
                    locationPathName = output,
                    target = BuildTarget.WebGL,
                    options = BuildOptions.None,
                };

                BuildReport build = BuildPipeline.BuildPlayer(options);
                BuildSummary summary = build.summary;
                if (summary.result == BuildResult.Succeeded)
                {
                    InstallIcons(summary.outputPath);
                    WriteServiceWorker(summary.outputPath, buildId);
                }
                succeeded = summary.result == BuildResult.Succeeded;

                report.Insert(0, FormattableString.Invariant($"result: {summary.result}\nbuild: {buildId}\nunity: {Application.unityVersion}\nseconds: {summary.totalTime.TotalSeconds:F0}\nbytes: {summary.totalSize}\noutput: {summary.outputPath}\n"));
                foreach (BuildStepMessage message in build.steps.SelectMany(step => step.messages))
                {
                    if (message.type == LogType.Error || message.type == LogType.Exception)
                    {
                        report.AppendLine($"error: {Flatten(message.content)}");
                    }
                }
            }
            catch (Exception e)
            {
                report.AppendLine($"error: {Flatten(e.ToString())}");
            }
            finally
            {
                Application.logMessageReceived -= OnLog;
                string text = report.ToString();
                Debug.Log($"ELEVEN build report\n{text}");
                if (!string.IsNullOrEmpty(reportPath)) File.WriteAllText(reportPath, text);
            }

            if (!succeeded) EditorApplication.Exit(1);
        }

        /// <summary>
        /// Replaces the PWA template's Unity icons with ELEVEN's (<c>unity/WebPlayer/icons</c>): the
        /// home-screen icon, the manifest's icons and the favicon.
        /// </summary>
        private static void InstallIcons(string player)
        {
            string webPlayer = Path.Combine(Application.dataPath, "..", "WebPlayer");
            string root = Path.GetFullPath(player);
            Directory.CreateDirectory(Path.Combine(root, "icons"));
            foreach (string icon in Directory.GetFiles(Path.Combine(webPlayer, "icons")).Where(file => file.EndsWith(".png") || file.EndsWith(".svg")))
            {
                File.Copy(icon, Path.Combine(root, "icons", Path.GetFileName(icon)), overwrite: true);
            }

            File.Copy(Path.Combine(webPlayer, "manifest.webmanifest"), Path.Combine(root, "manifest.webmanifest"), overwrite: true);

            string index = Path.Combine(root, "index.html");
            string html = File.ReadAllText(index);
            const string templateIcon = "<link rel=\"shortcut icon\" href=\"TemplateData/favicon.ico\">";
            if (!html.Contains(templateIcon)) throw new InvalidOperationException($"index.html has no {templateIcon} to replace.");
            File.WriteAllText(index, html.Replace(templateIcon,
                "<link rel=\"icon\" type=\"image/svg+xml\" href=\"icons/eleven.svg\">\n" +
                "    <link rel=\"icon\" type=\"image/png\" sizes=\"32x32\" href=\"icons/eleven-32.png\">\n" +
                "    <link rel=\"apple-touch-icon\" href=\"icons/eleven-180.png\">"));
        }

        /// <summary>
        /// Replaces the PWA template's service worker with ELEVEN's (<c>unity/WebPlayer/ServiceWorker.js</c>),
        /// which serves new deploys at once and falls back to its cache only offline. It precaches
        /// every file of the player.
        /// </summary>
        private static void WriteServiceWorker(string player, string buildId)
        {
            string source = File.ReadAllText(Path.Combine(Application.dataPath, "..", "WebPlayer", "ServiceWorker.js"));
            string root = Path.GetFullPath(player);
            string[] files = Directory.GetFiles(root, "*", SearchOption.AllDirectories)
                .Select(file => Path.GetRelativePath(root, file).Replace('\\', '/'))
                .Where(file => file != "ServiceWorker.js")
                .OrderBy(file => file, StringComparer.Ordinal)
                .ToArray();
            string list = string.Join(", ", files.Select(file => $"\"{file}\""));
            File.WriteAllText(Path.Combine(root, "ServiceWorker.js"),
                source.Replace("%BUILD_ID%", buildId).Replace("/* FILES */", list));
        }

        private static string BuildId(string number, string commit) =>
            $"{number}+{(commit.Length > 7 ? commit.Substring(0, 7) : commit)}";

        private static string Argument(string[] args, string name)
        {
            int index = Array.IndexOf(args, name);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        }

        private static string Flatten(string message) => message.Replace("\r", "").Replace("\n", " | ");
    }
}
