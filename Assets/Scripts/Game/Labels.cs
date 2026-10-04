using System.Collections.Generic;

namespace FamiconWars.Game
{
    /// <summary>Display names. Placeholder for the language table described in the design doc (7.1).</summary>
    public static class Labels
    {
        static readonly Dictionary<string, string> ja = new Dictionary<string, string>
        {
            { "unit.inf", "歩兵" }, { "unit.mech", "戦闘工兵" }, { "unit.tank_a", "戦車A" }, { "unit.tank_b", "戦車B" },
            { "unit.apc", "装甲輸送車" }, { "unit.art_a", "自走砲A" }, { "unit.art_b", "自走砲B" }, { "unit.aam", "対空ミサイル" },
            { "unit.aag", "高射砲" }, { "unit.sup", "補給車" }, { "unit.ftr_a", "戦闘機A" }, { "unit.ftr_b", "戦闘機B" },
            { "unit.bmb", "爆撃機" }, { "unit.heli", "輸送ヘリ" }, { "unit.bship", "戦艦" }, { "unit.lander", "揚陸艦" },
            { "terrain.hq", "基地" }, { "terrain.factory", "工場" }, { "terrain.city", "都市" }, { "terrain.airport", "空港" },
            { "terrain.port", "港" }, { "terrain.road", "道路" }, { "terrain.bridge", "橋" }, { "terrain.plain", "平地" },
            { "terrain.forest", "森" }, { "terrain.mountain", "山" }, { "terrain.lake", "湖" }, { "terrain.river", "川" },
            { "terrain.beach", "砂浜" }, { "terrain.sea", "海" },
            { "army.Red", "レッド軍" }, { "army.Blue", "ブルー軍" }, { "army.None", "中立" },
        };

        public static string Get(string key) => ja.TryGetValue(key, out var v) ? v : key;
        public static string Army(FamiconWars.Core.Army a) => Get("army." + a);
    }
}
