# AgentTasktrayViewerの開発連携

`tools/AgentTasktrayViewer`は独立Gitのサブモジュール。現在は公開remoteのない`C:/develop/AgentTasktrayViewer`を参照しているため、別PCではそのローカルパスから初期化できない。公開リポジトリを確定するまではローカル開発用として扱う。

ローカル登録は`catalog/tools.json`と子の`tool.json`、配信用候補は`config/tools.json`。配布は未公開、GitHubリンクは未設定。既存のMSIXを本体のInno配布物として扱わない。

通常起動は設定画面を開き、`--startup`／`--hidden`は無表示で常駐する。本体のstatus/show/stopと`--launch-origin`に対応。設定先分離の`--settings-dir`と受信・CLI取得を止める`--passive`は隔離試験用。詳細は[子の連携仕様](../../tools/AgentTasktrayViewer/docs/kotorintoys-integration.md)。

```powershell
cd C:\develop\KotorinToys
dotnet build tools/AgentTasktrayViewer/src/AgentTasktrayViewer.App/AgentTasktrayViewer.App.csproj -c Debug
dotnet build tests/KotorinToys.Tests/KotorinToys.Tests.csproj -c Debug --artifacts-path artifacts/agent-tasktray-viewer
./artifacts/agent-tasktray-viewer/bin/KotorinToys.Tests/debug/KotorinToys.Tests.exe --validate-online-catalog config/tools.json
./artifacts/agent-tasktray-viewer/bin/KotorinToys.Tests/debug/KotorinToys.Tests.exe --agent-tasktray-viewer-registration-tests
./artifacts/agent-tasktray-viewer/bin/KotorinToys.Tests/debug/KotorinToys.Tests.exe --agent-tasktray-viewer-ipc
```

IPC試験は開始時に既存プロセスがないことを確認し、版別logsへ設定を隔離する。OSの予約を変更せず、直接起動した試験用PIDだけを後始末する。試験中の画面ハンドル・タイトルはコマンドラインから照合する。GUIの目視受入・公開・実インストール試験ではない。

公開一覧の取得結果でローカル追加が隠れる場合は、開発用のローカル一覧指定で起動する。

```powershell
cd C:\develop\KotorinToys
dotnet run --project src/KotorinToys/KotorinToys.csproj -- --local-catalog
```
