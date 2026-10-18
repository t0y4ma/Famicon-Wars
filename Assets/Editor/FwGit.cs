using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace FamiconWars.EditorTools
{
    /// <summary>
    /// git add / commit / push for the build tool, run on a background thread so the editor never waits
    /// on git. Every git process has its standard output and error read at the same time (a full pipe
    /// would otherwise stop git), and a time limit after which that one process is ended.
    /// Progress and the result go to Library/FwBuild/last-git.txt (first line: RUNNING / OK / FAILED).
    /// Script reloads are held off while it runs so the thread is not cut off.
    /// </summary>
    public static class FwGit
    {
        public const string ResultFile = "Library/FwBuild/last-git.txt";
        const int AddTries = 8;                 // antivirus scanners briefly lock new files ("Permission denied")
        static readonly TimeSpan StepLimit = TimeSpan.FromMinutes(5), PushLimit = TimeSpan.FromMinutes(15);

        static volatile bool running;
        public static bool Running => running;
        static readonly ConcurrentQueue<Action> mainThread = new ConcurrentQueue<Action>();

        [InitializeOnLoadMethod]
        static void Init()
        {
            EditorApplication.update -= Pump;
            EditorApplication.update += Pump;
        }

        static void Pump()
        {
            while (mainThread.TryDequeue(out var a)) { try { a(); } catch (Exception e) { Debug.LogException(e); } }
        }

        /// <summary>Adds everything, commits with the message (skipped when nothing changed) and pushes. Returns at once.</summary>
        /// <param name="done">Called on the main thread with success and the log.</param>
        public static bool CommitAndPushAsync(string message, Action<bool, string> done = null)
        {
            if (running) { Debug.LogWarning("[FwGit] 前の git がまだ動いています"); return false; }
            if (string.IsNullOrWhiteSpace(message)) message = "ビルド " + DateTime.Now.ToString("yyyy-MM-dd HH:mm");
            running = true;
            string root = Path.GetFullPath(".");
            Directory.CreateDirectory(Path.GetDirectoryName(ResultFile));
            File.WriteAllText(ResultFile, "RUNNING  " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\n");
            EditorApplication.LockReloadAssemblies();
            var thread = new Thread(() => Work(root, message, done)) { IsBackground = true, Name = "FwGit" };
            thread.Start();
            Debug.Log("[FwGit] コミットとプッシュをバックグラウンドで始めました(" + ResultFile + ")");
            return true;
        }

        static void Work(string root, string message, Action<bool, string> done)
        {
            var log = new StringBuilder();
            bool ok = false;
            try { ok = Steps(root, message, log); }
            catch (Exception e) { log.AppendLine("exception: " + e.Message); }
            var text = (ok ? "OK" : "FAILED") + "  " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\n" + log;
            try { File.WriteAllText(Path.Combine(root, ResultFile), text); } catch (IOException) { }
            mainThread.Enqueue(() =>
            {
                running = false;
                EditorApplication.UnlockReloadAssemblies();
                if (ok) Debug.Log("[FwGit] " + text); else Debug.LogError("[FwGit] " + text);
                done?.Invoke(ok, text);
            });
        }

        static bool Steps(string root, string message, StringBuilder log)
        {
            void Note(string line)
            {
                log.AppendLine(line);
                try { File.WriteAllText(Path.Combine(root, ResultFile), "RUNNING  " + DateTime.Now.ToString("HH:mm:ss") + "\n" + log); } catch (IOException) { }
            }

            // add (retried: a scanner holding a file makes it fail now and then)
            Result add = null;
            for (int i = 0; i < AddTries; i++)
            {
                add = Git(root, "add -A", StepLimit);
                if (add.Code == 0) break;
                Thread.Sleep(1500);
            }
            Note("add: " + (add.Code == 0 ? "ok" : "failed\n" + add.Err.Trim()));
            if (add.Code != 0) return false;

            var status = Git(root, "status --porcelain", StepLimit);
            if (status.Out.Trim().Length > 0)
            {
                string msgFile = Path.Combine(root, "Library/FwBuild/commitmsg.txt");
                File.WriteAllText(msgFile, message.Replace("\r\n", "\n"), new UTF8Encoding(false));
                var commit = Git(root, "commit -F \"" + msgFile + "\"", StepLimit);
                Note("commit: " + (commit.Code == 0 ? FirstLine(commit.Out) : "failed\n" + commit.Err.Trim() + commit.Out.Trim()));
                if (commit.Code != 0) return false;
                // every object of the new commit must be there before it is sent
                var missing = Git(root, "rev-list --objects --missing=print HEAD~1..HEAD", StepLimit);
                int n = 0;
                foreach (var l in missing.Out.Split('\n')) if (l.StartsWith("?")) n++;
                if (n > 0) { Note("commit: " + n + " objects missing; not pushed"); return false; }
            }
            else Note("commit: 変更なし");

            var branch = Git(root, "rev-parse --abbrev-ref HEAD", StepLimit).Out.Trim();
            if (branch.Length == 0 || branch == "HEAD") { Note("push: ブランチが分かりません"); return false; }
            Note("push: origin " + branch + " …");
            var push = Git(root, "push origin " + branch, PushLimit);
            Note("push: " + (push.Code == 0 ? "ok\n" + LastLines(push.Err, 3) : "failed\n" + push.Err.Trim()));
            return push.Code == 0;
        }

        sealed class Result { public int Code = -1; public string Out = "", Err = ""; }

        /// <summary>One git command; both output streams are read as they come, and the process is ended past the limit.</summary>
        static Result Git(string root, string args, TimeSpan limit)
        {
            var res = new Result();
            var psi = new ProcessStartInfo("git", args)
            {
                WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
            };
            psi.EnvironmentVariables["GIT_TERMINAL_PROMPT"] = "0";   // never wait for a password prompt
            var outB = new StringBuilder(); var errB = new StringBuilder();
            using (var p = new Process { StartInfo = psi })
            {
                p.OutputDataReceived += (s, e) => { if (e.Data != null) lock (outB) outB.AppendLine(e.Data); };
                p.ErrorDataReceived += (s, e) => { if (e.Data != null) lock (errB) errB.AppendLine(e.Data); };
                p.Start();
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
                if (!p.WaitForExit((int)limit.TotalMilliseconds))
                {
                    try { p.Kill(); } catch (InvalidOperationException) { }
                    lock (errB) errB.AppendLine("(" + limit.TotalMinutes + " 分を過ぎたので止めました)");
                    p.WaitForExit(5000);
                    res.Code = -1;
                }
                else { p.WaitForExit(); res.Code = p.ExitCode; }   // the second wait lets the readers finish
            }
            lock (outB) res.Out = outB.ToString();
            lock (errB) res.Err = errB.ToString();
            return res;
        }

        static string FirstLine(string s) { var t = s.Trim(); int k = t.IndexOf('\n'); return k < 0 ? t : t.Substring(0, k); }
        static string LastLines(string s, int n)
        {
            var lines = s.Trim().Split('\n');
            return string.Join("\n", lines, Math.Max(0, lines.Length - n), Math.Min(n, lines.Length));
        }
    }

    /// <summary>「コミットしてプッシュ」: a message box and the progress of the background push.</summary>
    public class FwGitWindow : EditorWindow
    {
        string message = "";
        Vector2 scroll;

        [MenuItem("フィールド・コマンド/Git/コミットしてプッシュ")]
        public static void Open() => GetWindow<FwGitWindow>(true, "コミットしてプッシュ").minSize = new Vector2(520, 360);

        void OnGUI()
        {
            GUILayout.Label("コミットメッセージ", EditorStyles.boldLabel);
            message = EditorGUILayout.TextArea(message, GUILayout.Height(90));
            using (new EditorGUI.DisabledScope(FwGit.Running))
                if (GUILayout.Button(FwGit.Running ? "実行中…" : "すべてコミットしてプッシュ", GUILayout.Height(32))) FwGit.CommitAndPushAsync(message);
            GUILayout.Space(6);
            GUILayout.Label("結果", EditorStyles.boldLabel);
            string text = File.Exists(FwGit.ResultFile) ? File.ReadAllText(FwGit.ResultFile) : "";
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.SelectableLabel(text, EditorStyles.wordWrappedLabel, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
        }

        void OnInspectorUpdate() => Repaint();
    }
}
