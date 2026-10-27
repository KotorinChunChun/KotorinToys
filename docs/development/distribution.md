# 配布の手順と境界

[開発者ガイド](../DEVELOPERS_GUIDE.md)へ戻る。Innoの準備と具体的な生成コマンドは[インストーラー作成](../installer-build.md)、検査は[セキュリティ](../security.md)を参照してください。

## 採用するソース

親コミットと子のGitリンクを先に確認します。`stage-release-source.ps1`は採用ソースだけをUTF-8 ZIPから展開します。作業ツリーの未コミット差分や未採用の子HEADを配布入力へ混ぜません。日本語パス・パス長を考慮して短い隔離ルートを使います。

対応製品の正本は[distribution-products.ps1](../../dev/scripts/distribution-products.ps1)です。開発一覧へ登録できることと、親の生成スクリプトがその製品を作成できることは別です。

## 標準Innoの入口

| 工程 | スクリプト |
| --- | --- |
| 自己完結型発行 | publish-packages.ps1 |
| setupと固定ハッシュ台帳の生成 | build-installers.ps1 |
| Releaseの公開 | publish-releases.ps1 |

既定の発行対象は本体・LiveTextMarker・SmartPasteです。CapchunWindow／ChulipVideo／ChunapTool、EveryDB、OperationRecorderなどの指定可否・引数は各スクリプトで確認します。`test-distribution-scripts.ps1`で固定表との整合を検査できます。

publish-releases.ps1は固定アカウント・採用ソース・台帳・ハッシュを確認します。既存Releaseやassetは上書きせず、新しい配布物は新しい版へ進めます。

## 製品固有の入口

| 製品・方式 | 正本と注意 |
| --- | --- |
| SSCapture | 子のscripts/package.ps1とdev/RELEASE_RULE.md。専用maintenance／transaction付きsetupが必要。親の標準発行・生成・公開は明示指定しても拒否する |
| VoiceMixerMicRouter／SpkRouter | 各子のdev/scripts/Build-Installer.ps1。C#の発行表へ追加しない |
| EveryDB | 子のno-bundle Release入口。サンプル・利用条件・第三者表示と共通maintenanceを扱う |
| OperationRecorder | 親の単独製品指定と子のdocs/release.mdを確認する |
| HttpSelector・ScriptSync | 各子の署名付きMSIX経路を維持。汎用Innoへ変換しない |
| ポータブルZIP | [公開情報の検出](../portable-release-publication.md)と正式導入を分離する |

本体へ説明書を同梱する処理と、子のEXEを配布する処理を混同しません。FFmpeg／FFprobe・OneOCR外部ファイル・モデル・仮想音声ドライバー・利用者dataを、未確認のままsetupへ再配布しません。

## 配布物の照合

製品・数値版・AppId・所有者・tag・asset・容量・SHA-256を照合します。GitHubがdigestを返す場合は一致を要求します。同梱台帳と実公開の照合は別であり、取得失敗時の同梱版を最新公開版へ補完しません。

公開情報は[公開情報一覧](../public-release-cache.md)へ集約します。本体を配布する前に、一覧生成のActionsと専用ブランチが正常に公開されていることを確認します。

GitHubと許可されたassetホストのHTTPSだけを使い、時間・容量を制限します。利用者の確認時に固定した対象を取得し、同版内容の差し替えを拒否します。ハッシュと固定配布元の照合は独立したコード署名による発行元保証ではありません。

本体の自己更新に必要な資産・引継ぎ条件は[自己更新の配布仕様](self-update.md)を参照してください。

## 外部依存と記録

FFmpegは製品取得とは別に固定配布元・容量・ハッシュを検査します。停止中の正式SSCaptureへ必要ファイル・ライセンス等を配置し、設定・履歴・PATHを変えません。自己のReleasesへの再配布はしません。

生成・導入検証・公開のログは対象版のlogsへ保存します。`-RecordVersion vX.Y.Z`は記録先の指定であり、製品版を変更する引数ではありません。

生成、署名、外部公開、このPCへの導入・再起動は別工程です。実装や文書更新だけを許可根拠にして実行しません。
