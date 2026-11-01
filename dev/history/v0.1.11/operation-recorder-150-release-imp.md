# OperationRecorder 1.5.0の公開と本体からの更新

2026-11-01の明示依頼により、子の1.5.0を公開し、本体の公開情報へ反映する。既存のインストール済みアプリやOS予約は変更しない。

- [ ] P1：採用親・子ソースを隔離し、自己完結発行、setup生成、非実行展開と全件ハッシュ照合を行う。
- [ ] P2：新規Releaseを公開し、匿名再取得・製品別台帳・容量/SHA-256を照合する。
- [ ] P3：公開mainのOperationRecorder定義とrelease-indexを更新し、本体PublicReleaseClient/ReleaseDownloaderで実取得を確認する。

製品コードのCore412件＋Desktop52件成功は[実装時の報告](operation-recorder-replay-imp.md)を採用する。公開受入の期待版を1.5.0に更新し、試験ログを現在の本体記録版へ限定する。公開受入は専用キャッシュを使い、通常の利用者キャッシュを編集しない。

作業開始時の親HEADは4902e81、子の機能実装は79884c9。親の既存README/旧版報告/別版資料と他製品の変更は対象外。TimecardOCRが未配置のため、標準stageと同じGit archive方式で親とOperationRecorderだけを抽出し、対象を限定したrelease-source.jsonを作成する。
