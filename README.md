# KotorinToys

KotoriChunが開発・管理するWindows向けデスクトップ便利ツールを、導入・起動・状態確認・自動起動・使い方への導線とともに管理する本体プロジェクトです。

## プロジェクトの状態

LiveTextMarker、SmartPaste、SSCapture、ShortcutKeyCounter、CapchunScreen、ChulipVideo、ChunapTool、HttpSelectorの8件を登録し、本体の一覧から起動、既存画面の表示、正常停止、状態確認、使い方の表示を行えます。現在は開発中です。ソース公開は製品の正式リリースや全機能の受入完了を意味しません。

## ソースの取得

```powershell
git clone https://github.com/KotorinChunChun/KotorinToys.git
cd KotorinToys
git submodule update --init --recursive -- tools/LiveTextMarker tools/SmartPaste tools/SSCapture tools/ShortcutKeyCounter tools/ChunapTool
```

公開先のある5ツールは、それぞれの独立リポジトリから本体が採用した版を取得します。CapchunScreen、ChulipVideo、HttpSelectorのソースはローカル管理で、Gitの公開先が未設定です。8件構成をビルドするにはこれら3フォルダーの独立リポジトリも必要です。全体への`--recurse-submodules`は現段階では使用しないでください。

## サポートアプリ

| アプリ | 概要 | 本体との連携 |
|---|---|---|
| [LiveTextMarker](tools/LiveTextMarker/README.md) | テキスト表示を補助するツール | 起動・停止・状態確認に対応 |
| [SmartPaste](tools/SmartPaste/README.md) | 貼り付け作業を支援するツール | 起動・停止・状態確認に対応 |
| [SSCapture](tools/SSCapture/README.md) | 画面撮影・録画と保存先や命名を管理するツール | 起動・停止・状態確認に対応 |
| [ShortcutKeyCounter](tools/ShortcutKeyCounter/README.md) | ショートカットキーの利用回数を端末内で記録・集計し、入力を画面に表示するツール | 開発版の起動・画面表示・正常停止・状態確認に対応 |
| [CapchunScreen](tools/CapchunScreen/README.md) | 画面録画・操作履歴TSV・独立プレビュー・複数動画の同時再生・Unity Capture出力を行うツール | ローカル開発版の起動・画面表示・正常停止・状態確認に対応 |
| [ChulipVideo](tools/ChulipVideo/README.md) | 動画から手順書用の画像を切り出すツール | ローカル開発版の起動・画面表示・正常停止・状態確認に対応 |
| [ChunapTool](tools/ChunapTool/README.md) | ウィンドウの位置・サイズ・最大化を調整するツール | ローカル開発版の起動・画面表示・正常停止・状態確認に対応 |
| [HttpSelector](tools/HttpSelector/README.md) | 危険なリンクを警告し、ブラウザーのプロファイルを選ぶツール | 署名付きMSIXの起動・画面表示・正常停止・状態確認に対応 |

HttpSelectorは[署名付きMSIXの導入](tools/HttpSelector/docs/INSTALL_MSIX.md)が必須です。WindowsへHTTP/HTTPSの関連付け候補を登録した後、既定のアプリ設定で利用者が選択してください。本体はMSIXの署名付き登録・Publisher・製品版を照合し、未導入や旧版を開発EXEに切り替えません。既存の本体対応Inno配布とは別の導入経路です。

ShortcutKeyCounterの本体連携は[管理連携の手順](tools/ShortcutKeyCounter/docs/kotorintoys-integration.md)を参照してください。公開済みポータブルZIPとは別の開発ソースをビルドして利用します。本体からの正式導入・更新は未対応です。単独での利用方法は[ユーザーガイド](tools/ShortcutKeyCounter/docs/users-guide.md)を参照してください。

追加3ツールは独立Gitを維持し、親は採用コミットだけを記録します。ChunapToolは[公開リポジトリ](https://github.com/KotorinChunChun/ChunapTool)と[v0.1.1ポータブル開発版](https://github.com/KotorinChunChun/ChunapTool/releases/tag/v0.1.1)を公開しました。CapchunScreenとChulipVideoの`.gitmodules`はパスと`update=none`のみを登録し、架空の公開URLを設定しません。各アプリの利用ガイドと管理連携ガイドを参照してください。本体からの正式インストール・更新は未対応です。ひとこと解説の文言の再編集は後続対応とします。

## インストーラー（開発版）

[本体のReleases](https://github.com/KotorinChunChun/KotorinToys/releases)から`KotorinToys-0.1.0-setup.exe`を取得してください。Windows 11 x64・利用者単位・ランタイム同梱の未署名開発版です。全機能の受入完了版ではありません。

本体の「導入・削除」で対象ツールを選択し「選択をインストール」を押すと、GitHub Releasesから本体に登録された固定版を取得し、製品・版・容量・SHA-256を照合して導入します。ローカル配布台帳からの導入も使用できます。設定・保存データは保持し、正常終了を拒否したツールは更新・削除しません。SSCaptureの録画用FFmpeg／FFprobeとOneOCR外部ファイルは別途必要です。

## 開発版の起動

開発中のソースでは、導入済み版／GitHub公開版／更新の要否／一言解説、全体と選択行の状態、導入完了の結果一覧、FFmpeg導入・修復を追加しています。使い方は[利用ガイド](docs/USERS_GUIDE.md)を参照してください。この変更の配布用バイナリはまだ作成しておらず、上記の公開済みインストーラーや現在の導入先には未反映です。

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
- `contracts/` — ツール情報・文書・ショートカットの共通仕様
- `tools/` — 独立Gitリポジトリとして配置する各ツール
- `docs/` — 利用・開発・運用の現行資料
- `dev/history/v0.1.0/` — v0.1.0の要求・準備計画・進捗

各ツールは単独で導入・開発・利用できる独立リポジトリとして扱います。親プロジェクトから既存ツールの内部構成を一括変更しません。

## 開発者向け情報

環境確認と資料の扱いは [開発者ガイド](docs/DEVELOPERS_GUIDE.md) を参照してください。製品要件の正本は [v0.1.0要求定義](dev/history/v0.1.0/v0.1.0-req.md) です。

## 作者

KotoriChun
