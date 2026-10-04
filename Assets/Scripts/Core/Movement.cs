using System;
using System.Collections.Generic;

namespace FamiconWars.Core
{
    public sealed class MoveResult
    {
        /// <summary>Tile index -> total move cost to reach it.</summary>
        public readonly Dictionary<int, int> Cost = new Dictionary<int, int>();
        /// <summary>Tile index -> previous tile index on the cheapest path.</summary>
        public readonly Dictionary<int, int> Prev = new Dictionary<int, int>();
        /// <summary>Tiles the unit may only end on (boarding a lander on a beach).</summary>
        public readonly HashSet<int> EndOnly = new HashSet<int>();
        public int Start;

        public bool Reaches(int index) => Cost.ContainsKey(index);

        public List<int> PathTo(int index)
        {
            var path = new List<int>();
            if (!Cost.ContainsKey(index)) return path;
            int cur = index;
            path.Add(cur);
            while (cur != Start && Prev.TryGetValue(cur, out var p)) { cur = p; path.Add(cur); }
            path.Reverse();
            return path;
        }
    }

    public enum StopKind { None, Empty, Join, Load }

    public static class Movement
    {
        static readonly int[] DX = { 1, -1, 0, 0 };
        static readonly int[] DY = { 0, 0, 1, -1 };

        /// <summary>Dijkstra over tile costs, limited by min(move, fuel). Enemy units block; friendly units can be passed.</summary>
        public static MoveResult Reachable(GameState s, UnitState u)
        {
            var d = s.Def(u);
            var res = new MoveResult { Start = s.Index(u.X, u.Y) };
            int budget = Math.Min(d.Move, u.Fuel);
            res.Cost[res.Start] = 0;
            var open = new List<int> { res.Start };
            var closed = new HashSet<int>();
            while (open.Count > 0)
            {
                int best = 0;
                for (int i = 1; i < open.Count; i++) if (res.Cost[open[i]] < res.Cost[open[best]]) best = i;
                int cur = open[best];
                open.RemoveAt(best);
                if (!closed.Add(cur)) continue;
                if (res.EndOnly.Contains(cur)) continue;
                int cx = cur % s.Width, cy = cur / s.Width;
                for (int k = 0; k < 4; k++)
                {
                    int nx = cx + DX[k], ny = cy + DY[k];
                    if (!s.InBounds(nx, ny)) continue;
                    int ni = s.Index(nx, ny);
                    bool endOnly;
                    int step = StepCost(s, u, d, nx, ny, out endOnly);
                    if (step < 0) continue;
                    int nc = res.Cost[cur] + step;
                    if (nc > budget) continue;
                    if (res.Cost.TryGetValue(ni, out var old) && old <= nc) continue;
                    res.Cost[ni] = nc;
                    res.Prev[ni] = cur;
                    if (endOnly) res.EndOnly.Add(ni); else res.EndOnly.Remove(ni);
                    open.Add(ni);
                }
            }
            return res;
        }

        static int StepCost(GameState s, UnitState u, UnitDef d, int x, int y, out bool endOnly)
        {
            endOnly = false;
            var occ = s.UnitAt(x, y);
            if (occ != null && occ.Army != u.Army) return -1;
            int cost = s.TerrainAt(x, y).Cost[(int)d.MoveClass];
            if (cost < 0)
            {
                // Ground units may step onto a beach/port only to board a friendly transport there.
                // (only ships: a helicopter over water cannot be boarded — infantry cannot walk there)
                if (occ != null && d.Domain == Domain.Ground && s.Def(occ).Domain == Domain.Sea && CanBoard(s, u, occ)) { endOnly = true; return 1; }
                return -1;
            }
            return cost;
        }

        public static bool CanBoard(GameState s, UnitState u, UnitState transport)
        {
            if (transport == null || transport.Id == u.Id || transport.Army != u.Army) return false;
            var td = s.Def(transport);
            if (!td.CanCarry(s.Def(u))) return false;
            if (u.Cargo.Count > 0) return false;
            if (transport.Cargo.Count >= td.CargoCapacity) return false;
            if (td.Domain == Domain.Sea && !IsShoreForShip(s, transport.X, transport.Y)) return false;
            // a helicopter or truck is boarded by walking onto its tile, so the tile must be passable for the passenger
            if (td.Domain != Domain.Sea && s.TerrainAt(transport.X, transport.Y).Cost[(int)s.Def(u).MoveClass] < 0) return false;
            return true;
        }

        public static bool IsShoreForShip(GameState s, int x, int y)
        {
            var id = s.TerrainAt(x, y).Id;
            return id == "PORT" || id == "BEACH";
        }

        public static bool CanJoin(GameState s, UnitState u, UnitState other)
        {
            if (other == null || other.Id == u.Id || other.Army != u.Army || other.Type != u.Type) return false;
            if (u.Cargo.Count > 0 || other.Cargo.Count > 0) return false;
            return u.Count <= 9 || other.Count <= 9;
        }

        /// <summary>What happens if the unit ends its move on this tile.</summary>
        public static StopKind StopAt(GameState s, UnitState u, int x, int y)
        {
            var occ = s.UnitAt(x, y);
            if (occ == null || occ.Id == u.Id) return StopKind.Empty;
            if (CanBoard(s, u, occ)) return StopKind.Load;
            if (CanJoin(s, u, occ)) return StopKind.Join;
            return StopKind.None;
        }

        public static int Distance(int x1, int y1, int x2, int y2) => Math.Abs(x1 - x2) + Math.Abs(y1 - y2);
    }
}
