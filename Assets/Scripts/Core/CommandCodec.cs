using System;
using System.Globalization;

namespace FamiconWars.Core
{
    /// <summary>
    /// Compact text form of a Command for the network and for logs (a few dozen bytes each).
    /// U = unit command, P = produce, R = resupply all, E = end phase, S = surrender, A = match ended by the host.
    /// </summary>
    public static class CommandCodec
    {
        public static string Encode(Command c)
        {
            int a = (int)c.Army;
            switch (c)
            {
                case UnitCommand u:
                    return $"U,{a},{u.UnitId},{u.ToX},{u.ToY},{(int)u.Action},{u.TargetId},{u.CargoId},{u.DropX},{u.DropY}";
                case ProduceCommand p: return $"P,{a},{p.X},{p.Y},{p.UnitType}";
                case ResupplyAllCommand _: return $"R,{a}";
                case EndPhaseCommand _: return $"E,{a}";
                case SurrenderCommand _: return $"S,{a}";
                case AbortCommand _: return $"A,{a}";
            }
            throw new ArgumentException("unknown command " + c.GetType().Name);
        }

        /// <summary>Returns null for anything malformed (never trust the wire).</summary>
        public static Command Decode(string text)
        {
            if (string.IsNullOrEmpty(text) || text.Length > 128) return null;
            var p = text.Split(',');
            try
            {
                int I(int k) => int.Parse(p[k], NumberStyles.Integer, CultureInfo.InvariantCulture);
                if (p.Length < 2) return null;
                int armyNo = I(1);
                if (armyNo != 0 && armyNo != 1) return null;
                var army = (Army)armyNo;
                switch (p[0])
                {
                    case "U":
                        if (p.Length != 10) return null;
                        int act = I(5);
                        if (!Enum.IsDefined(typeof(UnitAction), act)) return null;
                        return new UnitCommand { Army = army, UnitId = I(2), ToX = I(3), ToY = I(4), Action = (UnitAction)act, TargetId = I(6), CargoId = I(7), DropX = I(8), DropY = I(9) };
                    case "P":
                        if (p.Length != 5) return null;
                        return new ProduceCommand { Army = army, X = I(2), Y = I(3), UnitType = p[4] };
                    case "R": return new ResupplyAllCommand { Army = army };
                    case "E": return new EndPhaseCommand { Army = army };
                    case "S": return new SurrenderCommand { Army = army };
                    case "A": return new AbortCommand { Army = army };
                }
            }
            catch (FormatException) { }
            catch (OverflowException) { }
            return null;
        }

        /// <summary>Order-sensitive digest of everything that matters, to detect a client drifting from the server.</summary>
        public static int Hash(GameState s)
        {
            unchecked
            {
                int h = 17;
                void Add(int v) { h = h * 31 + v; }
                Add(s.Day); Add((int)s.Active); Add(s.Funds[0]); Add(s.Funds[1]); Add(s.GameOver ? 1 : 0); Add(s.NextUnitId);
                for (int i = 0; i < s.Owner.Length; i++) { Add((int)s.Owner[i]); Add(s.CaptureProgress[i]); }
                foreach (var u in s.Units) { Add(u.Id); Add(u.Type); Add(u.X); Add(u.Y); Add(u.Hp); Add(u.Fuel); Add(u.Ammo); Add(u.Acted ? 1 : 0); Add(u.CarriedBy); }
                return h;
            }
        }
    }
}
