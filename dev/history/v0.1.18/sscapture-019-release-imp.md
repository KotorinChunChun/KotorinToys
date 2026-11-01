# SSCapture v0.1.9の公開と本体一覧への反映

2026-11-01の利用者指示「push・リリース・kotorintoysから入れられるようにしてください」に対応する。本体の版・配布物は変更せず、SSCaptureの公開済み配布物を本体から取得できるようにする。

## 計画

- SSCaptureの専用package入口で公開用setup・製品別台帳・SHA256SUMSを生成し、未署名prereleaseとして公開する。
- 最新origin/mainを起点にした隔離worktreeでSSCaptureのGitリンク、配信用config、同梱配布台帳だけを更新する。元の作業ツリーと他ツールの変更は含めない。
- 既存のSpkRouter向け公開ダウンロード検査を製品ID指定にも対応させ、本体と同じPublicReleaseClient/ReleaseDownloaderでSSCaptureを検証する。EXEは実行せず、通常設定・キャッシュを変更しない。
- 公開情報一覧Actionsを成功させ、匿名取得したraw一覧と配布物を照合する。既存の本体setupを作り直さず、利用者は「状態を更新」から取得できるようにする。

## 状況

- [SSCapture v0.1.9](https://github.com/KotorinChunChun/SSCapture/releases/tag/v0.1.9)を公開済み。採用ソース・Gitリンクは `90e73f63219eafedf6de674eb7ce0a4e284eb10b`。setupは66,847,503バイト、SHA-256は `b1cd45e4dd9fceca325d2e6fc6392a41f13fd5fcabc458cb4029a5f8c9d6aea6`。setup・製品別台帳・SHA256SUMSの匿名再取得が一致した。
- 本体検証CLIは警告0・エラー0、配信用configは14件の形式検査に成功。公開一覧生成試験とworkflowの初回作成・変更なし・通常更新のローカル試験は成功した。全体の `--release-index-tests` は未展開の子のtool.jsonがないため開始できず、全体回帰を成功扱いにしない。
- 本体の定義・台帳・SSCapture参照・製品ID指定の検証CLIを更新した。元の作業ツリーや他製品の採用コミットを変更しない。公開一覧Actionsと実ダウンロードの照合は続けて実施する。
- SSCaptureの本PCへのv0.1.9導入と設定・前回範囲・起動予約保持は先行工程で確認済み。今回公開したsetupは公開メタデータを含めて別に生成したため、先行候補の導入試験と同じ資産として扱わない。公開setupの再インストール、本体の入れ替え、GUI確認の再実行は今回行わない。
