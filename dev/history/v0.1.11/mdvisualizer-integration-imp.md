# MdVisualizerのコレクション追加

2026-10-25。依頼：`tools/MdVisualizer`を他ツールの規約に沿って追加し、必要な引数とコマンドライン試験を整備する。常駐機能は不要。

## 計画と進捗

- [x] 親子Git、カタログ、起動・タイトル・管理契約を調査。
- [x] フェーズ1：独立子Gitに通常起動、管理IPC、安全な終了、試験用設定分離と利用手順を実装し、Rust／frontendの近接試験・ビルド後にコミット。
- [x] フェーズ2：本体へ通常起動能力とMdVisualizerの定義を追加。自動起動は許可しない。登録・回帰・実プロセス連携を検証しコミット。

## 契約

- 常駐・トレイ・自動起動予約を追加しない。通常起動と再起動は画面を表示する。最後のウィンドウを閉じると終了。
- `status/show/stop`は現在ユーザーSIDの管理pipeで応答し、PID・セッション・期限を検証。終了は全画面の最新状態を照会し、未保存・保存中・応答なしでは拒否し、既存の終了ガードを経由する。
- `--launch-origin`、`--settings-dir`を文書引数から分離する。従来の文書引数、複数画面、単一起動、固有アイコンを維持。
- 本体の`start`能力を`silent-start`と区別する。MdVisualizerは`silent-start`を持たず、自動起動の対象外。既存14製品の起動引数は維持する。
- 外部公開・導入済みアプリ更新は行わない。開始時から存在するREADME・旧版資料・他ツールのGit差分は変更対象外。

## 検証結果

- 子：frontend 2ファイル33件、Rust 199件成功・2ignored。型・共有投影・UI契約、frontend build、Rust Debug build成功。移動前のパスを指すGit対象外junction計5件を現配置へ修復。
- 親：登録22件、操作可否112件、オンライン一覧160件、ローカル一覧16件、自動起動画面28件成功。Debug build成功。NuGet脆弱性情報を取得できないNU1900警告あり。
- 実プロセス連携19件成功。起動・同じPIDの表示・最小化復元・二重起動・文書起動での2画面作成・全画面正常終了・再起動・最終画面終了後のプロセス終了を確認。誤PID／PID欠落／未対応操作／自動起動経路は拒否する。
- 起動中のMSIX版（開始時PID 32724）を検出した初回連携試験は中止。`--settings-dir`時の単一起動識別子とpipeを設定パスのハッシュで分離し、既存版を保持したまま19件を実行した。
- 全体回帰は既存CapchunWindow（ソース名CapchunScreen）の`ManagementEndpoint.cs`と共通コピーの不一致で失敗。今回の対象外差分を変更して通過させていない。独立した上記の関連試験は成功。
- 通常の親試験出力先は既存の本体試験プロセスと重なったため、一度MSB3021となった。既存試験の終了後に同じ隔離出力先へ再ビルドして成功。製品の実行中EXEは上書きしていない。

親ログは`logs/mdvisualizer-20261025/`。全体回帰：`full-regression.log`、連携：`ipc-isolated-final.log`。子の詳細は[子実装報告](../../../tools/MdVisualizer/dev/history/v0.0.19/kotorintoys-integration-imp.md)。GUIの目視・実編集による未保存拒否・SharePoint・公開物の受入はコマンドライン検証とは別とする。既存のMSIX・本体導入先・OS予約は更新していない。

## 再実行

親で`pwsh -NoProfile -File dev/scripts/Test-MdVisualizer.ps1`。ビルド済みの登録・プロセス連携だけなら`-NoBuild`。ローカル一覧のデバッグ表示は`artifacts/mdvisualizer/bin/KotorinToys/debug/KotorinToys.exe --local-catalog`。

## 後続の全体回帰（2026-10-25）

ユーザーの追加指示により管理契約の正本と照合を修正し、製品の安全対策を保持した。さらに全体試験のURL未設定通知を非対話で検証するよう整備し、1,202件すべて成功・終了コード0で完了した。上記の回帰停止は当初の結果として保持する。[管理契約修正と最終回帰の報告](management-contract-regression-imp.md)を参照。
