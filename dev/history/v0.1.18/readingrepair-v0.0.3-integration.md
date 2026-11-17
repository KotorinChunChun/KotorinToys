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

## このPCの実更新・導入後検証（2026-11-05）

利用者の明示指示により、公開時点で未実施だった本PCの更新を実施した。KotorinToysの公開main定義・公開一覧・製品別台帳・GitHub digestを確認し、共通ToolInstallationRunnerから公開setupを取得・実行。正式導入済み0.0.1から0.0.3へ更新し、HKCU・tool.json・実EXE版で確認した。本体や他製品の更新は含めていない。

- 既存GUIをPID照合して正常終了し、更新後は通常設定のGUIを再起動。未保存の原文151文字、指定読み、辞書候補を退避・復元・照合した。
- 有効設定の全項目、履歴7件の全内容、実ユーザー辞書400件を保持。設定・辞書・履歴等216パスの存在状態・SHA-256が更新直後・再起動後・同期検証後で一致した。通常settings.jsonが未作成で既定値を使う状態も保持した。
- 実辞書SHA-256はff97e78016e749c20eb8adc76885021c96544c1dc8030476574ca48cd06524c7で不変。
- 同期試験は正式導入先の同じEXE・バックエンドを隔離設定で起動し、開いたままのユーザー辞書画面へ別CLIから1件、続いて2件一括を追加した。約2.676秒（CLI処理を含む）で1件へ反映し、一括処理後に3件となることを実セルで確認。手動取得・タブ再選択なし。入力中の候補保持と古い確認結果の無効化も成功した。
- 隔離GUIは正常終了。通常GUIはv0.0.3で起動中で、最終原文照合も成功。実辞書へ試験語は追加していない。外部LLM・音声生成・聴取・画像での外観評価は今回の対象外。

生ログ・退避データ・HTML報告は親のdev/history/v0.1.18/logs/readingrepair-local-update-20261105/に非公開で保持する。機密の原文や履歴、辞書テーブルは本報告とコミットに含めない。公開済みタグ・アセットは変更していない。