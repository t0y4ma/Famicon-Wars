using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FamiconWars.Game
{
    /// <summary>"あそびかた": the rules and controls, one topic at a time (list on the left, text on the right).</summary>
    public partial class GameHud
    {
        RectTransform howRoot;
        TextMeshProUGUI howHeading, howBody;
        readonly List<Button> howTabs = new List<Button>();

        static readonly (string title, string body)[] HowTopics =
        {
            ("勝ち負け",
             "レッド軍とブルー軍が交互に手番を行う、ターン制の戦略ゲームです。レッド軍とブルー軍の手番がひとまわりすると1日が終わります。\n\n" +
             "<b>勝ち</b>\n" +
             "・相手の基地<color=#A3AC92>(H)</color>を占領する\n" +
             "・相手の部隊をすべて倒す\n" +
             "・相手が降伏する\n\n" +
             "<b>引き分け</b>\n" +
             "・255日を過ぎても決着がつかないとき\n" +
             "・オンラインで部屋主が対局を終了したとき"),

            ("手番の流れ",
             "1. 手番の始めに、自軍の施設の数に応じて資金が入ります(基地 4,000・都市 1,000・空港と港 2,000)。\n" +
             "2. 部隊を1つずつ動かします。どの部隊も1手番に1回だけ行動でき、行動した部隊は暗くなります。\n" +
             "3. 資金があれば、施設で部隊を生産します。\n" +
             "4. 「手番終了」で相手の手番になります。全部の部隊を動かさなくてもかまいません。\n\n" +
             "画面上の帯に、いまの軍・日数・資金・収入・部隊数が出ています。"),

            ("操作",
             "<b>マウス</b>\n" +
             "・左クリック: 部隊を選ぶ → 移動先を選ぶ → 出てきたメニューで行動を選ぶ\n" +
             "・右クリック または Esc: 取り消し\n" +
             "・空いている自軍の施設をクリック: 生産\n\n" +
             "<b>画面</b>\n" +
             "・ホイール: 拡大・縮小　　右ボタンを押したままドラッグ / 画面の端にカーソル: 移動\n" +
             "・右上の「リセット」: 最初の位置と倍率に戻す\n\n" +
             "<b>そのほか</b>\n" +
             "・戦闘の場面はクリックか Space でとばせます\n" +
             "・COM どうしの観戦中は Space で一時停止\n" +
             "・マウスを乗せたマスの地形と部隊の情報が左下に出ます\n" +
             "・COM の戦法(速攻・物量・精鋭)は対戦設定や部屋の BOT 選びで決められます。「ランダム」にすると対戦中も明かされません"),

            ("部隊と移動",
             "・部隊は最大10機で、機数が減るほど攻撃も弱くなります。\n" +
             "・歩兵・車両・航空機・艦船で、通れる地形と進みにくさが違います。山と川は車両では通れず、海は艦船と航空機だけが通れます。\n" +
             "・敵の部隊は通り抜けられません。味方の部隊は通れます。\n" +
             "・移動すると燃料を使います。航空機と艦船は燃料が0になると失われます(航空機は自軍の空港にいれば無事です)。\n" +
             "・輸送車とヘリは歩兵を、揚陸艦は地上の部隊を2つまで運べます。\n" +
             "・同じ種類の部隊どうしは合流でき、10機を超えた分は失われます。\n" +
             "・部隊は1つの軍で50まで(運ばれている部隊も数えます)。"),

            ("戦闘",
             "・攻撃すると、隣のマスにいる相手は同時に撃ち返します(反撃)。どちらも戦闘前の機数で撃ち合います。\n" +
             "・自走砲・対空ミサイル・戦艦は離れたマスを撃てる<b>間接攻撃</b>の部隊です。反撃を受けませんが、移動した手番には撃てず、隣の敵には撃てません。\n" +
             "・森・山・都市などにいる部隊は受けるダメージが減ります(航空機には地形の守りがありません)。\n" +
             "・攻撃には弾を使います。弾がなくなると攻撃も反撃もできません。\n" +
             "・攻撃の前に、与えるダメージと受けるダメージの見込みが表示されます。"),

            ("占領と施設",
             "・歩兵と工兵は、敵や中立の施設に止まって「占領」できます。\n" +
             "・占領するたびに、乗っている機数だけ進み具合が増え、合計20で自軍の施設になります(10機なら2回)。途中で離れると0に戻ります。合流しても進み具合は残ります。\n" +
             "・基地を占領できるのは歩兵だけです(工兵はできません)。相手の基地を占領すると勝ちです。\n\n" +
             "<b>施設の役割</b>\n" +
             "・基地・工場・都市: 地上の部隊を生産、地上の部隊を補給\n" +
             "・空港: 航空機を生産・補給　　港: 艦船を生産・補給\n" +
             "・生産できるのは、自軍の基地から2マス以内にある施設だけです。"),

            ("生産と補給",
             "<b>生産</b>\n" +
             "空いている自軍の施設をクリックして部隊を選びます。生産した部隊はその手番には動けません。\n\n" +
             "<b>全補(ぜんぽ)</b>\n" +
             "1手番に1回、まだ行動していない部隊をまとめて補給します。\n" +
             "・自軍の施設にいる部隊: 2機ぶん回復し、燃料と弾が満タンになります\n" +
             "・補給車の隣にいる地上の部隊: 燃料と弾が満タンになります\n" +
             "補給した部隊はその手番には行動できません。費用は補給した量に応じてかかります。"),

            ("オンライン対戦",
             "・「部屋を作る」で部屋を作り、4桁の番号を相手に伝えます。公開にすると、ロビーの一覧から誰でも入れます。\n" +
             "・部屋にはレッド軍とブルー軍の席があり、席にいない人は観戦になります。\n" +
             "・部屋主は、マップ・空いた席の BOT・公開範囲・ゲーム速度を決めて対戦を始めます。部屋主を他の人に譲ることもできます。\n" +
             "・対戦中に通信が切れても、同じブラウザで同じ番号に入り直せば続きから遊べます。\n" +
             "・対戦が終わると、そのまま席やマップを決め直して次の対戦ができます。\n" +
             "・終わった対戦は「対戦の記録」から一手ずつ振り返れます。"),
        };

        void BuildHowTo()
        {
            howRoot = Panel("HowTo", canvasRt, Backdrop);
            Fill(howRoot, 0, 0, 0, 0);
            var title = Label("Heading", howRoot, SizeTitle, Paper, TextAlignmentOptions.MidlineLeft, true);
            title.text = "あそびかた";
            Pin(title.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(96, -48), new Vector2(600, 60), pivot: new Vector2(0, 1));

            for (int i = 0; i < HowTopics.Length; i++)
            {
                int k = i;
                var b = MakeButton("Topic" + i, howRoot, HowTopics[i].title, false, () => ShowHowTopic(k));
                Pin((RectTransform)b.transform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(96, -140 - i * 76), new Vector2(320, 64), pivot: new Vector2(0, 1));
                howTabs.Add(b);
            }

            var page = Panel("Page", howRoot, Plate);
            Pin(page, new Vector2(0, 1), new Vector2(1, 1), new Vector2(456, -140), new Vector2(-552, 800), pivot: new Vector2(0, 1), stretchX: true);
            page.offsetMin = new Vector2(456, page.offsetMin.y); page.offsetMax = new Vector2(-96, page.offsetMax.y);
            Edge(page, Brass);
            howHeading = Label("Title", page, SizeTitle - 4, Brass, TextAlignmentOptions.TopLeft, true);
            Pin(howHeading.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -32), new Vector2(-96, 52), pivot: new Vector2(0.5f, 1), stretchX: true);
            howBody = Label("Body", page, SizeBody, Paper, TextAlignmentOptions.TopLeft, false);
            howBody.textWrappingMode = TextWrappingModes.Normal;
            howBody.overflowMode = TextOverflowModes.Overflow;
            howBody.richText = true;
            howBody.lineSpacing = 12;
            Pin(howBody.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -108), new Vector2(-96, 660), pivot: new Vector2(0.5f, 1), stretchX: true);

            var back = MakeButton("Back", howRoot, "戻る", false, () => ShowTitle());
            Pin((RectTransform)back.transform, new Vector2(0, 0), new Vector2(0, 0), new Vector2(96, 56), new Vector2(200, 64), pivot: new Vector2(0, 0));
            howRoot.gameObject.SetActive(false);
        }

        public bool HowToVisible => howRoot != null && howRoot.gameObject.activeSelf;

        public void ShowHowTo()
        {
            ShowOnly(howRoot, howTabs.Count > 0 ? howTabs[0] : null);
            ShowHowTopic(0);
        }

        void ShowHowTopic(int k)
        {
            howHeading.text = HowTopics[k].title;
            howBody.text = HowTopics[k].body;
            for (int i = 0; i < howTabs.Count; i++) StyleButton(howTabs[i], i == k);
        }
    }
}
