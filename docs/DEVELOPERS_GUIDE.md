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

.NET 10 SDKと.NET 10 Windows Desktop Runtimeが必要です。LiveTextMarkerとSmartPasteは.NET 8ランタイム同梱の開発出力を使用します。ShortcutKeyCounterはRust stable MSVC・C++ Build Tools・Node.js・WebView2を使用し、`target/debug/ShortcutKeyCounter.exe`を管理対象にします。NuGet／Cargo復元にはネットワーク接続が必要です。更新ビルド前には対象プロセスを正常終了してください。

## 統合検証

本体の `--self-test` は登録された8ツールで起動、既存PIDの維持、正常終了、再起動を検証します。HttpSelectorは署名付きMSIXの導入が必要です。既存インスタンスは変更せず、今回起動したツールは最後に正常停止します。実行前に編集中の操作や撮影・録画を完了してください。

追加3件は`dev/scripts/build.ps1`で各子の`artifacts/management`へDebugビルドします。旧版が通常のbin/Debugで実行中でも上書きしません。本体のカタログはこの管理連携用出力を参照します。

追加3件だけの隔離連携：`dotnet build tests/KotorinToys.Tests/KotorinToys.Tests.csproj -c Debug --artifacts-path artifacts/support-apps`の後、`./artifacts/support-apps/bin/KotorinToys.Tests/debug/KotorinToys.Tests.exe --new-tools-ipc`を実行します。開始時に停止中であることを確認し、版別logs内の専用設定を使用します。CapchunScreenのホットキーを取得せず、実動画や既存設定を操作しません。`--new-tools-panel`は通常本体とは別の同じDashboardを開き、予約変更を禁止します。管理の起動・表示・終了の検証は録画・書き出し・外部窓操作そのものの受入ではありません。

## ShortcutKeyCounterだけの隔離検証

通常の本体・3ツールを変更せず確認する場合、追加ツールの通常プロセスを終了してから次を使用します。検証DBは本体v0.1.0のlogs内、入力Hook・オーバーレイ・Run同期は無効です。

```powershell
cd C:\develop\KotorinToys
dotnet build tests/KotorinToys.Tests/KotorinToys.Tests.csproj -c Debug --artifacts-path artifacts/shortcut-final
./artifacts/shortcut-final/bin/KotorinToys.Tests/debug/KotorinToys.Tests.exe --shortcut-ipc
# 実ダッシュボードの隔離プレビュー（本体の通常トレイは変更しない）
./artifacts/shortcut-final/bin/KotorinToys.Tests/debug/KotorinToys.Tests.exe --shortcut-panel
```

プレビューは画面を閉じるだけでは子ツールを終了しません。「停止させる」で今回の試験ツールを停止してから閉じてください。本体の正式導入版を更新する操作ではありません。

## 独立Git管理

親リポジトリのtools内の各フォルダーは独立したGitリポジトリです。まず子で変更をコミットし、親でmode-160000の採用コミットを記録します。以下は公開先のある7件の取得です。CapchunScreenはCapchunWindow、ChulipVideoはChulipVideo-CSharpを取得します。EveryDBは別途明示したupdateで取得できます。HttpSelectorはURL未設定のローカル管理を維持します。

```powershell
git clone https://github.com/KotorinChunChun/KotorinToys.git
cd KotorinToys
git submodule update --init --recursive -- tools/LiveTextMarker tools/SmartPaste tools/SSCapture tools/ShortcutKeyCounter tools/CapchunScreen tools/ChulipVideo tools/ChunapTool
```

本体だけを取得済みの場合も、上記の7件を明示したupdateを使用します。全体への`--recurse-submodules`はURL未設定の子で失敗するため使用しません。子の最新版を無条件に取得するのではなく、親に記録された採用コミットを取得します。ソース公開と製品Releaseは別です。0.1.2から追加3ツールも本体対応Inno配布を行います。ShortcutKeyCounter・HttpSelector・EveryDBの本体用Inno配布は対象外です。

