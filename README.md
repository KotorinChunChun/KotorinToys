# KotorinToys

KotoriChunが開発・管理するWindows向けデスクトップ便利ツールを、導入・起動・状態確認・自動起動・使い方への導線とともに管理する本体プロジェクトです。

## プロジェクトの状態

LiveTextMarker、SmartPaste、SSCapture、ShortcutKeyCounter、CapchunScreen、ChulipVideo、ChunapTool、HttpSelector、EveryDB、VoiceMixerMicRouter、VoiceMixerSpkRouter、OperationRecorderの12件を登録し、本体の一覧から起動、既存画面の表示、正常停止、状態確認、使い方の表示を行えます。現在は開発中です。ソース公開は製品の正式リリースや全機能の受入完了を意味しません。

## ソースの取得

ScriptSync（読みあわせ）も `tools/ScriptSync` の独立Gitとして収録しています。本体一覧では使い方とGitHubを案内します。本体からの起動・停止・自動起動は未対応です。[収録ガイド](tools/ScriptSync/docs/kotorintoys-integration.md)を参照してください。ソースは `git submodule update --init -- tools/ScriptSync` で取得します。ローカルで採用した子コミットが未pushの場合は、公開先で取得可能になるまで既存チェックアウトを使用してください。

```powershell
git clone https://github.com/KotorinChunChun/KotorinToys.git
cd KotorinToys
git submodule update --init --recursive -- tools/LiveTextMarker tools/SmartPaste tools/SSCapture tools/ShortcutKeyCounter tools/CapchunScreen tools/ChulipVideo tools/ChunapTool
```

