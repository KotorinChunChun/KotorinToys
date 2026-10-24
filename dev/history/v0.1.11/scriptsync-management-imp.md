# ScriptSyncの管理操作連携

2026-10-24。ユーザーから起動・画面表示・状態確認・正常停止の実装指示を受けた。ソース収録時の未対応能力を実装後に有効化する。

- [x] 子の管理契約2を実装し、試験済みコミットを採用する。実装コミット `d6721a5`。
- [x] 開発専用ビルドを本体から参照し、管理能力4件をローカル・配信用定義へ反映する。
- [x] 本体ToolManager経由の実EXEの非表示起動、表示、PID保持、状態、正常停止、再起動と不正要求拒否を隔離確認する。
- [x] 関連回帰と文書を更新し、既存の別作業を除いてコミットする。

設定・WebViewは試験用ディレクトリに隔離する。既存ScriptSyncが稼働している場合は変更せず試験を中止する。強制終了、既存データ移行、MSIX導入、公開、本体の正式導入更新は実行しない。

## 変更内容

- 子の `build-debug-ScriptSync-kotorintoys.ps1` を全体開発ビルドに追加した。画面を内包した `tools/ScriptSync/target/kotorintoys/scriptsync.exe` を参照し、Viteを別途起動せず管理できる。
- ローカル定義と配信用configのソースにstatus／show／stop／silent-startを宣言した。`distributionStatus` はunpublished、既存MSIX識別・4部版を維持する。新しいInno製品やMSIX専用導入経路は追加しない。
- `--local-catalog` のときだけ、実ファイルのある非MSIX開発ツールの状態を「実行中（開発）」「停止中（開発）」として表示する。導入Verは未導入を維持する。通常一覧の未導入表示は変更しない。
- 実接続試験 `--script-sync-ipc` と専用Dashboard `--script-sync-panel` を追加した。後者はScriptSyncだけの一覧・専用設定・読み取り専用のOS予約経路を使用する。

## 検証

| 確認 | 結果 |
|---|---|
| 子のUI回帰 | 34ファイル231件成功 |
| 子のRust回帰 | 156件成功、外部環境依存10件ignored。sandboxのfixture確定時アクセス拒否は通常権限の同一試験で解消 |
| 子の静的検査・PowerShell | 30項目と12スクリプト構文成功 |
| 子の管理用ビルド | custom-protocolで画面内包、専用パスへコピー成功 |
| 本体・テストビルド | 成功。NuGet脆弱性データ照会にはNU1900通信警告、この照会は未確認 |
| 登録・開発状態表示 | `--script-sync-registration-tests` 70件成功 |
| 一覧配信の模擬回帰 | `--online-catalog-tests` 160件成功 |
| UIの操作可否 | `--ui-policy-tests` 112件成功 |
| ローカル一覧 | `--local-catalog-tests` 16件成功 |
| 実ToolManager接続 | `--script-sync-ipc` 17件成功。最終ビルドで実施 |

実接続では、既存プロセスがないことを確認してから、非表示起動・同一PID表示・最小化復元・準備状態と4部版・起動経路・誤PIDとPID欠落の拒否・未対応操作・正常停止・再起動・引数なし通常表示を確認した。WebView終了時にChrome_WidgetWin_0の解除警告が出たが、対象プロセスの終了と再起動を照合できた。

最終実画面では本番と同じDashboardで起動し、logs内へ合成プロジェクトを作成、設定画面中の停止拒否と、設定を閉じた後の正常停止を確認した。開発状態表示の追加後は停止中→実行中→停止中の実表示を別途確認した。今回起動した子と確認用本体は終了した。実マイク・録音音質・外部ASR・MSIX実導入は今回の確認対象ではない。

最終レビューで、停止を拒否しただけの要求が撤回時にバックエンドのロックを待つ経路を子で修正した。実際に停止を準備した要求だけ撤回する。実際の新規保存先選択中に停止を拒否して77ms、直後のstatusはcompleted・ready trueで4msと確認し、キャンセル後に正常停止した。結果は `logs/scriptsync-native-stop-lock/final/result.json`。追加後のRust回帰も156件成功・10件ignored。先の動画とUI試験は画面・通常停止の確認であり、この追加回帰とは区別する。

## 操作動画

- [停止拒否・正常停止](logs/recording-9be5b5e3e4054164b69f01afb6f7eb42/2026年10月24日_12時42分15秒_ScriptSync管理起動・設定中停止拒否・正常停止のテスト風景_9be5b5.mp4)：completed、decoded true、pass。設定画面と拒否案内、終了を映像と実操作で照合。
- [開発状態列](logs/recording-3f4cbaa1df944b798b97837064916c5d/2026年10月24日_12時53分01秒_ScriptSync開発状態の表示確認のテスト風景_3f4cba.mp4)：completed、decoded true、pass。開始・実行中・終了のフレームと実画面で3状態を照合。
- `recording-d34668a8f2cc438a83ca30066bbba24a` は操作前の一覧調整で中断しfailとした記録。成功証跡へ含めない。録画中はmoov未確定のため、フレーム抽出は停止後に実施した。

その他の生ログは `logs/scriptsync-management/`、実接続と合成プロジェクトは `logs/scriptsync-ipc-*/`。Git・公開へ含めない。

## 開発確認

本体の通常開発出力もビルド済み。公開済み本体・既存インストール・OS予約・通常設定の更新は行っていない。

```powershell
cd C:\develop\KotorinToys
pwsh -File dev/scripts/start.ps1 -LocalCatalog
```

既存本体が起動中なら先に本体を終了してから実行する。ScriptSyncを「すべて」「未導入」から選び操作する。新たにソースから用意する場合は子の管理用ビルドと本体ビルドが必要。[管理連携ガイド](../../../tools/ScriptSync/docs/kotorintoys-integration.md)を参照する。
