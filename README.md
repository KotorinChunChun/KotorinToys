# KotorinToys

KotoriChunが開発・管理するWindows向けデスクトップ便利ツールを、導入・起動・状態確認・自動起動・使い方への導線とともに管理する本体プロジェクトです。

## プロジェクトの状態

LiveTextMarker、SmartPaste、SSCaptureを登録し、本体の一覧から起動、既存画面の表示、正常停止、状態確認、使い方の表示を行えます。現在は開発中です。ソース公開は製品の正式リリースや全機能の受入完了を意味しません。

## ソースの取得

```powershell
git clone --recurse-submodules https://github.com/KotorinChunChun/KotorinToys.git
```

収録3ツールも、それぞれの独立リポジトリから本体が採用した版を取得します。製品インストーラーのGitHub Releases公開はまだ行っていません。

## 開発版の起動

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
