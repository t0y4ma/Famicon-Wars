# フィールド・コマンド オンライン対戦の配置手順（Nine の VM に追加する）

サーバーは Nine・軍人将棋と同じ VM（`nine.freeddns.org`）に**追加**します。既存の設定・ファイル・サービスはそのまま残ります。
WebGL クライアントは Cloudflare Pages から配信します（VM の外向き通信量を使わないため）。

## 全体の形

```
ブラウザ（Cloudflare Pages から WebGL を読み込む）
   │  wss://nine.freeddns.org/fw
   ▼
Caddy（VM・TLS 終端）
   ├─ /fw で始まる接続     ──▶ 127.0.0.1:7782   フィールド・コマンド（fw-server.service・~/fieldcommand）
   ├─ /gunjin で始まる接続 ──▶ 127.0.0.1:7778   軍人将棋（今までどおり）
   └─ それ以外             ──▶ 127.0.0.1:27777  Nine（今までどおり）
```

| | 値 |
|---|---|
| systemd のサービス | `fw-server` |
| 置き場所 | `~/fieldcommand`（初回に update-fw.sh を実行したユーザーのホーム） |
| ポート（VM の中だけ） | 7782（外には開けない） |
| 接続先 | `wss://nine.freeddns.org/fw` |
| WebGL | Cloudflare Pages（このリポジトリの `Builds/WebGL`） |

## 1. WebGL を Cloudflare Pages に登録する（初回のみ）

1. Cloudflare →「Workers & Pages」→「作成」→「Pages」→「Git に接続」
2. リポジトリ `t0y4ma/Famicon-Wars` を選ぶ
3. フレームワーク：なし／ビルドコマンド：空欄／ビルド出力ディレクトリ：`Builds/WebGL`
4. 以後は GitHub に push するたびに自動で更新されます

## 2. ゲームサーバーを VM に追加する（初回も更新も同じ）

1. GCP コンソールで VM の「SSH」を開く
2. 歯車 →「ファイルをアップロード」で `Builds/FieldCommandServer.zip` と `Deploy/update-fw.sh` を送る
3. `bash ~/update-fw.sh`

最後のログに `[FW] server started on port 7782` が出れば動いています。ログを追う：`sudo journalctl -u fw-server -f`

## 3. Caddy に振り分けを足す（初回）

`/etc/caddy/Caddyfile` の `nine.freeddns.org { ... }` に、`Deploy/Caddyfile.fw` の `handle /fw* { ... }` を **一番上に** 足します（`/gunjin*` と最後の `handle { ... }` はそのまま）。

```bash
sudo cp /etc/caddy/Caddyfile /etc/caddy/Caddyfile.bak
sudo nano /etc/caddy/Caddyfile
sudo caddy validate --config /etc/caddy/Caddyfile
sudo systemctl reload caddy
```

戻すとき：`sudo cp /etc/caddy/Caddyfile.bak /etc/caddy/Caddyfile && sudo systemctl reload caddy`

## 4. 確認

1. Nine と軍人将棋が今までどおり動く
2. Pages の URL を開き「オンライン対戦」→「部屋を作る」で4桁の番号が出る
3. 別のブラウザ（シークレットウィンドウなど）で番号を入れて「部屋に入る」→ 対戦が始まる
4. 片方のタブを閉じて開き直し、同じ番号で入ると続きから再開できる
5. `free -h` でメモリに余裕があるか（e2-micro は 1GB。3つ目のサーバーなので、足りなければスワップを追加）

手元で試すとき：エディタまたは Windows ビルドを `-server` 付きで起動し、WebGL は `index.html?server=localhost` で開くとそのサーバーにつながります。

## 更新するとき

通信の中身が変わった版では、**サーバーと WebGL を必ず一緒に**更新してください（違うと「サーバーとゲームのバージョンが違います」と出ます）。
Unity のメニュー「フィールド・コマンド → ビルド → 両方」→ push → zip を VM に送って `bash ~/update-fw.sh`。
サーバーを再起動すると進行中の部屋は消えます。

## 通信量の目安

1手 = 送信 約 30 バイト、受信 約 50 バイト（コマンド文字列＋乱数の種＋確認用ハッシュ）。途中参加・再接続のときだけ、その対戦の全手順（数 KB〜数十 KB）を一度送ります。
