# KotorinToys 開発者ガイド

## 前提

- Windows 11 x64
- PowerShellコマンドは `pwsh` で実行する
- 本体と各ツールは独立したGitリポジトリとして扱う
- 未確認の実行ファイル名、引数、インストーラーURL、設定パスを推測して登録しない

## リポジトリの確認

```powershell
Set-Location C:\develop\KotorinToys
git status --short
git diff --check
```

## 資料の配置

- 版に属する要求・計画・進捗・実装報告は `dev/history/vX.Y.Z/`
- 現行の利用方法・仕様・再現可能な手順は `docs/`
- 版に属さない調査は `dev/research/<対象>/`
- テスト動画・生ログ・生成結果は `dev/history/vX.Y.Z/logs/<実行識別子>/`

ログは `.gitignore` によりGit対象外です。報告に必要な結果の要約だけを版別資料へ残します。

## 新しいツールを登録するとき

1. 既存ツールのリポジトリ、README、マニュアル、設定、導入経路を実物確認する。
2. `catalog/` に登録情報を追加し、確認済み・未対応・未調査を区別する。
3. 新規ツールは `tool.json`、`README.md`、`docs/manual.ja.md`、`config/shortcuts.json` を入口として整える。
4. インストール、アンインストール、ランタイム、起動確認、終了、サインイン時起動の具体値は、確認できたものだけ記録する。
5. 変更後にリンクと差分を確認し、問題がなければ日本語のコミットメッセージでコミットする。

## 現在の確認範囲

責務別の入口、互換性境界、専用出力での全製品回帰は[開発時の責務と検証](architecture.md)を参照してください。

本体はC# / .NET 10 / Windows Formsを使用します。各ツールは独立したプロセスで動作し、現在のWindowsユーザー専用の名前付きパイプで状態取得、画面表示、正常終了要求を受けます。

## ビルドと起動

```powershell
cd C:\develop\KotorinToys
pwsh -File dev/scripts/build.ps1
pwsh -File dev/scripts/start.ps1
```

.NET 10 SDKと.NET 10 Windows Desktop Runtimeが必要です。LiveTextMarkerとSmartPasteは.NET 8ランタイム同梱の開発出力を使用します。NuGet復元にはネットワーク接続が必要です。更新ビルド前には本体と3ツールを正常終了してください。

## 統合検証

本体の `--self-test` は実際の3ツールで起動、既存PIDの維持、正常終了、再起動を検証します。最後は3ツールを起動した状態にします。実行前に編集中の操作や撮影・録画を完了してください。

## 独立Git管理

親リポジトリのtools内4フォルダーは独立したGitリポジトリです。ShortcutKeyCounterはリポジトリ登録・紹介のみで、本体の管理カタログにはまだ登録していません。まず子で変更をコミット・pushし、親で採用コミットを記録します。`.gitmodules`に各公開先を登録し、次の操作で採用版をまとめて取得できます。

```powershell
git clone --recurse-submodules https://github.com/KotorinChunChun/KotorinToys.git
```

本体だけを取得済みの場合は、本体フォルダーで`git submodule update --init --recursive`を実行します。子の最新版を無条件に取得するのではなく、親に記録された採用コミットを取得します。ソースの公開と製品インストーラーのReleases公開は別です。

## Releases配布

本体と3ツールは`KotorinChunChun`配下の各Releasesへ、未署名の開発版インストーラー・SHA256SUMS・製品別台帳を公開します。`catalog/releases.json`は同梱版の製品・版・サイズ・固定SHA-256を記録します。開発中のソースではPublicReleaseClientで既知3リポジトリの公開版（開発版を含む）と製品別台帳を取得し、製品識別・数値tag・準備済みasset・サイズ・GitHubのdigest（提供時）を照合します。取得失敗時の同梱版は未確認として表示します。同梱済み版と同じ版の配布情報が変更された場合も拒否します。新しい配布物は新しい版へ進め、旧assetを上書きしないでください。

公開入力は`dev/scripts/publish-packages.ps1`で発行し、`build-installers.ps1`でインストーラーと固定ハッシュ台帳を生成します。公開版SSCaptureは`publish-packages.ps1 -ExcludeFFmpeg`を指定し、FFmpeg／FFprobeと利用者dataを含めません。`publish-releases.ps1 -CatalogPath <版別logsのpackages.json> -Products <製品名>`は固定アカウント・採用コミット・配布物のハッシュを確認して公開します。既存Releasesやassetの自動上書きはしません。

製品ダウンロードはGitHubと指定されたGitHub assetホストのHTTPSだけ許可し、時間・容量・サイズ・SHA-256を検査します。同梱版は本体の固定ハッシュ、新しい公開版は既知所有者の製品別台帳とGitHub assetの照合で確定したハッシュを使用します。公開版の表示だけで導入せず、利用者の確認時にその固定スナップショットを取得します。新公開版の台帳はGitHub上の配布元を信頼する境界であり、独立したコード署名による発行元確認ではありません。未署名の注意と導入済み版→対象版を表示します。

FfmpegInstallerはこれとは別にGyanの固定HTTPS ZIP・固定容量・固定SHA-256を使用します。正式導入済みで停止中のSSCaptureだけに必要な2 EXE・ライセンス・READMEを配置し、設定／履歴／PATHを変えません。製品インストーラーへの同梱や自分のReleasesへの再配布はしません。
