# 本体アイコンの共通規約

本体KotorinToysは、採用04「青い小鳥と赤・緑の積み木」を使う。正本は[src/KotorinToys/assets/KotorinToys.ico](../src/KotorinToys/assets/KotorinToys.ico)。マスターPNGと再生成方法は[素材の説明](../src/KotorinToys/assets/README.md)を参照する。収録ツールは各製品の固有アイコンを維持する。

| 表示箇所 | 設定・所有権 |
| --- | --- |
| 本体EXE、メンテナンスEXE、Explorer | 各csprojのApplicationIconで同じICOを埋め込む |
| 通知領域（タスクトレイ） | AppIcon.Createで埋込みリソースから生成し、トレイ終了後に破棄する |
| 管理画面、設定画面、導入画面のタイトルバー／タスクバー | BrandedFormを継承する。画面ごとのIconを基底クラスが所有・破棄する |
| 本体セットアップ／アンインストーラー | Innoテンプレートの本体条件でSetupIconFileを指定する |
| スタートメニュー／EXE参照のショートカット／削除一覧 | 各製品EXEの先頭アイコンを参照する |

新しい本体画面もBrandedFormを継承し、タイトルは[WindowTitle.Createの規約](window-title-rule.md)に従う。複数画面で同じIconインスタンスを共有・破棄しない。警告・エラーの状態を示す絵やWindows標準ダイアログはブランドアイコンに置き換えない。

## 非対話の検証

pwsh・プロジェクトルートで実行する。製品配布用のpublishやインストーラー作成は不要。

    ./dev/scripts/test-app-icon.ps1
    dotnet build tests/KotorinToys.Tests/KotorinToys.Tests.csproj -c Debug --artifacts-path artifacts/icon-check/host
    dotnet build src/KotorinToys.Maintenance/KotorinToys.Maintenance.csproj -c Debug --artifacts-path artifacts/icon-check/maintenance
    ./artifacts/icon-check/host/bin/KotorinToys.Tests/debug/KotorinToys.Tests.exe
    ./artifacts/icon-check/host/bin/KotorinToys.Tests/debug/KotorinToys.Tests.exe --verify-icon-exe ./artifacts/icon-check/host/bin/KotorinToys/debug/KotorinToys.exe ./artifacts/icon-check/maintenance/bin/KotorinToys.Maintenance/debug/KotorinToys.Maintenance.exe

資産テストは10サイズの寸法・透過・色を検査する。共通回帰は全独自画面のIconと所有権、埋込みリソース、プロジェクト／インストーラーの参照を検査する。PE検査はEXEを実行せず、Win32資源をデータとして読み、全10フレームを元ICOとバイト照合する。根拠：[LoadLibraryExの資源読込み](https://learn.microsoft.com/en-us/windows/win32/api/libloaderapi/nf-libloaderapi-loadlibraryexw)、[SetupIconFile](https://jrsoftware.org/ishelp/topic_setup_setupiconfile.htm)。

実導入後のトレイ／タイトルバー／高DPI表示／Explorerキャッシュの見た目は別途受入確認する。ソース変更だけでは導入済みEXEや既存ショートカットを更新しない。配布用成果物・実導入・キャッシュ操作・再起動は、その作業への指示を受けて行う。
