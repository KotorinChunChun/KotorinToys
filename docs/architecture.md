# 開発時の責務と検証

本体と各収録アプリは独立した製品・Gitリポジトリ・プロセスとして扱います。参照先や実行方法は[開発者ガイド](DEVELOPERS_GUIDE.md)、一覧・画面・導入処理の分担は[本体の構成要素](development/host-components.md)を参照してください。

## 本体

| 責務 | 主な入口 |
| --- | --- |
| カタログと安全な相対パス | [ToolCatalog](../src/KotorinToys/ToolCatalog.cs)、[CatalogModels](../src/KotorinToys/CatalogModels.cs)、[PathSafety](../src/KotorinToys/PathSafety.cs) |
| ツールの配置・情報集約 | [ToolManager](../src/KotorinToys/ToolManager.cs) |
| 管理窓口との通信・状態・起動停止 | [通信](../src/KotorinToys/ToolManager.Transport.cs)、[状態](../src/KotorinToys/ToolManager.Status.cs)、[ライフサイクル](../src/KotorinToys/ToolManager.Lifecycle.cs) |
| 正式導入・依存物の検出 | [InstallationDetector](../src/KotorinToys/InstallationDetector.cs)、[RuntimeDetector](../src/KotorinToys/RuntimeDetector.cs) |
| 配布識別と台帳検証 | [ProductIdentity](../src/KotorinToys/ProductIdentity.cs)、[PackageCatalogReader](../src/KotorinToys/PackageCatalogReader.cs) |
| 取得・検証保存・導入 | [ReleaseDownloader](../src/KotorinToys/ReleaseDownloader.cs)、[HTTP](../src/KotorinToys/ReleaseDownloader.Http.cs)、[検証保存](../src/KotorinToys/ReleaseDownloader.Transfer.cs)、[InstallerAdapter](../src/KotorinToys/InstallerAdapter.cs)、[InstallationQueue](../src/KotorinToys/InstallationQueue.cs) |
| 設定・予約・復旧 | [HostSettingsStore](../src/KotorinToys/HostSettingsStore.cs)、[StartupCoordinator](../src/KotorinToys/StartupCoordinator.cs)、[TaskSchedulerGateway](../src/KotorinToys/TaskSchedulerGateway.cs)、[RemovalReservations](../src/KotorinToys/RemovalReservations.cs) |
| 公開版の照合・更新判定・起動復帰 | [PublicReleaseClient](../src/KotorinToys/PublicReleaseClient.cs)、[DashboardStatus](../src/KotorinToys/DashboardStatus.cs)、[ToolUpdateCoordinator](../src/KotorinToys/ToolUpdateCoordinator.cs) |
| 一括最終結果・外部録画依存の導入 | [InstallationSummary](../src/KotorinToys/InstallationSummary.cs)、[FfmpegInstaller](../src/KotorinToys/FfmpegInstaller.cs) |
| 本体アイコンの共通化・画面の所有権 | [AppIcon](../src/KotorinToys/AppIcon.cs)、[BrandedForm](../src/KotorinToys/BrandedForm.cs)、[共通規約](app-icon-rule.md) |

モデルは実処理と分離しています。メンテナンスEXEは必要な非UIソースだけを明示的に共有し、製品識別を同じProductIdentityで照合します。ダウンロードは、HTTP応答の所有権・固定版／ホスト・容量・SHA-256・検証後の確定保存を分けて扱います。未署名であることや確認できない状態を成功へ補完しません。

## 各ツール

- LiveTextMarker：Controllerは状態と画面初期化、Controller.Trackingは追従、Controller.Settingsは設定、Controller.Lifecycleは管理窓口と正常終了を担当します。Detector・Geometry・Overlayなど既存の認識／描画部品は維持します。
- SmartPaste：Programは起動入口、AppSettingsは保存、TrayAppは初期化を担当します。Clipboard・Paste・PasteImage・PasteFiles・Lifecycleで監視／貼付／終了を分け、CopyReviewPolicyは確認要否だけを判定します。Uiと個別のForm型は画面を担当し、Naming・CopyService・Images・OCRワーカーは既存の処理部品として維持します。
- SSCapture：TrayApplicationContextは状態と初期化を担当します。Input・Capture・Recording・Settings・Regions・History・Feedback・Lifecycleが各処理を担当します。TrayOperationPolicyは終了拒否と通知保留だけを判定します。Capture／Recording／Services／UIの既存部品は維持します。

partialは状態を共有するための整理であり、別インスタンスや独立したサービスではありません。状態フィールドを別ファイルへ複製したり、初期化・イベント登録・破棄の順序を変えないでください。画面名・Control.Name・管理窓口・保存JSONは互換性境界です。非同期状態機械の総入替や全UIの設計変更は、この整理に含みません。

## 回帰検証

既存の稼働アプリを更新せず、専用のartifactsと版別logsへ出力します。.NET 8のテストは自己完結型で発行します。実ユーザーの設定・履歴を試験入力には使いません。Scheduler試験は専用のGUID付き予約だけを作成・解除します。

```powershell
cd C:\develop\KotorinToys
$recordVersion = 'v' + (Get-Content catalog/host.json -Raw | ConvertFrom-Json).version
pwsh -NoProfile -File dev/scripts/test-refactoring.ps1 -Target All -Version $recordVersion
```

TargetはHost／LiveTextMarker／SmartPaste／SSCaptureへ限定できます。失敗した工程は終了コードとlogsで確認します。実OCR・AI・録画・デバイス・実サインイン・導入更新の受入は別の検証であり、この非対話テストの合格から推定しません。

## 配布物を作らないコード検証

```powershell
cd C:\develop\KotorinToys
dotnet build tests/KotorinToys.Tests/KotorinToys.Tests.csproj --artifacts-path artifacts/code-tests
& ./artifacts/code-tests/bin/KotorinToys.Tests/debug/KotorinToys.Tests.exe
```

この既定テストは模擬インストーラー／ZIP／APIと実フォームのコントロールを検査します。実インストールや既存ツールの停止はしません。--public-release-checkは実GitHubのAPIと小さな台帳だけを取得して照合し、製品バイナリを取得・実行しません。一時Debug出力を配布用成果物の完成と扱わないでください。

## 構造変更の検査

[SourceRefactor](../dev/scripts/SourceRefactor/Program.cs)はSDK同梱Roslynで構文を読み、全メンバーtoken一致を検査してpatchを出力する開発補助です。自分では製品ソースを変更しません。[版別移動マップ](../dev/history/v0.1.0/refactoring-map.json)は当時の移動を記録したもので、現在のファイルへ無条件に再適用しないでください。既存出力や構文エラー、ディレクティブを含む入力は拒否します。

--verify-layoutは各リポジトリの元mainとの状態フィールド・初期化順序・主コンストラクター・保存モデルを照合します。mainへ統合した後の新しい比較基準を推測して使わないでください。この構造検査はビルド・動作テストの代替ではありません。

```powershell
dotnet build dev/scripts/SourceRefactor/SourceRefactor.csproj --artifacts-path artifacts/refactoring-tool
.\artifacts\refactoring-tool\bin\SourceRefactor\debug\SourceRefactor.exe C:\develop\KotorinToys --verify-layout dev/history/v0.1.0/refactoring-map.json
```
