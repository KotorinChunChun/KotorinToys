# OperationRecorder 1.5.0の公開と本体からの更新

2026-11-01の明示依頼により、子の1.5.0を公開し、本体の公開情報へ反映する。既存のインストール済みアプリやOS予約は変更しない。

- [x] P1：採用親・子ソースを隔離し、自己完結発行、setup生成、非実行展開と全件ハッシュ照合を行う。
- [x] P2：新規Releaseを公開し、匿名再取得・製品別台帳・容量/SHA-256を照合する。
- [x] P3：公開mainのOperationRecorder定義とrelease-indexを更新し、本体PublicReleaseClient/ReleaseDownloaderで実取得を確認する。

製品コードのCore412件＋Desktop52件成功は[実装時の報告](operation-recorder-replay-imp.md)を採用する。公開受入の期待版を1.5.0に更新し、試験ログを現在の本体記録版へ限定する。公開受入は専用キャッシュを使い、通常の利用者キャッシュを編集しない。

作業開始時の親HEADは4902e81、子の機能実装は79884c9。親の既存README/旧版報告/別版資料と他製品の変更は対象外。TimecardOCRが未配置のため、標準stageと同じGit archive方式で親とOperationRecorderだけを抽出し、対象を限定したrelease-source.jsonを作成する。

## 生成・公開結果

初回の発行は、子が固定した.NET 10.0.13が公式最新10.0.14より古く、既存の配布ゲートで停止した。自己完結のCore/Desktopを10.0.14へ更新し、Releaseビルド（警告0/エラー0）後に子`c0a7538b9f2d7176a2b515f43df6817ffb2402b9`・親`01d774c`から再生成した。停止した旧入力は公開していない。

製品489ファイルの秘密検査0件。非実行展開した全489ファイルと保守用197ファイルは容量/SHA-256が台帳と一致し、展開後の秘密検査も0件。同梱のreplay用config例・ps1・文書・ライセンスを確認し、配布EXEのimport/check、同梱ps1のcheckも成功した。

[OperationRecorder 1.5.0](https://github.com/KotorinChunChun/OperationRecorder/releases/tag/v1.5.0)を2026-11-01 21:55:59（日本時間）に未署名prereleaseとして公開。setupは96,762,552バイト、SHA-256は`eba0a9df00cadddd3642e0c58a8bba1eb9ed86cc1077a3ce606ceb7bb4de113e`。SHA256SUMS/製品別台帳も公開し、匿名再取得・GitHubのdigestと一致した。既存1.3.2や新規1.5.0のタグ/資産は差し替えていない。

公開本体mainは別worktreeで既存`0921c58`から`500b252`へ通常pushした。OperationRecorderのconfig、子参照と関連する3試験だけを反映し、現在の作業ブランチ全体は公開していない。[release-index更新Actions](https://github.com/KotorinChunChun/KotorinToys/actions/runs/41308841512)が成功し、公開raw一覧にv1.5.0と3資産が存在することを確認した。

本体テストEXEの`--operation-recorder-public-acceptance`で匿名の公開config→PublicReleaseClient→製品別台帳→ReleaseDownloaderを通し、実setupの容量・SHA-256照合に成功した。本体の「状態を更新」後に対象を選ぶと「アップデート」できる。既存の公開キャッシュは最大5分残る場合がある。

本体のビルドは警告0/エラー0、対象だけの隔離登録試験19件成功。本体全体の回帰、インストール済みPCの実更新・GUI受入は実施していない。利用者の設定・記録・OS予約と他製品の作業差分を保持した。

記録は`logs/operation-recorder-150/`のsource/receipt/公開取得結果、生成ログはsource.jsonが示す隔離元内の版別logs。詳細は[子の公開報告](../../../tools/OperationRecorder/dev/history/v1.5.0/v1.5.0-release.md)を参照。