## Releases配布

本体の自己更新は[本体更新の配布条件と隔離検証](self-update.md)を参照してください。子ツールの取得経路は本体を拒否したままにし、専用のメンテナンス引継ぎで実装しています。実装コミットと新しい版のRelease公開・このPCへの更新は別の工程です。

### 本体内部の責務

ツール一覧の配信は[GitHub設定の運用手順](online-tool-catalog.md)を参照してください。ToolCatalogはローカル開発定義の互換読込・共通検証、OnlineToolCatalogは公開configの検証、ToolCatalogClientは固定HTTPSの取得、ToolCatalogSourceは最後の正常一覧と予約照合用の旧登録を扱います。本番のHostApplicationContextは共有Sourceを持つToolManagerをDashboardとStartupCoordinatorへ渡します。配信用configは子ファイルに依存せず、ビルド・発行時にも本体へコピーします。公開configの更新と製品バイナリの配布は別です。

`Dashboard.cs`は画面の依存・状態・寿命を保持し、`Dashboard.Layout.cs`で構築とイベント接続、`Dashboard.Display.cs`で表の詳細・操作失敗通知・ボタンの有効条件、`Dashboard.Operations.cs`で操作の直列化と単体導入／詳細設定の導線、`Dashboard.Refresh.cs`で実行状態・公開版の再取得を扱います。メインにフッターは作りません。単体の導入・アップデート・削除はメインから選択IDと操作種別を渡し、通常のInstallationFormには確認・進捗・結果だけを表示します。operation未指定の同フォームは開発者向け詳細設定となり、一括処理・台帳切替・FFmpegを表示します。

`ToolInstallationRunner`は本体対応形式と製品識別を確認し、製品ごとに取得・実行・失敗結果を完結させます。configへの追加だけで未対応形式を実行可能にはしません。`ReleaseDisplay`はGitHubのpublished_atを日本時間の公開日へ変換し、版表示と確認文を共通化します。公開日を取得日時で代用せず、旧台帳の日付欠落も許容します。ローカル台帳の欠落製品も、停止・実行より前に日本語で拒否します。

今回の変更は配布用publishやインストーラー生成なしで次の隔離ビルドと関連回帰を実行できます。全体回帰は最後の引数を外します。実導入・削除とデスクトップ目視は別の確認です。

```powershell
cd C:\develop\KotorinToys
dotnet build tests/KotorinToys.Tests/KotorinToys.Tests.csproj -c Debug --artifacts-path artifacts/installation-ui
& .\artifacts\installation-ui\bin\KotorinToys.Tests\debug\KotorinToys.Tests.exe --installation-tests
```

`PublicReleaseClient`は製品識別・期限・失敗時の結果を制御し、`.Http.cs`は許可URL・リダイレクト・容量制限付きの読込だけを担当します。`PublicReleaseMetadata`は通信・画面・設定保存を持たず、最新候補の選択、assetの所有者・製品・tag、台帳と固定版・ハッシュの一致を検証します。選択候補はJSONを所有し、元のJsonDocumentの寿命に依存しません。HTTPを模擬する回帰と検証型の直接テストを両方維持してください。

本体と対応6ツールは`KotorinChunChun`配下の各Releasesへ、未署名の開発版インストーラー・SHA256SUMS・製品別台帳を公開します。`catalog/releases.json`は同梱版の製品・版・サイズ・固定SHA-256を記録します。PublicReleaseClientはProductIdentityの固定配布先（C#版ChulipVideoはChulipVideo-CSharp）を使い、公開版（開発版を含む）・台帳・製品識別・数値tag・asset・容量・GitHub digestを照合します。取得失敗時の同梱版は未確認です。同版の配布情報差替を拒否し、新しい配布物は新しい版へ進めます。旧assetを上書きしないでください。