公開リポジトリのある7ツールは、それぞれ本体が採用した版を取得します。CapchunScreenフォルダーの製品名はCapchunWindow、ChulipVideoは専用のChulipVideo-CSharpを取得します。EveryDBは [独立リポジトリ](https://github.com/KotorinChunChun/EveryDB) から `git submodule update --init -- tools/EveryDB` で取得できます。HttpSelectorはローカル管理のため、9件構成の開発ビルドには別途ソースが必要です。全体への`--recurse-submodules`は現段階では使用しないでください。配布本体の利用に子ソースの取得は不要です。

0.1.8の本体ソースは公開済みですが、一部の採用子コミットのpushは別途確認中です。ソースビルドで採用SHAを取得できない場合は公開待ちとなります。setupの利用には影響しません。

## サポートアプリ

公開0.1.8では、短い一覧表示を保ち、詳細セルのクリック／Enterで理由と対処を開けます。絞り込み・表示保存、条件付きのEXE修復、用途別の一括操作タブ、公開前検査を追加しています。操作説明は[利用者向け](docs/USERS_GUIDE.md)、定義の追加方法や判定条件は[開発者向け](docs/DEVELOPERS_GUIDE.md)へ分離しています。旧版の配布物は差し替えず、新しい0.1.8を公開しています。

一覧は起動時と「状態を更新」でGitHubのconfigを取得する構成です。この機能を含む本体への初回更新後は、mainの [config/tools.json](config/tools.json) だけで追加・非表示・順序・説明を変更できます。通信できないときは前回の正常な一覧を維持します。[設定更新の運用手順](docs/online-tool-catalog.md)を参照してください。以下は同梱時点の初期一覧です。

| アプリ | 概要 | 本体との連携 |
|---|---|---|
| [LiveTextMarker](tools/LiveTextMarker/README.md) | テキスト表示を補助するツール | 起動・停止・状態確認に対応 |
| [SmartPaste](tools/SmartPaste/README.md) | 貼り付け作業を支援するツール | 起動・停止・状態確認に対応 |
| [SSCapture](tools/SSCapture/README.md) | 画面撮影・録画と保存先や命名を管理するツール | 起動・停止・状態確認に対応 |
| [ShortcutKeyCounter](tools/ShortcutKeyCounter/README.md) | ショートカットキーの利用回数を端末内で記録・集計し、入力を画面に表示するツール | 開発版の起動・画面表示・正常停止・状態確認に対応 |
| [CapchunWindow](tools/CapchunScreen/README.md) | 操作風景と入力イベントを同じ時間軸で記録し、動画マニュアル・手順の振り返り・AI向けコンテキストに活用するツール | 導入・更新・削除と起動・停止・状態確認に対応 |
| [ChulipVideo C#版](tools/ChulipVideo/README.md) | 動画から手順書用の画像を切り出すツール | 導入・更新・削除と起動・停止・状態確認に対応 |
| [ChunapTool](tools/ChunapTool/README.md) | ウィンドウの位置・サイズ・最大化を調整するツール | 導入・更新・削除と起動・停止・状態確認に対応 |
| [HttpSelector](tools/HttpSelector/README.md) | 危険なリンクを警告し、ブラウザーのプロファイルを選ぶツール | 0.1.6ソースで署名付きMSIXの導入・更新・削除、起動・停止・状態確認に対応 |
| [EveryDB](tools/EveryDB/README.md) | 複数のデータソースをSQLiteへ取り込み、横断検索するツール | 共通インストーラーで導入・更新・削除と起動・停止・状態確認に対応 |
| [VoiceMixerMicRouter](https://github.com/KotorinChunChun/VoiceMixerMicRouter) | MIC・RVC・スピーカー入力を3出力へ送るツール | 開発ソースで導入・更新・削除、起動・設定表示・正常停止・状態確認に対応 |
| [VoiceMixerSpkRouter](https://github.com/KotorinChunChun/VoiceMixerSpkRouter) | 再生デバイス・アプリ・ブラウザーの音声を2出力へ転送するツール | 開発ソースで導入・更新・削除、起動・設定表示・正常停止・状態確認に対応 |
| [OperationRecorder](tools/OperationRecorder/README.md) | PC操作を静止画で記録し、画像付きの手順書を作るツール | 導入・更新・削除、起動・画面表示・正常停止・状態確認に対応 |
| [ScriptSync（読みあわせ）](tools/ScriptSync/README.md) | 台本を発話ごとに録音し、採用テイクを結合出力するツール | ソース収録・使い方・GitHub案内。管理操作は未対応 |

OperationRecorder 1.3.2は[公開リポジトリ](https://github.com/KotorinChunChun/OperationRecorder)で管理し、本体対応のランタイム同梱インストーラーを提供します。本体0.1.9以降の「状態を更新」でオンライン一覧と公開版を取得し、一覧から導入できます。[管理連携ガイド](tools/OperationRecorder/docs/kotorintoys-integration.md)を参照してください。開発時は専用Debugビルド後に`pwsh -File dev/scripts/start.ps1 -LocalCatalog`でローカル一覧を確認できます。実際の公開・導入検証の結果は[実装・公開記録](dev/history/v0.1.9/v0.1.9-imp.md)へまとめています。

音声2件は0.1.8とGitHub mainの配信用configへ公開済みです。旧本体の固定識別では対応できない場合があるため、本体も0.1.8へ更新してください。[登録補完と検証](dev/history/v0.1.7/voice-router-registration.md)を参照してください。

HttpSelectorは署名付きMSIXが必須です。0.1.6ソースでは[本体からのMSIX管理](docs/msix-management.md)に対応しました。WindowsへHTTP/HTTPSの関連付け候補を登録した後、既定のアプリ設定で利用者が選択してください。本体は署名・Publisher・Package Identity・製品版を照合し、未導入や旧版を開発EXEに切り替えません。Inno EXEとは別の専用導入経路を使用します。

ShortcutKeyCounterの本体連携は[管理連携の手順](tools/ShortcutKeyCounter/docs/kotorintoys-integration.md)を参照してください。開発版の本体では[ポータブル版の公開検出・表示](docs/portable-release-publication.md)に対応し、版と公開日を一覧へ掲載します。本体からの正式導入・更新は未対応です。公開済みv0.1.5ポータブルZIPの単独利用は[ユーザーガイド](tools/ShortcutKeyCounter/docs/users-guide.md)を参照してください。

追加3ツールは独立Gitを維持し、親は採用コミットだけを記録します。[CapchunWindow 0.1.2](https://github.com/KotorinChunChun/CapchunWindow/releases/tag/v0.1.2)、[ChulipVideo C# 0.2.1](https://github.com/KotorinChunChun/ChulipVideo-CSharp/releases/tag/v0.2.1)、[ChunapTool 0.1.2](https://github.com/KotorinChunChun/ChunapTool/releases/tag/v0.1.2)をランタイム同梱の本体対応インストーラーで公開しています。旧ZIPや別実装は置き換えません。FFmpegと仮想カメラドライバーは同梱しません。各アプリの導入ガイドを確認してください。

EveryDBは[管理連携ガイド](tools/EveryDB/docs/kotorintoys-integration.md)に従って専用デバッグ版をビルドします。[独立リポジトリ](https://github.com/KotorinChunChun/EveryDB)は公開前点検と履歴整理を実施しました。履歴整理前に取得したコピーは新規取得し直してください。本体0.1.5以降はEveryDB 0.1.1の共通Inno形式を照合し、導入・更新・削除できます。旧MSI／NSIS版からは手動移行です。旧版を終了・削除したうえで共通版を導入し、既存DBを引き続き利用します。WebView2 Runtimeが必要です。

SSCapture、OperationRecorder、CapchunWindowの目的と用途の違いは、[画面・操作記録ツールの使い分け](docs/recording-tools.md)を参照してください。

## インストーラー（開発版）

[本体0.1.8のReleases](https://github.com/KotorinChunChun/KotorinToys/releases/tag/v0.1.8)から`KotorinToys-0.1.8-setup.exe`を取得してください。Windows 11 x64・利用者単位・.NET 10.0.13同梱の未署名開発版です。全機能の受入完了版ではありません。自己更新対応の既存本体では「本体アップデート」で開発版も対象にするか、setupで保持付き更新してください。

メイン一覧から「インストール」「アップデート」「アンインストール」と条件付きの「修復」を操作できます。GitHub Releasesの固定版・容量・SHA-256を照合し、公開日はツールチップで確認できます。一括操作・SSCaptureのFFmpeg導入・ローカル台帳は「一括インストール」の用途別タブにあります。設定・履歴は保持し、正常終了を拒否した対象は更新・削除しません。SSCaptureのOneOCR外部ファイルは別途必要です。

## 開発版の起動

v0.1.1の本体全体の操作行には「本体アップデート」があります。GitHub Releasesの新しい本体を検証して取得し、本体だけを終了・更新・再起動します。正式版が既定で、開発版は明示選択です。設定・履歴・予約と子の稼働を保持します。旧v0.1.0からは最初に手動導入してください。[利用方法・配布条件・検証範囲](docs/self-update.md)を確認してください。

開発中のソースでは、導入済み版／GitHub公開版／更新の要否／一言解説、全体と選択行の状態、導入完了の結果一覧、FFmpeg導入・修復を追加しています。使い方は[利用ガイド](docs/USERS_GUIDE.md)を参照してください。2026年10月17日に検証PC用の本体インストーラーを作成して保持付き更新を行い、GitHub mainのconfig取得・8件反映を確認しました。上記の公開済みReleasesのインストーラーは差し替えていません。

```powershell
cd C:\develop\KotorinToys
pwsh -File dev/scripts/start.ps1
```

初回のビルドは `pwsh -File dev/scripts/build.ps1` です。本体を閉じても各ツールは動作を続けます。

## 対応環境

- Windows 11
- x64

## 構成

- `src/` — KotorinToys本体のソース
- `catalog/` — 収録ツールの一覧と登録情報
- `config/` — GitHubで差し替える自己完結した配信用ツール一覧
- `contracts/` — ツール情報・文書・ショートカットの共通仕様
- `tools/` — 独立Gitリポジトリとして配置する各ツール
- `docs/` — 利用・開発・運用の現行資料
- `dev/history/v0.1.0/` — v0.1.0の要求・準備計画・進捗

各ツールは単独で導入・開発・利用できる独立リポジトリとして扱います。親プロジェクトから既存ツールの内部構成を一括変更しません。今回の配布範囲と検証結果は[v0.1.2記録](dev/history/v0.1.2/v0.1.2-imp.md)を参照してください。

## 開発者向け情報

環境確認と資料の扱いは [開発者ガイド](docs/DEVELOPERS_GUIDE.md) を参照してください。製品要件の正本は [v0.1.0要求定義](dev/history/v0.1.0/v0.1.0-req.md) です。

## 作者

KotoriChun

## VoiceMixerSpkRouter

[VoiceMixerSpkRouter](tools/VoiceMixerSpkRouter/README.md)を独立Gitとして収録しています。再生デバイス・アプリ・ブラウザーの音声を2出力へ転送するツールです。v1.0.10の共通インストーラーと管理契約2を登録し、本体から導入・更新・削除、起動・画面表示・正常停止・状態確認に対応します。公開版は未署名の開発版です。[今回の検証結果](dev/history/v0.1.6/voicemixer-spk-router-imp.md)を参照してください。取得は `git submodule update --init -- tools/VoiceMixerSpkRouter`。[旧1.0.9の検証記録](dev/history/v0.1.5/voicemixer-spk-router-pc-test.md)を参照してください。

## VoiceMixerMicRouter

[VoiceMixerMicRouter](tools/VoiceMixerMicRouter/README.md) v1.0.18を登録しました。MIC・RVC・スピーカー入力を3出力へ送る常駐ツールです。本体0.1.6の開発ソースでは起動・設定表示・正常停止と共通インストーラー管理に対応します。RVC環境・モデル・仮想音声ドライバーは別途必要です。[追加と検証の記録](dev/history/v0.1.6/voicemixer-mic-router-imp.md)を参照してください。
