# 本体の構成要素

[開発者ガイド](../DEVELOPERS_GUIDE.md)へ戻る。ファイル別の入口と互換性境界は[アーキテクチャ](../architecture.md)を参照してください。

## 一覧の取得

ToolCatalogは開発定義の互換読込、OnlineToolCatalogは公開configの検証、ToolCatalogClientは固定HTTPSの取得、ToolCatalogSourceは最後の正常一覧と予約照合用の旧登録を担当します。

HostApplicationContextは共有SourceをToolManager・Dashboard・StartupCoordinatorへ渡します。配信用configは子ファイルに依存せず、本体のビルド・発行時にもコピーします。configの公開と製品バイナリの公開は別です。[configの運用](../online-tool-catalog.md)を参照してください。

## 画面の状態と操作

- Dashboard.cs：依存、状態、寿命。
- Layout：構築とイベント接続。Display：表の詳細・失敗通知・操作可否。
- Operations：操作の直列化と単体／一括導入。Refresh：状態・公開版の再取得。
- View：表示保存。Details：詳細ダイアログ。

メインにフッターは作りません。未導入・停止中・確認中・処理中等の判定は[操作可否](operation-availability.md)へ集約します。単体操作のInstallationFormは確認・進捗・結果、一括操作は用途別タブを表示します。

## 導入と公開情報

ToolInstallationRunnerは対応形式・識別を確認し、製品ごとに取得・実行・失敗結果を完結させます。定義追加だけで未知の配布形式を実行可能にしません。

ReleaseDisplayはGitHubの公開日時を日本時間へ変換します。公開日を取得日時で代用せず、日付欠落も未確認として扱います。確認文は導入済み版→対象版・公開日と質問に絞り、照合詳細はログへ分けます。

PublicReleaseClientは対象・期限・結果を、HTTP部は許可URL・リダイレクト・容量制限を、PublicReleaseMetadataは候補・tag・asset・台帳・ハッシュの一致を担当します。選択候補はJSONの所有権を持ち、元のJsonDocumentの寿命へ依存しません。HTTP模擬回帰と検証型の直接試験を維持します。

## 診断と更新

EXE導入診断は[インストーラー診断](installer-diagnostics.md)、本体更新の引継ぎは[自己更新の実装](self-update.md)、MSIXの署名・データ保持は[MSIX管理](msix-management.md)を参照してください。