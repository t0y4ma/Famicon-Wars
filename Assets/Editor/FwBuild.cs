using System;
using System.IO;
using System.IO.Compression;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace FamiconWars.EditorTools
{
    /// <summary>
    /// Release builds, same layout as the other games on the VM (see Deploy/README.md).
    ///  - server: Linux dedicated server (.x86_64, Server subtarget, IL2CPP) -> Builds/Server + Builds/FieldCommandServer.zip
    ///  - client: WebGL -> Builds/WebGL (tracked in git; Cloudflare Pages publishes it on every push)
    /// Deploy the server and the web client together whenever the protocol changes.
    /// </summary>
    public static class FwBuild
    {
        const string ServerDir = "Builds/Server";
        const string ServerExe = "FieldCommandServer.x86_64";
        const string ServerZip = "Builds/FieldCommandServer.zip";
        const string WebGLDir = "Builds/WebGL";
        const string ResultFile = "Library/FwBuild/last-result.txt";
        static readonly string[] Scenes = { "Assets/Scenes/Game.unity" };

        [MenuItem("フィールド・コマンド/ビルド/サーバー(Linux .x86_64)")]
        public static void BuildServerMenu() => Request(server: true, web: false);

        [MenuItem("フィールド・コマンド/ビルド/クライアント(WebGL)")]
        public static void BuildWebGLMenu() => Request(server: false, web: true);

        [MenuItem("フィールド・コマンド/ビルド/両方(サーバー+WebGL)")]
        public static void BuildBothMenu() => Request(server: true, web: true);

        // IL2CPP Linux builds fail with "No Linux sysroot found" unless the editor is switched to Linux
        // first, so switch, wait for the recompile, then continue (survives the domain reload).
        const string PendingKey = "FwBuild.Pending";
        const string ResumeAtKey = "FwBuild.ResumeAt";
        const double SettleSeconds = 20;

        public static void Request(bool server, bool web)
        {
            if (!server || EditorUserBuildSettings.activeBuildTarget == BuildTarget.StandaloneLinux64) { Run(server, web); return; }
            SessionState.SetString(PendingKey, (server ? "1" : "0") + "," + (web ? "1" : "0"));
            Debug.Log("[FwBuild] switching to Linux before building (takes a moment)");
            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Standalone, BuildTarget.StandaloneLinux64);
            WatchPending();
        }

        [InitializeOnLoadMethod]
        static void WatchPending()
        {
            EditorApplication.update -= ResumeWhenReady;
            if (string.IsNullOrEmpty(SessionState.GetString(PendingKey, ""))) return;
            SessionState.SetFloat(ResumeAtKey, (float)(EditorApplication.timeSinceStartup + SettleSeconds));
            EditorApplication.update += ResumeWhenReady;
        }

        static void ResumeWhenReady()
        {
            var pending = SessionState.GetString(PendingKey, "");
            if (string.IsNullOrEmpty(pending)) { EditorApplication.update -= ResumeWhenReady; return; }
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.StandaloneLinux64) return;
            if (EditorApplication.timeSinceStartup < SessionState.GetFloat(ResumeAtKey, 0)) return;
            EditorApplication.update -= ResumeWhenReady;
            SessionState.EraseString(PendingKey);
            var parts = pending.Split(',');
            Run(parts[0] == "1", parts.Length > 1 && parts[1] == "1");
        }

        public static bool Run(bool server, bool web)
        {
            var restoreTarget = BuildTarget.StandaloneWindows64;
            PlayerSettings.bundleVersion = DateTime.Now.ToString("yyyy.MM.dd.HHmm");
            var log = new System.Text.StringBuilder();
            bool ok = true;
            try
            {
                if (server) ok &= BuildServer(log);
                if (web && ok) ok &= BuildWebGL(log);
            }
            catch (Exception e) { ok = false; log.AppendLine("exception: " + e); }
            finally
            {
                // left on Server, Mirror treats the editor as headless and starts a server in Play Mode
                EditorUserBuildSettings.standaloneBuildSubtarget = StandaloneBuildSubtarget.Player;
                if (EditorUserBuildSettings.activeBuildTarget != restoreTarget)
                    EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Standalone, restoreTarget);
            }
            log.Insert(0, (ok ? "OK" : "FAILED") + $"  {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n");
            Directory.CreateDirectory(Path.GetDirectoryName(ResultFile));
            File.WriteAllText(ResultFile, log.ToString());
            Debug.Log("[FwBuild] " + log);
            return ok;
        }

        static bool BuildServer(System.Text.StringBuilder log)
        {
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, BuildTarget.StandaloneLinux64))
            { log.AppendLine("server: Linux build support is not installed (Unity Hub: Linux Dedicated Server Build Support)"); return false; }
            // Unity 6 Linux servers built with Mono can crash after start (seen with Nine): use IL2CPP
            PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Server, ScriptingImplementation.IL2CPP);
            if (Directory.Exists(ServerDir)) Directory.Delete(ServerDir, true);
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = Scenes,
                locationPathName = Path.Combine(ServerDir, ServerExe),
                target = BuildTarget.StandaloneLinux64,
                subtarget = (int)StandaloneBuildSubtarget.Server,
                options = BuildOptions.None,
            });
            if (!Report("server", report, log)) return false;

            if (File.Exists(ServerZip)) File.Delete(ServerZip);
            using (var zip = ZipFile.Open(ServerZip, ZipArchiveMode.Create))
            {
                foreach (var file in Directory.GetFiles(ServerDir, "*", SearchOption.AllDirectories))
                {
                    var rel = file.Substring(ServerDir.Length + 1).Replace('\\', '/');
                    if (rel.Contains("_DoNotShip") || rel.Contains("_BackUpThisFolder_ButDontShipItWithYourGame")) continue;
                    var entry = zip.CreateEntryFromFile(file, rel, System.IO.Compression.CompressionLevel.Optimal);
                    bool exec = rel.EndsWith(".x86_64") || rel.EndsWith(".so") || rel.Contains(".so.");
                    entry.ExternalAttributes = (exec ? 0x81ED : 0x81A4) << 16;   // unix mode 755 / 644
                }
            }
            log.AppendLine($"  zip for the VM: {ServerZip} ({new FileInfo(ServerZip).Length / (1024f * 1024f):0.0} MB)");
            return true;
        }

        static bool BuildWebGL(System.Text.StringBuilder log)
        {
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.WebGL, BuildTarget.WebGL))
            { log.AppendLine("WebGL: Web build support is not installed"); return false; }
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
            PlayerSettings.WebGL.decompressionFallback = true;   // works on any static host
            PlayerSettings.runInBackground = true;
            if (Directory.Exists(WebGLDir)) Directory.Delete(WebGLDir, true);
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = Scenes,
                locationPathName = WebGLDir,
                target = BuildTarget.WebGL,
                options = BuildOptions.None,
            });
            return Report("WebGL", report, log);
        }

        static bool Report(string label, BuildReport report, System.Text.StringBuilder log)
        {
            var s = report.summary;
            if (s.result == BuildResult.Succeeded)
            {
                log.AppendLine($"{label}: OK {s.outputPath} ({s.totalSize / (1024f * 1024f):0.0} MB, {s.totalTime.TotalSeconds:0}s)");
                return true;
            }
            log.AppendLine($"{label}: {s.result} ({s.totalErrors} errors)");
            foreach (var step in report.steps)
                foreach (var m in step.messages)
                    if (m.type == LogType.Error || m.type == LogType.Exception) log.AppendLine("  " + m.content);
            return false;
        }
    }
}
