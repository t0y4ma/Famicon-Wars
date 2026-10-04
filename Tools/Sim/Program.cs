using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FamiconWars.Core;

namespace FamiconWars.Sim
{
    /// <summary>
    /// dotnet run -c Release -- <experiment> [seeds] [maxDays]
    /// Writes progress and a summary to out/<experiment>.txt (and the console).
    /// </summary>
    public static class Program
    {
        public static string Root;
        static GameData data;
        static string mapText;
        static readonly object Gate = new object();
        static StreamWriter log;

        public struct Result { public Army Winner; public int Day; public string Reason; public int[] Built, Inf, Hi; public double PressSum; public int PressN; }

        public static int Main(string[] args)
        {
            Root = AppContext.BaseDirectory;
            // find the project root (folder that contains Assets) from the build output
            var dir = new DirectoryInfo(Root);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Assets"))) dir = dir.Parent;
            if (dir == null) { Console.WriteLine("Assets folder not found"); return 1; }
            var data0 = Path.Combine(dir.FullName, "Assets/Resources/Data");
            data = GameData.Load(File.ReadAllText(Path.Combine(data0, "units.csv")), File.ReadAllText(Path.Combine(data0, "terrains.csv")),
                File.ReadAllText(Path.Combine(data0, "damage.csv")), File.ReadAllText(Path.Combine(data0, "rules.csv")));
            mapText = File.ReadAllText(Path.Combine(dir.FullName, "Assets/Resources/Maps/map01.txt"));

            string exp = args.Length > 0 ? args[0] : "eco";
            int seeds = args.Length > 1 ? int.Parse(args[1]) : 4;
            int maxDays = args.Length > 2 ? int.Parse(args[2]) : 50;
            var outDir = Path.Combine(dir.FullName, "Tools/Sim/out");
            Directory.CreateDirectory(outDir);
            var path = Path.Combine(outDir, exp + ".txt");
            log = new StreamWriter(new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite)) { AutoFlush = true };
            var sw = Stopwatch.StartNew();
            Log($"# {exp}  seeds={seeds} maxDays={maxDays}  started {DateTime.Now:HH:mm:ss}");
            var matchups = Experiments.Get(exp);
            var summary = new List<string>();
            foreach (var m in matchups)
            {
                // each seed is played twice with sides swapped; games run in parallel
                var jobs = new List<(uint seed, bool aRed)>();
                for (uint sd = 1; sd <= seeds; sd++) { jobs.Add((sd, true)); jobs.Add((sd, false)); }
                int aw = 0, bw = 0, dr = 0; int[] built = new int[2], inf = new int[2], hi = new int[2];
                Parallel.ForEach(jobs, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 2) }, j =>
                {
                    var r = j.aRed ? Play(m.A(), m.B(), j.seed, maxDays) : Play(m.B(), m.A(), j.seed, maxDays);
                    int ai = j.aRed ? 0 : 1;
                    lock (Gate)
                    {
                        string w = r.Winner == Army.None ? "draw" : (int)r.Winner == ai ? "A" : "B";
                        if (w == "A") aw++; else if (w == "B") bw++; else dr++;
                        built[0] += r.Built[ai]; built[1] += r.Built[1 - ai]; inf[0] += r.Inf[ai]; inf[1] += r.Inf[1 - ai]; hi[0] += r.Hi[ai]; hi[1] += r.Hi[1 - ai];
                        Log($"  press≈{(r.PressN > 0 ? r.PressSum / r.PressN : 0):F2} [{m.Name}] seed{j.seed} A={(j.aRed ? "赤" : "青")} -> {w}  day{r.Day} {r.Reason}  built A{r.Built[ai]}(歩{r.Inf[ai]}) B{r.Built[1 - ai]}(歩{r.Inf[1 - ai]})");
                    }
                });
                var line = $"{m.Name}: A {aw} - {bw} B (draw {dr})   built A{built[0]}(歩{inf[0]} 高級{hi[0]}) B{built[1]}(歩{inf[1]} 高級{hi[1]})";
                summary.Add(line);
                Log("= " + line);
            }
            Log($"# done in {sw.Elapsed.TotalSeconds:F0}s");
            foreach (var l in summary) Log("SUMMARY " + l);
            log.Dispose();
            return 0;
        }

        static void Log(string s) { lock (Gate) { Console.WriteLine(s); log.WriteLine(s); } }

        public static Result Play(AiProfile red, AiProfile blue, uint seed, int maxDays)
        {
            var s = GameState.Create(data, MapDef.Parse(mapText, data), seed);
            RulesEngine.StartGame(s);
            var ais = new[] { new AiPlayer(Army.Red, red, seed * 7 + 1), new AiPlayer(Army.Blue, blue, seed * 13 + 5) };
            var res = new Result { Winner = Army.None, Built = new int[2], Inf = new int[2], Hi = new int[2] };
            int commands = 0;
            while (!s.GameOver && s.Day <= maxDays && commands < 60000)
            {
                var ai = ais[(int)s.Active];
                var c = ai.Next(s);
                var r = RulesEngine.Apply(s, c);
                commands++;
                if (!r.Ok) { ai.Rejected(c); continue; }
                if (c is ProduceCommand pc) { res.PressSum += ai.Profile.LastPress; res.PressN++; res.Built[(int)pc.Army]++; if (pc.UnitType == "INF") res.Inf[(int)pc.Army]++; if (data.Unit(pc.UnitType).Price >= 5000) res.Hi[(int)pc.Army]++; }
                foreach (var e in r.Events) if (e is GameOverEvent go) res.Reason = go.Reason;
                foreach (var u in s.Units)
                    if (!u.IsCarried && s.TerrainAt(u.X, u.Y).Cost[(int)s.Def(u).MoveClass] < 0)
                        res.Reason = "!! " + s.Def(u).Id + " on " + s.TerrainAt(u.X, u.Y).Id;
            }
            res.Day = s.Day;
            res.Winner = s.Winner;
            if (!s.GameOver)
            {
                long[] v = new long[2];
                foreach (var u in s.Units) v[(int)u.Army] += (long)s.Def(u).Price * u.Hp / 100;
                for (int i = 0; i < s.Owner.Length; i++) if (s.Owner[i] != Army.None) v[(int)s.Owner[i]] += 2000;
                res.Winner = v[0] == v[1] ? Army.None : v[0] > v[1] ? Army.Red : Army.Blue;
                res.Reason = "判定(" + v[0] + " vs " + v[1] + ")";
            }
            return res;
        }
    }
}
