# OperationRecorder 1.5.0の操作リプレイ採用

実施日：2026-11-01。記録をconfigへ変換し、CLI/ps1から対象ウィンドウ・クラス・UIA要素・画像条件を監視して再生する機能を子リポジトリへ実装した。

## 本体への反映

- 子の開発版は1.5.0。独立Gitを維持し、登録・管理試験の期待版を更新した。
- 採用子コミット：`79884c9161b5401192bfc56ce030e10c0875c098`。段階ごとの設計・config・Windows実行・CLIのコミットも子の履歴へ記録した。
- 手動、指定日時、日次/間隔、ホットキー、アプリ起動待ちの開始条件を実装。既存の本体/単体の自動起動やOS予約は追加・変更していない。watchは監視プロセス動作中だけ有効。
- 公開済みcatalogのOperationRecorderは1.3.2のまま。今回は実装・検証であり、インストーラー公開・配信定義更新・このPCへの導入はしていない。
- 利用手順は[子のリプレイ説明](../../../tools/OperationRecorder/docs/replay.md)、要求・詳細設計と試験は[子の版別報告](../../../tools/OperationRecorder/dev/history/v1.5.0/v1.5.0-imp.md)を正本とする。

## 本体の検証

`dotnet build tests/KotorinToys.Tests/KotorinToys.Tests.csproj -c Debug --artifacts-path artifacts/operation-recorder-replay -p:NuGetAudit=false`：警告0、エラー0。

通常catalogからの登録試験は、別作業で削除されている`tools/TimecardOCR/tool.json`が見つからず対象試験へ到達しなかった。この削除や他の変更は復元・取り込みしていない。

`logs/operation-recorder-replay/isolated-catalog`へOperationRecorderのcatalog/製品定義/必要文書だけを配置し、子プロセス限定の`KOTORINTOYS_TEST_ROOT`で同じテストEXEの`--operation-recorder-registration-tests`を実行した。19件成功。記録は`logs/operation-recorder-replay/registration.log`。本体全体の回帰や、公開版の実インストール・実運用受入を代替する結果ではない。

子の最終一括試験はCore412件＋Desktop52件の計464件成功、失敗0/未実行0。Releaseビルドは警告0/エラー0。結果は子の`dev/history/v1.5.0/logs/final-key-all/summary.json`とTRX、ケース表は同版の`test-cases.json`（24機能、291メソッド/464引数行）。Computer Useは使用せず、実入力は別PIDの専用fixtureに限定した。
