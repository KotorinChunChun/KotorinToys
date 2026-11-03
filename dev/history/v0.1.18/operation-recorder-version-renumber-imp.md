# OperationRecorderの版番号訂正

2026-11-03。利用者指定で1.3.2→0.1.2、1.4.0→0.1.3、1.4.1→0.1.4、1.5.0→0.1.5、1.6.0→0.1.6へ訂正した。子の元履歴29コミットを加工し、3,346ファイルスナップショットの比較で版番号/パス以外が一致、バイナリ変更0件を確認した。元Git履歴と旧Release全資産は非公開のローカル復元領域へ保存した。

子のmainとタグを元SHAのforce-with-lease・atomicで訂正した。新番号の0.1.2/0.1.5/0.1.6は、それぞれの対応ソースからsetupを作り直し、全ファイルを非実行展開・照合した。再発行時の同梱.NETはすべて10.0.14。配布入力484/489/494ファイルの秘密検査は検出0件。

[0.1.2](https://github.com/KotorinChunChun/OperationRecorder/releases/tag/v0.1.2)、[0.1.5](https://github.com/KotorinChunChun/OperationRecorder/releases/tag/v0.1.5)、[0.1.6](https://github.com/KotorinChunChun/OperationRecorder/releases/tag/v0.1.6)の全9資産を匿名取得し、容量・GitHub digest・製品別台帳・SHA256SUMSを照合した。その後、退避済みの旧番号タグ/Releaseを整理した。いずれも未署名prerelease。

公開mainの`9c8699d`でOperationRecorderだけの定義・子参照・試験期待版を0.1.6へ更新した。[公開一覧の更新](https://github.com/KotorinChunChun/KotorinToys/actions/runs/41559806543)が成功し、匿名のrelease-indexには新番号3版だけが載る。本体PublicReleaseClient/ReleaseDownloaderで最新版0.1.6の検出、台帳、公開日、setup匿名取得とサイズ・SHA-256の一致を確認した。

最新setupは96,780,579バイト、SHA-256は`87fd284197def10ff0cd44b6813488c065774531080485decda2c7d86d8f09c8`。親・子のコンパイルは警告0/エラー0。機能動作テスト・GUI・Codex生成・現PCの再導入は利用者指定の範囲外として実施していない。旧1.x系を導入済みの場合、自動ダウングレードはせず、必要に応じて新番号setupを明示して再導入する。

詳細は[子の番号訂正報告](../../../tools/OperationRecorder/dev/history/v0.1.6/v0.1.6-renumber.md)と[製品版の番号](../../../tools/OperationRecorder/docs/version-numbering.md)を参照する。本体の他製品を含む履歴全体は書き換えず、対象の参照・公開定義だけを通常pushした。
