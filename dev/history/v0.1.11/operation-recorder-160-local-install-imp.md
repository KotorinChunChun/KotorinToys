# OperationRecorder 1.6.0の現PC更新

利用者の明示指示を受け、既存の1.3.2から今回の開発版1.6.0へ更新し、画面を表示して起動状態にした。

- 採用親`acd1c66`・子`979a389`の確定ソースから、対象だけを隔離発行した。
- ローカルsetup生成、配布入力493ファイルの秘密検査0件、導入後の全493ファイルのハッシュ照合に成功。
- setupのSHA-256：`35cc963a7a1caf2bc60466231dd3bcdf74eae87821161c03790afbaefb7be54d`。
- 旧版と設定をLocalAppDataの非公開領域へバックアップ。旧版PID2368の正常終了を確認してから、同じ`%LOCALAPPDATA%/Programs/OperationRecorder`へ更新した。
- 新PID35652の実EXE、採用SHA、管理statusのready/1.6.0/host_manual、表示済みMainWindowHandleとタイトル`OperationRecorder - ver 1.6.0`を確認。
- 設定と記録38ファイルはハッシュ一致。既存OS予約と本体の自動起動設定も保持した。本体・他製品の停止や更新は行っていない。

生ログ・導入receiptは`logs/operation-recorder-160-local-2069a562/`。設定内容や記録ファイル名を含む台帳は利用者の非公開バックアップ領域だけへ置き、Gitや版別ログに収録していない。

公開済み1.5.0のReleaseや本体公開configは変更せず、今回は現PCの差し替えだけ。機能回帰、Codex生成、操作リプレイは前回指示に従い実施していない。詳細は[子の現PC更新報告](../../../tools/OperationRecorder/dev/history/v1.6.0/v1.6.0-local-install.md)を参照。
