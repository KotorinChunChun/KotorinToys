# ReadingRepair v0.0.2の導入・更新連携

依頼日: 2026-11-04。親の採用元: 公開main 61700b9。親アプリ自体の版と公開配布物は変更しない。

## 要求と計画

- [x] ReadingRepairだけのconfig版・公開台帳経路・子Gitリンクを更新する。
- [x] 一覧configと配布台帳の検証、公開情報の取得・新規導入／0.0.1からの更新表示をCLIで確認する。
- [x] 公開main反映後、公開情報一覧Actionsを更新し、匿名ダウンロードの容量・SHA-256を本体の実コードで確認する。

## 境界

現在のPCの通常インストール先は更新しない。downloadモードは導入可能なsetupを本体共通経路で取得し、製品・版・容量・SHA-256を検証するが、setupを実行しない。配布物のWPF管理IPCはReadingRepair側で隔離検証する。

## 結果

作業中。生ログはdev/history/v0.1.18/logs/readingrepair-v0.0.2/へ保存する。

事前検証で判明: ReadingRepairは定義追加製品であり、固定組込み製品の検証を使う同梱fallback台帳へ追加すると、定義を渡さない旧経路が「未登録の配布製品」で停止する。ReadingRepairの同梱fallbackだけを除き、公開一覧と製品別台帳を照合する標準の定義ベース経路へ統一する。公開確認の失敗を同梱版へ補完しない。公開版取得と新規導入／更新を専用の実接続CLIで検証する。

ローカル検証: 公開一覧252件、定義ベース製品45件、導入モード36件、配布取得43件、計376件成功。連携CLIのビルド・validate成功。新規cloneでもReadingRepairの公開先を解決できるよう.gitmodulesを追加。子はv0.0.2タグのf84ba33を採用する。公開後のonline／downloadは次工程。

公開確認完了: ReadingRepair v0.0.2のZIP・setup・台帳とSHAを正式公開。親main 277a695のconfig更新により、公開情報一覧Actions 41827774321が成功した。通常の公開URLでReadingRepair定義0.0.2とRelease v0.0.2を検出。

ReadingRepair.Acceptance downloadの9項目が成功し、公開mainの定義採用、公開一覧／製品別台帳／GitHub digest、新規導入と0.0.1→0.0.2の更新表示、匿名ダウンロードの容量・SHA-256を確認。取得したsetupは235,068,144 bytes、SHA-256 c693318b67cf92f969f0ef2e028bc191aaeef2a22ea828ced8c7371b373e4cc7。

生ログ: logs/readingrepair-v0.0.2/public-download.log、public-download/publication.json、public-download/download-result.json、release-index-workflow.json。CLIはインストーラーを実行していない。既存PCの通常版は更新せず、設定・履歴・自動起動予約を変更していない。独立した子の版別報告はReadingRepair/dev/history/v0.0.2/v0.0.2-imp.md。親自体のリリース・他製品の公開変更は行っていない。
