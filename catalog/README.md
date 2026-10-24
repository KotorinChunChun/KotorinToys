# ツールカタログ

開発用の採用一覧と、同梱する固定版の配布情報を置きます。追加／更新は[管理アプリの共通手順](../docs/development/managed-app-integration.md)から確認してください。

## 正本の分担

| ファイル | 役割 |
| --- | --- |
| [tools.json](tools.json) | schemaVersion 2。親が採用する子tool.jsonへの参照。開発一覧として使う |
| 各子のtool.json | schemaVersion 1。製品名・版・管理能力・開発／導入EXE・文書・依存物を宣言する |
| [配信用config](../config/tools.json) | schemaVersion 1。利用者がGitHubから取得する自己完結した一覧。本フォルダーの一覧とは別に更新する |
| [releases.json](releases.json) | 同梱の検証対象固定版。最新公開版の確認は各製品のReleasesから行う |
| [host.json](host.json) | KotorinToys本体の製品・版の情報 |

対象製品と件数の正本はJSONです。本書へ固定の製品一覧・「未公開」の状況を複写しません。

## 登録方針

- 実在する配布元、導入検出、実行対象、ランタイム、起動・終了方法を確認してから登録する。
- 未調査の項目は空欄または未調査として扱い、推測値で補完しない。
- 各ツールの単体利用を維持し、親プロジェクトから内部設定やフォルダーを一律に作り替えない。
- 子の独立Gitを維持し、親は採用コミットをgitlinkで記録する。製品名・ID・AppId・ソース配置の既存例外を保つ。

## 公開・導入状態の扱い

`distributionStatus`は本体対応Inno配布の宣言であり、GitHub上の公開物の有無や、このPCへ導入されていることの検出結果ではありません。`published`でも台帳・実配布物の照合前に実行を許可しません。ZIP公開と本体からの導入対応は[別判定](../docs/portable-release-publication.md)です。

HttpSelectorの `msix` はPackage Family・Publisher・Application Idを宣言します。本体はOS登録と署名を専用経路で照合し、MSIX未導入・旧版・確認不能を開発EXEへのフォールバックで隠しません。`distributionStatus: unpublished`から「MSIXも未公開」と推測しないでください。[MSIX管理](../docs/msix-management.md)を参照してください。

設定だけで追加できる条件と未対応形式の境界は[標準管理製品の契約](../docs/development/definition-managed-products.md)、配信方法は[GitHub設定の運用](../docs/online-tool-catalog.md)を正本とします。
