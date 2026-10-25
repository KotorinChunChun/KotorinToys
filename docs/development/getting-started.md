# ソース取得と開発起動

[開発者ガイド](../DEVELOPERS_GUIDE.md)へ戻る。配布版を使うだけの場合は[利用者向けの導入](../../README.md)を参照してください。

## 前提環境

- Windows 11 x64、PowerShell 7（コマンドはpwshで実行）。
- 本体はC#／.NET 10／Windows Forms。ビルドには.NET 10 SDKを使います。
- 子アプリの前提は各子のREADME・開発者ガイドを確認します。Rust製品にはMSVC、Web画面のある製品にはNode.js、WebView2等が必要です。
- 依存復元・モデル等の取得とビルド、アプリの起動、インストール、公開を別工程として扱います。

## ソース取得

本体と子は独立したGitです。親が記録した採用コミットを取得し、子の先行HEADへ無条件に更新しません。

```powershell
git clone https://github.com/KotorinChunChun/KotorinToys.git
cd KotorinToys
git submodule update --init --recursive -- `
  tools/LiveTextMarker tools/SmartPaste tools/SSCapture tools/ShortcutKeyCounter `
  tools/CapchunScreen tools/ChulipVideo tools/ChunapTool tools/HttpSelector `
  tools/EveryDB tools/VoiceMixerMicRouter tools/VoiceMixerSpkRouter `
  tools/OperationRecorder tools/ScriptSync
```

取得先の正本は[.gitmodules](../../.gitmodules)です。TimecardOCRには取得元URLが未設定のため上のコマンドへ含めていません。全体ビルドやローカル開発一覧を使う場合は、提供されたTimecardOCRの独立ソースも`tools/TimecardOCR`へ準備してください。公開URLを推測して登録しません。未登録アプリは自動取得しません。

公開先で採用コミットを取得できない場合は、そのコミットの公開状況を確認します。別実装や別リポジトリへの置換で回避しません。CapchunScreenフォルダーの製品名はCapchunWindow、C#版ChulipVideoの公開先はChulipVideo-CSharpです。

## 変更前の確認

```powershell
cd C:\develop\KotorinToys
git status --short
git diff --check
```

子を変更する場合は、その子のGit状態とAGENTS.mdも確認します。子の変更を子でコミットし、親は確認したコミットをGitリンクとして採用します。既存差分を一括でステージしません。

## 本体だけのビルド

```powershell
cd C:\develop\KotorinToys
dotnet build src/KotorinToys/KotorinToys.csproj -c Debug
```

子のEXEは生成しません。未導入・EXEなしなどの操作可否と、製品ごとの実機確認を区別してください。

## 子を含む全体ビルド

```powershell
pwsh -NoProfile -File dev/scripts/build.ps1
```

対象と出力先は[build.ps1](../../dev/scripts/build.ps1)と各子のtool.jsonを正本とします。この入口は子の専用ビルドも呼びます。出力先のEXEが実行中なら、利用者の作業を確認して正常終了してから実施します。勝手に強制終了しません。配布用setupの生成・公開ではありません。

## 専用設定での開発起動

```powershell
pwsh -NoProfile -File dev/scripts/start.ps1 -LocalCatalog
```

ローカル一覧にある各子の定義ファイルを準備してから使用します。版別logs内に専用の設定先を作り、ローカルのcatalog/tools.jsonを使います。「状態を更新」でもGitHubの一覧へ置換せず、通常利用の一覧キャッシュや起動予約を変更しません。

本体の単一起動制御は共通なので、既存本体が起動中なら既存画面を表示します。ローカル一覧へ切り替えるときは本体を正常終了してから起動してください。本体を閉じても子アプリは終了しません。

引数なしのstart.ps1は通常の配信用一覧を使います。開発時の隔離と製品の通常起動を混同しません。

## 検証へ進む

通常のコード検証は[管理機構の検証](../testing.md)、子の起動・表示・正常停止の受入は[連携検証](tool-integration.md)を参照してください。GUI・音声・OCR・録画・サインインは、ビルド成功だけで確認済みにしません。