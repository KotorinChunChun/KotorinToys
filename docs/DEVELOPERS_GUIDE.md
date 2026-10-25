# KotorinToys 開発者ガイド

利用者の導入・操作は[README](../README.md)と[使い方](USERS_GUIDE.md)、説明書全体の入口は[目次](README.md)を参照してください。本書は開発・保守資料の入口です。

## 環境準備・ビルド

- [ソース取得と開発起動](development/getting-started.md)：前提環境、独立Gitの取得、本体／子アプリのビルド、専用設定。
- [コードの責務](architecture.md)・[本体の構成要素](development/host-components.md)：変更箇所と互換性境界。
- [管理連携と製品別の検証](development/tool-integration.md)：IPC・起動停止の検証入口と実機受入の区分。

## アプリの追加・更新

| 確認すること | 正本 |
| --- | --- |
| 起動引数・管理IPC・文書の契約 | [共通契約](../contracts/README.md) |
| 定義だけで追加できる製品の条件 | [標準管理製品](development/definition-managed-products.md) |
| 配信用の一覧・並び順・説明 | [GitHub configの運用](online-tool-catalog.md) |
| ボタンを押せる条件と理由 | [操作可否](development/operation-availability.md) |
| 自動起動の所有・判定・復旧 | [自動起動の実装](development/startup-settings.md) |
| 画面タイトル・アイコン | [タイトル規約](window-title-rule.md)・[アイコン規約](app-icon-rule.md) |

子の`tool.json`と採用コミット、親の`catalog/tools.json`、配信用`config/tools.json`を区別します。共通契約に適合したアプリの追加・更新は、対応済み本体なら定義とReleaseの更新で反映できます。新しい配布方式・通信・信頼条件には本体側の実装が必要です。

## 検証・配布

- [管理機構の検証](testing.md)：通常回帰と製品別の受入。
- [配布の手順と境界](development/distribution.md)：採用ソース・生成物・公開の区分。
- [インストーラー作成環境](installer-build.md)：Inno Setupと生成コマンド。
- [本体自己更新の配布・実装](development/self-update.md)・[MSIXの照合とデータ保持](development/msix-management.md)。
- [インストーラー診断](development/installer-diagnostics.md)・[公開情報キャッシュ](public-release-cache.md)・[ポータブル公開情報](portable-release-publication.md)。
- [セキュリティ点検と公開前検査](security.md)。

## 資料と進捗

[文書の編集方針](development/documentation-policy.md)に従い、READMEは利用者向け、現行仕様・手順はdocs、版別の要求・計画・結果は`dev/history/vX.Y.Z/`へ置きます。過去の件数・公開作業・PCでの実測は[開発記録の目録](../dev/README.md)から確認してください。

独立した子のGit・版・保存先を保持します。既存の未コミット差分を取り込んだり、設定・履歴を検証材料にしたりしません。開発用秘密情報は`C:\AI\env\<ツール名>\`へ保存し、ソースや版別ログへ値を残しません。