公開入力は`stage-release-source.ps1`で親コミットと各gitlinkの採用ソースだけをUTF-8 ZIPから展開して作成します。元の作業ツリーや未採用の子HEADを混ぜません。日本語パス・Windowsパス長に備え短い隔離ルートを使います。配布スクリプトは`distribution-products.ps1`の固定表を共有し、`test-distribution-scripts.ps1`で検査できます。追加3ツールは`-Products CapchunWindow,ChulipVideo,ChunapTool`で指定します。製品名とソースのフォルダー名を混同しないでください。

公開入力は`dev/scripts/publish-packages.ps1`で発行し、`build-installers.ps1`でインストーラーと固定ハッシュ台帳を生成します。公開版SSCaptureは`publish-packages.ps1 -ExcludeFFmpeg`を指定し、FFmpeg／FFprobeと利用者dataを含めません。`publish-releases.ps1 -CatalogPath <版別logsのpackages.json> -Products <製品名>`は固定アカウント・採用コミット・配布物のハッシュを確認して公開します。既存Releasesやassetの自動上書きはしません。

配布・Inno準備・ローカル導入のログは、既定でcatalog/host.jsonの版の`dev/history/vX.Y.Z/logs/`へ保存します。他製品や初回導入用旧版を検証する場合は`-RecordVersion vX.Y.Z`で記録の対象版を明示してください。製品版そのものを変更する引数ではありません。公開台帳の許可範囲も指定版のlogs内に限定します。

製品ダウンロードはGitHubと指定されたGitHub assetホストのHTTPSだけ許可し、時間・容量・サイズ・SHA-256を検査します。同梱版は本体の固定ハッシュ、新しい公開版は既知所有者の製品別台帳とGitHub assetの照合で確定したハッシュを使用します。公開版の表示だけで導入せず、利用者の確認時にその固定スナップショットを取得します。新公開版の台帳はGitHub上の配布元を信頼する境界であり、独立したコード署名による発行元確認ではありません。確認文は導入済み版→対象版と公開日・質問だけとし、未署名の注意と照合詳細はガイド・詳細ログに残します。

FfmpegInstallerはこれとは別にGyanの固定HTTPS ZIP・固定容量・固定SHA-256を使用します。正式導入済みで停止中のSSCaptureだけに必要な2 EXE・ライセンス・READMEを配置し、設定／履歴／PATHを変えません。製品インストーラーへの同梱や自分のReleasesへの再配布はしません。

## HttpSelectorのMSIX連携と検証

HttpSelectorは独立Gitを保持して `tools/HttpSelector` に配置します。通常利用は署名付きMSIX必須です。`tool.json` の `msix` がPackage Family・Publisher・Application Idを宣言し、本体はOS登録とManifestを照合してWindowsのパッケージ起動APIを使います。実プロセスのPackage Familyも照合し、非MSIX開発プロセスを取り違えません。資料は子リポジトリを参照します。

`dev/scripts/build.ps1` は隔離管理試験用に子の `target/management` もビルドしますが、通常起動でこのEXEにフォールバックしません。

```powershell
cd C:\develop\KotorinToys
dotnet build tests/KotorinToys.Tests/KotorinToys.Tests.csproj --artifacts-path artifacts/http-selector-tests
.\artifacts\http-selector-tests\bin\KotorinToys.Tests\debug\KotorinToys.Tests.exe --http-selector-ipc
.\artifacts\http-selector-tests\bin\KotorinToys.Tests\debug\KotorinToys.Tests.exe --http-selector-msix
```

最初は専用設定・スキャン無効のDebug EXEでの管理契約試験、後者は署名付きMSIX 0.0.3.2を現在ユーザーに導入した実パッケージ試験です。既存HttpSelectorがあれば試験開始せず、試験所有PIDだけを正常停止します。MSIX試験は導入済みの利用者設定を使用するため事前に退避し、プロファイルを開いたり既定アプリを変更したりしません。結果は子の `dev/history/v0.0.3.2/` に記録します。
