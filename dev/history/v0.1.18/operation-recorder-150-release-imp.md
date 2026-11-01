# OperationRecorder 1.5.0の公開と本体更新一覧への反映

2026-11-01の明示依頼により、[OperationRecorder 1.5.0](https://github.com/KotorinChunChun/OperationRecorder/releases/tag/v1.5.0)を未署名prereleaseとして公開した。配布タグ・自己完結EXEの採用元は`c0a7538b9f2d7176a2b515f43df6817ffb2402b9`。子mainの後続コミットは公開結果の報告だけで、タグ/資産は変更していない。

製品コードの試験はCore412件＋Desktop52件の計464件成功。同梱.NETを公式最新修正版10.0.14へ更新し、自己完結配布入力489ファイルの秘密検査0件、非実行展開した製品489ファイル＋一時保守用197ファイルを台帳と全件照合した。配布EXEと同梱ps1のreplay import/checkも確認した。

setupは96,762,552バイト、SHA-256は`eba0a9df00cadddd3642e0c58a8bba1eb9ed86cc1077a3ce606ceb7bb4de113e`。setup、SHA256SUMS、製品別台帳の3資産を公開し、匿名取得した内容とGitHubのsize/digestが一致した。

本体公開mainの`500b252`でOperationRecorderのconfig、子参照と関連試験の期待版だけを更新した。[公開情報一覧の更新Actions](https://github.com/KotorinChunChun/KotorinToys/actions/runs/41308841512)が成功し、公開raw一覧へv1.5.0と3資産が反映された。本体と同じPublicReleaseClient/ReleaseDownloader経路で公開定義・版・日付・製品別台帳を照合し、setupを匿名再取得して容量・SHA-256を確認した。

本体の「状態を更新」後にOperationRecorderを選び、「アップデート」を使用する。公開キャッシュが最大5分残る場合がある。既存本体のsetupを作り直す必要はなく、本体やOperationRecorderのこのPCへの実導入・更新は行っていない。通常の設定・記録・OS予約も変更していない。

生成元の親コミットは作業ブランチの`01d774c`。その親全体はmainへ統合せず、公開mainの`0921c58`を起点に対象変更だけを通常pushした。ローカル検証記録は対象作業版の`dev/history/v0.1.11/logs/operation-recorder-150/`。詳細な採用元・試験結果・境界は[子の公開報告](../../../tools/OperationRecorder/dev/history/v1.5.0/v1.5.0-release.md)を参照する。本体全体回帰、異なる版からの実インストール更新とGUI受入は未実施。
