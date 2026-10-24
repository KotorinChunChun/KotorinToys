# ScriptSyncの収録登録

2026-10-24。ユーザーが `C:\develop\KotorinToys\tools\ScriptSync` へ移動したソースをKotorinToys管理下へ置く依頼。

## 範囲と計画

- [x] 第1段階：独立Gitの履歴・差分・移動先を確認し、子の定義と現行の開発・配布手順の参照先を整える。リンク・静的検査後、今回の子変更だけコミットする。子コミット `5024bb1`。
- [x] 第2段階：親のgitlink、ローカル一覧、配信用一覧のソース、文書同梱を登録する。定義読込と未対応操作の拒否を関連試験で確認し、今回の親変更だけコミットする。

移動済みソースの収録を対象とする。本体管理用IPCは未実装のため、能力を空配列にして起動・画面表示・停止・自動起動を有効にしない。ScriptSyncの署名付きMSIX配布・識別子・アプリ版を維持し、Inno製品として公開しない。GitHubへのpush、配信configの公開、インストール、本体更新、録音・原稿・利用者設定の移行は対象外。

## 開始時の状態

- 親：`dev/history/v0.1.9/v0.1.9-imp.md`、CapchunScreen・HttpSelector・SSCaptureのgitlink差分、未追跡のSSCapture指示書・MdVisualizerあり。保持する。
- 子：HEAD `63113ed`、originは `https://github.com/KotorinChunChun/ScriptSync.git`。`dev/templates/PROJECT_RULE.md` の既存変更と未追跡の `dev/research/workflow-scenarios/`、`docs/images/` を保持する。
- 旧 `C:\develop\ScriptSync` は存在せず、移動先はリンクではない実ディレクトリ。子の `.git` ディレクトリを保持する。
- 子の所有者警告は当該リポジトリだけ `git -c safe.directory=...` で照合する。

## 検証結果

- 子の静的検査：30項目成功。新規ガイドのローカルリンクとJSON構文、現行資料の旧ルート除去を確認した。歴史資料の旧パスは保持する。
- 本体・テストを `artifacts/scriptsync-registration` へ隔離ビルド：成功、エラー0。NuGet脆弱性情報の取得は通信制限でNU1900警告となり、この照会は未確認。
- `KotorinToys.Tests.exe --script-sync-registration-tests`：68件成功。14件の登録、ScriptSyncの定義一致・参照・MSIX識別維持・未対応操作の拒否を確認した。最初の定義比較ではWindowsの区切り文字の違いを検出し、論理ルートの区切り文字だけ正規化して比較するよう試験を修正した。
- `--online-catalog-tests`：160件成功。模擬通信による配信一覧の受理・拒否・更新の回帰。実GitHubへの公開試験ではない。
- `--repository-button-tests`：45件成功。ブラウザー起動を置換し、ScriptSyncのGitHub導線を含む操作を確認した。
- `--local-catalog-tests`：16件成功。専用設定と模擬状態で一覧更新・通常キャッシュの保持を確認した。
- 本番 `publish-packages.ps1` の本体向け文書同梱ブロックだけを隔離実行。ScriptSyncの定義・README・収録ガイド・利用者ガイド・キーJSON・ICO・キー定義の7参照をハッシュ照合。子EXE・音声・原稿・秘密鍵の混入なし。製品publish・インストーラー作成は実行していない。
- PowerShell構文・今回のMarkdownリンク・Git差分を検査した。ネイティブ録音、実GUI目視、本体への更新・インストールは実行していない。

## 再確認

```powershell
cd C:\develop\KotorinToys
dotnet build tests/KotorinToys.Tests/KotorinToys.Tests.csproj -c Debug --artifacts-path artifacts/scriptsync-registration
./artifacts/scriptsync-registration/bin/KotorinToys.Tests/debug/KotorinToys.Tests.exe --script-sync-registration-tests
pwsh -File dev/scripts/start.ps1 -LocalCatalog
```

一覧では「すべて」「未導入」から確認する。収録ガイドは [子の文書](../../../tools/ScriptSync/docs/kotorintoys-integration.md) を参照。今回の生ログは `logs/scriptsync-registration/`、テストのfixtureは同版の `logs/host-test-*/` に保持する。子の既存未コミット資料は含めない。
