# セキュリティ点検と公開前検査

本体と収録ツールは独立したGit・版・公開先を持つ。点検済みは安全性の保証や未知の脆弱性がないことを意味しない。今回の範囲・実測・未解決事項は[v0.1.7報告](../dev/history/v0.1.7/v0.1.7-imp.md)を参照する。

新規の標準Inno製品は、検証した定義を処理ごとに明示して扱う。[定義で追加する契約と信頼境界](development/definition-managed-products.md)を参照する。固定所有者・製品名・AppId・版・容量・ハッシュの照合は維持し、未知IDのグローバル許可、任意URL、未知MSIX署名の許可へ広げない。

## 秘密情報の扱い

秘密鍵、トークン、接続資格情報、利用者DB・設定・録音・入力履歴をGitや配布物へ含めない。開発秘密はリポジトリ外の`C:\AI\env\<ツール名>\`へ置く。公開証明書・公開IDは秘密鍵と区別する。疑いがある値をIssue・スクリーンショット・ログへ貼らない。

漏洩を検出した場合は値を伏せ、種類・場所・影響を記録する。失効／再発行を優先して確認し、履歴の強制書換えや旧配布物の撤回を独断で行わない。既存のコピーは履歴削除だけでは失効しない。

## 本体共通の配布拒否ゲート

`publish-packages.ps1`と`build-installers.ps1`は[test-public-payload-security.ps1](../dev/scripts/test-public-payload-security.ps1)を必ず通す。秘密保管用のファイル名・実DB・PDB・リンクを拒否し、固定SHA-256で検証したGitleaksでテキストを完全マスク走査する。同梱.NETは公式8.0／10.0メタデータの最新修正版以上を要求する。通信や検査の失敗を「検出なし」へ置き換えない。

Gitleaksは公式8.30.1を専用のartifactsキャッシュへ取得する。システムへの登録やグローバル設定変更は行わない。既定ルールと無視指定を固定し、`.gitleaksignore`やコード内allowコメントによる迂回を認めない。バイナリ内部や外部FFmpeg・OCR・音声モデルの安全性まで保証するゲートではない。

```powershell
cd C:\develop\KotorinToys
pwsh -NoProfile -File .\dev\scripts\test-security-gates.ps1
pwsh -NoProfile -File .\dev\scripts\test-public-source-security.ps1
```

後者は公開12リポジトリのmirrorとローカルの到達可能履歴を読み、版別logsへ完全マスクの結果だけを作る。GitHub CLIの読取り権限が必要。Git所有者の許可はプロセス内の対象パスだけに限定する。既存のHttpSelectorの差分を採用・変更しない。

公開資産の再点検には`test-public-release-security.ps1`を使う。事前に公式InnoUnp 2.71.2（`jrathlev/InnoUnpacker-Windows-GUI`の`ui_2_2_13/innounp-2.zip`、ZIP SHA-256 `10fce27b6344b7674d5625cc34afca1c88c12183d957c6d93e401097fa2556cd`）を同スクリプトの出力先`tools/innounp-2.71/`へ展開する。EXEの固定ハッシュも実行前に照合する。旧NSIS／MSIは`-SevenZip`で指定した現行7-Zipを使い、製品をインストールせずに展開する。旧公開物に禁止ファイルが残る場合は非0で終了し、配布物自体は削除しない。

## 依存の再検査

NuGetは`dotnet list <製品csproj> package --include-transitive --vulnerable --format json`、npmは各lockの`npm audit --json`（製品用と開発用を区別）、Cargoは固定lockと最新RustSec DBを使って照合する。検査器の版、DBコミット、対象OS、除外・保守終了・unsound警告を記録する。`cargo audit`の終了0だけで警告0と判断しない。今回yanked照合は未実施。

自己完結型.NET、ネイティブDLL、FFmpeg/OCR/モデル、OSとWebView2は別途の更新確認が必要。新しい監査結果に合わせて修正・再ビルドし、旧版と同じtag／assetを無断で置き換えない。公開・署名・導入は個別の承認範囲で実施する。
