# OperationRecorderの製品版番号訂正

2026-11-03。利用者の明示指定で、1.3.2→0.1.2、1.4.0→0.1.3、1.4.1→0.1.4、1.5.0→0.1.5、1.6.0→0.1.6へ訂正する。GitHubの履歴/公開情報を含め、現PCへの再導入は対象外。

- [x] 元履歴を完全bundleへ、旧Release2件・全6資産をローカルへ退避。
- [x] 子の全29コミットで版、文書パス、コミット文を訂正。既存タグはv0.1.2/v0.1.5へ対応付け、両worktreeを同期。fsckと全履歴の製品版を検査。
- [x] 新番号の過去公開版2件と現行0.1.6を再発行し、配布入力/EXE/台帳/ハッシュを照合。
- [x] 子の公開main/タグ、Release、本体公開config・採用参照・release-indexを更新し、匿名取得を確認。

元履歴・旧資産・対応表は`artifacts/operation-recorder-renumber-b44a0e70/`に保持する。親は他製品の履歴全体を強制更新せず、OperationRecorderの参照と定義を通常コミットで更新する。既存の他作業の差分は含めない。機能動作テスト/GUI/Codex生成は前回の指定を継承して実施しない。

子の最新製品とテストプロジェクトはReleaseコンパイルで警告0/エラー0。過去の実装/実行報告は当時の証跡として保持し、番号訂正後の配布物とはハッシュ等を区別する。詳細は[子の番号訂正報告](../../../tools/OperationRecorder/dev/history/v0.1.6/v0.1.6-renumber.md)。

元履歴との比較で29コミット/3,346ファイルスナップショットが番号・パス変更だけで一致し、バイナリ変更0件。親のDebugコンパイルも警告0/エラー0。

0.1.2/0.1.5/0.1.6のsetupを生成し、各484/489/494ファイルと保守用各197ファイルを非実行展開で照合した。すべて.NET10.0.14を同梱し、旧版の固定ランタイムは隔離発行側で上書きした。生ログ・採用元・overrideのハッシュ・公開候補台帳は`logs/operation-recorder-renumber/`へ保存した。

## 公開結果

子mainとタグを元SHAのlease付きatomic pushで訂正。新番号の3Release・全9資産を匿名取得して照合し、退避済みの旧番号Release2件を削除した。現在の公開版は[v0.1.2](https://github.com/KotorinChunChun/OperationRecorder/releases/tag/v0.1.2)、[v0.1.5](https://github.com/KotorinChunChun/OperationRecorder/releases/tag/v0.1.5)、[v0.1.6](https://github.com/KotorinChunChun/OperationRecorder/releases/tag/v0.1.6)。

本体公開mainは新しい`10efefd`から対象変更だけを採用し、`9c8699d`として通常pushした。他製品の履歴や作業ブランチ全体は書き換えていない。[公開一覧更新Actions](https://github.com/KotorinChunChun/KotorinToys/actions/runs/41559806543)が成功し、公開rawのOperationRecorder一覧には訂正後3版だけが存在する。

本体のPublicReleaseClient/ReleaseDownloader経路で公開定義0.1.6、公開日、製品別台帳、setupの匿名取得と容量・SHA-256を照合した。`public-acceptance.log`と同受入の`receipt.json`に記録。最新setupは96,780,579バイト、SHA-256は`87fd284197def10ff0cd44b6813488c065774531080485decda2c7d86d8f09c8`。

今回の範囲は版番号・履歴・配布/公開情報の訂正。現PCの旧番号1.6.0、利用者設定、記録、OS予約は変更せず、機能動作テストも未実施。
