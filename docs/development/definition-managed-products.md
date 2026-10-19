# 定義だけで追加する標準管理製品（開発者向け）

この拡張を含む本体へ初回更新した後、共通契約に対応するアプリの追加・説明変更・版更新に本体の再ビルドは不要。候補は[config/tools.json](../../config/tools.json)、公開版は各GitHub Releasesから取得する。公開mainへの反映と利用中本体の更新は別の工程であり、ローカル修正だけで公開済み旧本体が対応するわけではない。

## 新規製品の契約

- 所有者はKotorinChunChunに限定。`repositoryUrl`に実在するHTTPSリポジトリのトップを宣言する。製品名からURLを推測しない。
- `id`は永続キー、`name`はEXEの製品名、`appId`は`KotorinToys.<name>`、`installedExecutable`は`<name>.exe`。既存製品の名前・AppIdを別IDへ流用しない。
- 共通管理契約2のstatus／show／stop／silent-startを実装する。固定CLI引数、同一利用者・セッション・PID照合、正常終了の拒否を維持する。
- Innoの現在ユーザー登録`<appId>_is1`とInstallLocation、導入先のtool.json、EXE製品名／版、unins000.exeを整える。
- 公開は`vX.Y.Z`、`<name>-X.Y.Z-setup.exe`、`<name>-packages.json`。JSONはschemaVersion 1／publishedの製品別台帳にし、製品・AppId・版・ファイル名・SHA-256を一致させる。SHA256SUMSも添付する。
- 単体setupの更新・削除補助は、同梱tool.jsonから新規製品を照合できる共通Maintenanceまたは同等の契約が必要。同梱tool.jsonにもrepositoryUrlと共通管理能力を宣言する。既存データ・履歴は配布物に含めず保持する。

`distributionStatus: unpublished`は候補・使い方・GitHubの案内専用。publishedでも台帳・配布実体を照合できるまでインストールを許可しない。新しいインストーラー形式や未知のMSIX署名・信頼条件は、定義追加だけでは実行できない。HttpSelectorの固定署名付きMSIX経路は従来どおり検証する。

## 信頼とデータの受渡し

ProductIdentityの既存表は後方互換の既定値として残す。新規Innoは検証したToolDefinitionを公開情報・台帳・取得・導入・更新・削除へ明示的に渡す。プロセス全体の許可表を書き換えず、定義を渡さない未知IDは拒否する。

許可するのは宣言された所有者／リポジトリ／tag／assetだけ。製品名・AppId・容量・SHA-256とGitHub digest、同版差替拒否、パス越境防止を維持する。固定表の既存IDも、別製品へ差し替えられない。

採用後のID・製品名・AppId・MSIX識別・宣言済み公開先は同じ製品へ固定する。新規管理製品は公開先を一旦空にして差し替えることも拒否する。非表示は配列から除外し、旧登録は予約照合・解除用に保持する。

一覧キャッシュと設定・履歴は別データ。オフライン・不正JSON・不正な製品差替・保存失敗では最後の正常一覧を保つ。設定の正本と通信上限は[一覧更新仕様](../online-tool-catalog.md)を参照する。

## 検証

本体テストEXEの`--definition-product-tests`は架空の新規製品・模擬HTTP・無害なテスト配布物で、定義更新、版照合、容量／ハッシュ検証、導入部への引渡し、正常停止を伴う更新、予約解除、フォーム導線、差替・改変拒否を検証する。実インストーラー・実アプリ・利用者設定・OS予約は変更しない。
