# KotorinToys セキュリティ診断・改善実装レポート

診断日: 2026-10-15。対象: 本体 `1252223`（feature/full-refactoring）と `tools/` の独立7リポジトリ。開始時の作業ツリーは全件clean。親子のGit境界を維持する。

## 実施範囲と結果

追跡ソース・設定・依存マニフェストの静的診断、配布取得/検証/インストーラー起動、パス境界、IPC、保存データ、秘密パターン検査を実施した。本体単独で高/致命的の確定事項は見つからなかった。これは安全性の保証や侵入テストの完了を意味しない。

|対象|主要結果|個別レポート|
|---|---|---|
|CapchunScreen|高: 通常文字記録OFFでもShift付き文字を履歴へ保存する問題を修正、154検査成功。保護入力判定は別途設計。|[診断](../../../tools/CapchunScreen/dev/research/security-audit-2026-10-15/report.md)|
|ChulipVideo|中: 同一利用者の管理pipe応答待ちによる停止・資源上限不足。|[診断](../../../tools/ChulipVideo/dev/research/security-audit-2026-10-15/report.md)|
|ChunapTool|中: 共通管理pipeの資源上限不足。|[診断](../../../tools/ChunapTool/dev/research/security-audit-2026-10-15/report.md)|
|LiveTextMarker|中: 共通管理pipe。NuGet既知脆弱性報告0。|[診断](../../../tools/LiveTextMarker/dev/research/security-audit-2026-10-15/report.md)|
|ShortcutKeyCounter|保守注意: RustSecの保守終了8件と、Windows依存経路外glib 1件。|[診断](../../../tools/ShortcutKeyCounter/dev/research/security-audit-2026-10-15/report.md)|
|SmartPaste|中: 共通管理pipe。AI画像送信の境界・ツール禁止の強制は要設計。|[診断](../../../tools/SmartPaste/dev/research/security-audit-2026-10-15/report.md)|
|SSCapture|中: 共通管理pipe。NuGet既知脆弱性報告0。|[診断](../../../tools/SSCapture/dev/research/security-audit-2026-10-15/report.md)|

## 確認した防御

- `src/KotorinToys/ReleaseDownloader.Catalog.cs:29`: HTTPS、標準ポート、userinfoなし、GitHubの許可ホストを検証。リダイレクトも各段で検証する。
- `src/KotorinToys/ReleaseDownloader.Transfer.cs:35`: ストリーム中のサイズ上限とSHA-256照合、完了前ファイル名分離。
- `src/KotorinToys/InstallerAdapter.cs:24`: 検証済みインストーラーの書換・削除を禁止するファイルハンドルを実行完了まで保持。
- `src/KotorinToys/FfmpegInstaller.cs:115`: 固定アーカイブ、サイズ/ハッシュ検証、項目数/展開容量/パス/リンク検査、既知ファイルのみ展開。
- `src/KotorinToys/PathSafety.cs:8`: 相対パスの正規化、ルート外と既存reparse pointの逸脱を拒否。

## 指摘・改善案

|ID|重大度・成立条件|証拠|改善・今回の扱い|
|---|---|---|---|
|KT-01|中。同一Windows利用者の悪意あるプロセスが管理pipeを偽装して大量の改行なし応答を返せる場合、2秒期限内でも大きなメモリ消費が可能。権限昇格や遠隔RCEを実証したものではない。|`src/KotorinToys/ToolManager.Transport.cs:25` の `ReadLineAsync` に長さ上限なし。|共通契約で要求/応答を例8KiBに制限し、全ツールと互換試験する。今回は中の改善案として保留。|
|KT-02|低/運用。アプリ本体・ローカル配布台帳を変更可能な主体は期待ハッシュも変更できる。|`src/KotorinToys/ReleaseDownloader.Catalog.cs:10`、`src/KotorinToys/InstallerAdapter.cs:19`。|現在の固定ハッシュは通信・配布事故への防御。公開署名/鍵管理/更新元の信頼方針は重要判断が伴うため保留。|

本体製品のcsprojに外部PackageReferenceはない。SDK、.NETランタイム、Windows、WebView、FFmpeg等の外部バイナリを安全と断定しない。NuGet監査は依存のある2ツールで実行し、RustはCargo.lockの489 registry項目をOSVに照会した（各レポート参照）。

追跡テキスト113ファイルに秘密鍵PEM、GitHub token、AWS access-key ID、OpenAI token形式の値非表示検査を実施し一致0。子を合わせ661ファイル。未追跡/無視対象、Git過去履歴、一般パスワード、外部資格情報ストアは未検査。`dev/private`・`dev/temp`を除外した。

## 改善実装・検証状況

- [x] 全8プロジェクトの診断レポート作成、参照先・差分検査。
- [x] CapchunScreenの高リスクを既存v0.1.0計画へ記録し、合成回帰で修正前9失敗→修正後全154成功。警告0/エラー0。詳細は子レポートを参照。
- 中/低の項目、署名/鍵管理、保護入力の新ポリシー、配布/本番更新は未実施。

文書だけの診断フェーズでは製品ビルド・GUIを形式的に実行しない。ネットワーク侵入、別ユーザー/リモートpipe、実パスワード、実録画、インストール、配布/公開は未試験。

## 子コミットの採用

診断・修正の完了後、親indexが診断開始時の子HEADと一致し、そこからの差分が今回の診断/CapchunScreen修正だけであること、全7子の作業ツリーがcleanであることを確認した。次のgitlinkだけを更新する。個別レポートの「親gitlink変更なし」は子内部の診断/修正フェーズ時点の記録であり、採用更新は本節で記録する。

|子|開始時→採用コミット|
|---|---|
|CapchunScreen|`a5fc6d9` → `af7ea0c`|
|ChulipVideo|`0c053f3` → `74043aa`|
|ChunapTool|`f116668` → `58ab90c`|
|LiveTextMarker|`6f9e4d1` → `2fb56fa`|
|ShortcutKeyCounter|`88aea33` → `e8ecf77`|
|SmartPaste|`79f3c4c` → `14e8685`|
|SSCapture|`4bdc693` → `5f44d3d`|

作業中に別タスクが追加・変更した本体の.gitmodules/catalog/src/tests/新規HttpSelectorは含めない。push・配布・導入は行わない。
