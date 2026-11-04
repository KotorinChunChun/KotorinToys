# ReadingRepair v0.0.3 導入・更新連携

依頼日: 2026-11-05。親の採用元: 公開main 2beb035。親自体の版・配布物と他製品は変更しない。

## 計画

- [x] ReadingRepairだけのconfig版・子Gitリンク・実接続CLIの期待版を0.0.3へ更新する。
- [x] 関連回帰と公開mainの一覧・製品別配布台帳・GitHub digestを照合する。
- [x] 公開情報一覧Actions反映後、新規導入と0.0.1／0.0.2→0.0.3の更新対象、本体の匿名ダウンロード・容量・SHA-256を検証する。

CLIのdownloadはインストーラーを実行しない。通常インストール・既存の設定・履歴・自動起動予約は変更しない。生ログ: logs/readingrepair-v0.0.3/。

ローカル検証: 公開一覧252件、定義ベース45件、導入モード36件、配布取得43件、計376件成功。validate／連携CLIビルド成功。子の採用はv0.0.3タグbbffe3c。公開情報一覧と実download検証はRelease公開後に行う。


公開確認完了: Release v0.0.3の最終setupは235,086,040 bytes、SHA-256 a1b05356e21860bb9c8815f7d377cde16e595737e65c97b3bcd017c17e8856c6。ZIPとsetupは認証なしで再取得し一致。最終候補の変更はガイド1ファイルだけで、コードは検証済みのまま保持した。

公開main ffcb8f5の更新でActions 41874743483が成功した。本体のReadingRepair.Acceptance download 10項目が成功し、main定義・公開一覧・製品別台帳・GitHub digest・導入許可・未導入からの導入／0.0.1・0.0.2から0.0.3への更新対象表示・匿名setup取得・容量／SHA-256を確認した。

証跡: logs/readingrepair-v0.0.3/public-download.log、public-download/publication.json、public-download/download-result.json、release-index-workflow.json。インストーラー実行はfalse。通常設定・履歴・予約と本体自体の配布物・他製品を変更していない。最終配布検査は同フォルダーのportable-final-security／setup-final-security／final-payload-comparison.jsonを参照。
