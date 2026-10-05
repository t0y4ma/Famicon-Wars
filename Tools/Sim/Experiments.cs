using System;
using System.Collections.Generic;
using FamiconWars.Core;

namespace FamiconWars.Sim
{
    public sealed class Matchup { public string Name; public Func<AiProfile> A, B; }

    /// <summary>Edit freely: this folder is outside Assets, so changing it never makes Unity recompile.</summary>
    public static class Experiments
    {
        static AiProfile L(int lv) => AiProfile.ForLevel(lv);
        static AiProfile F4(Action<AiProfile> f) { var p = L(4); p.LookaheadFollow = 1.0f; f(p); return p; }
        static AiProfile Old3() { var p = L(3); p.Features &= ~(AiFeature.Economy | AiFeature.Formation); return p; }
        /// <summary>Lv3 as it was before the slot model and rotation (previous release).</summary>
        static AiProfile Prev3() { var p = L(3); p.EcoSlotModel = false; p.RotateWeak = false; return p; }
        static AiProfile Eco(Action<AiProfile> f) { var p = L(3); p.EcoSave = false; p.EcoCrowd = 99; p.EcoQuality = 0; f(p); return p; }

        public static List<Matchup> Get(string name)
        {
            var list = new List<Matchup>();
            void Add(string n, Func<AiProfile> a, Func<AiProfile> b) => list.Add(new Matchup { Name = n, A = a, B = b });
            switch (name)
            {
                case "eco":
                    Add("rotate2 only vs prev3", () => { var p = L(3); p.EcoSlotModel = false; return p; }, Prev3);
                    Add("tilt+jam+rotate2 vs prev3", () => L(3), Prev3);
                    break;
                case "l4":
                    Add("f1.0 sharpen2 vs L3", () => F4(p => p.EcoSharpen = 2), () => L(3));
                    Add("f1.0 sharpen3 vs L3", () => F4(p => p.EcoSharpen = 3), () => L(3));
                    Add("f0.8 sharpen2 vs L3", () => { var p = L(4); p.EcoSharpen = 2; return p; }, () => L(3));
                    Add("L3 sharpen2 vs L3", () => { var p = L(3); p.EcoSharpen = 2; return p; }, () => L(3));
                    Add("f1.0 sharpen2 vs L4 f1.0", () => F4(p => p.EcoSharpen = 2), () => F4(p => { }));
                    break;
                case "truck":
                    Add("L3 truck vs L3 notruck", () => L(3), () => { var p = L(3); p.TransportTactics = false; return p; });
                    Add("L4 truck vs L4 notruck", () => L(4), () => { var p = L(4); p.TransportTactics = false; return p; });
                    break;
                case "capture":
                    Add("L3 latecap vs L3 off", () => L(3), () => { var p = L(3); p.EcoLateCapRange = 0; return p; });
                    Add("L3 joincap vs L3 off", () => L(3), () => { var p = L(3); p.JoinCapture = false; return p; });
                    Add("L4 both vs L4 neither", () => L(4), () => { var p = L(4); p.EcoLateCapRange = 0; p.JoinCapture = false; return p; });
                    break;
                case "join":
                    Add("L3 joincap vs L3 off", () => L(3), () => { var p = L(3); p.JoinCapture = false; return p; });
                    Add("L3 afteract vs L3 anytime", () => L(3), () => { var p = L(3); p.JoinAfterAct = false; return p; });
                    break;
                case "mirror":
                    Add("L3 vs L3", () => L(3), () => L(3));
                    break;
                case "air":
                    Add("L3 strategic vs L3 off", () => L(3), () => { var p = L(3); p.StrategicBuy = false; return p; });
                    break;
                case "block":
                    Add("L3 keep vs L3 off", () => L(3), () => { var p = L(3); p.KeepBarricade = false; return p; });
                    break;
                case "top":
                    Add("L4 vs L3", () => L(4), () => L(3));
                    Add("L3 vs L2", () => L(3), () => L(2));
                    break;
                case "levels":
                    for (int a = 1; a <= 4; a++) for (int b = a + 1; b <= 4; b++) { int x = a, y = b; Add($"L{y} vs L{x}", () => L(y), () => L(x)); }
                    break;
                default:
                    throw new ArgumentException("unknown experiment " + name);
            }
            return list;
        }
    }
}
