using System.Collections.Generic;
using System.IO;
using System.Text;
using FamiconWars.Core;
using UnityEditor;

namespace FamiconWars.EditorTools
{
    /// <summary>Reading and writing the CSV files in Resources/Data from the editor windows.</summary>
    public static class FwData
    {
        public const string Dir = "Assets/Resources/Data";

        public static string PathOf(string name) => Dir + "/" + name + ".csv";
        public static string Read(string name) => File.Exists(PathOf(name)) ? File.ReadAllText(PathOf(name)) : "";

        public static GameData LoadGame(Dictionary<string, string> overrides = null)
        {
            string R(string n) => overrides != null && overrides.TryGetValue(n, out var t) ? t : Read(n);
            var g = GameData.Load(R("units"), R("terrains"), R("damage"), R("rules"), R("economy"), R("production"));
            if (overrides == null) FamiconWars.Game.Labels.Register(g);
            return g;
        }

        public static void Write(string name, string text)
        {
            var path = PathOf(name);
            File.WriteAllText(path, text, new UTF8Encoding(false));
            AssetDatabase.ImportAsset(path);
        }

        /// <summary>The file as a header and rows of cells (cells kept as text, in file order).</summary>
        public static List<string[]> Table(string text, out string[] header)
        {
            header = null;
            var rows = new List<string[]>();
            foreach (var raw in text.Replace("\r", "").Split('\n'))
            {
                var line = raw.TrimStart('﻿');
                if (line.Trim().Length == 0) continue;
                var cells = line.Split(',');
                if (header == null) { header = cells; continue; }
                if (cells.Length < header.Length)
                {
                    var full = new string[header.Length];
                    for (int i = 0; i < full.Length; i++) full[i] = i < cells.Length ? cells[i] : "";
                    cells = full;
                }
                rows.Add(cells);
            }
            return rows;
        }

        public static string Join(string[] header, List<string[]> rows)
        {
            var sb = new StringBuilder();
            sb.Append(string.Join(",", header)).Append('\n');
            foreach (var r in rows) sb.Append(string.Join(",", r)).Append('\n');
            return sb.ToString();
        }

        /// <summary>A cell may not hold the separator.</summary>
        public static string Clean(string v) => (v ?? "").Replace(",", "、").Replace("\n", " ").Trim();
    }
}
