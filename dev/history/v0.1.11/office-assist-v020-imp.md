# OfficeAssist v0.2.0と本体配布連携

2026-11-01。OfficeAssistのExcel画像挿入で、ドロップ位置／選択範囲、原寸大／選択範囲に収める、埋め込み／リンクされた図／IMAGE関数式を選べるよう実装した。IMAGEには利用者が直接取得可能なHTTPS画像URLを指定する。

本体・メンテナンス・テストプロジェクトのビルドに成功。OfficeAssistの固定製品識別と共通の安全なメンテナンス付き配布経路、公開一覧・台帳を整備した。旧ローカルv0.1.0でも旧tool.jsonの公開先記載に依存せず保守対象として識別できる。

配布入力478ファイルの秘密検査0件。OfficeAssist v0.2.0開発版を[公開](https://github.com/KotorinChunChun/OfficeAssist/releases/tag/v0.2.0)し、setupの公開SHA-256 `a11d85a5b35f1fd75a78fc5050a41ada70da9fe36dc27432ce8db263ab13b273`を照合。本体公開mainのconfigだけへOfficeAssistを追加（`0921c58462382dd7658006a9996a7b7bb04448ec`）。[公開情報インデックス更新](https://github.com/KotorinChunChun/KotorinToys/actions/runs/41268974871)が成功し、インデックス上のsetupと台帳も確認した。

実Excel、GUI、本体からの実インストール・更新の動作テストは利用者の前回指示に従い実施していない。OfficeAssistのセットアップは未署名。既存の導入済みソフトとユーザーデータの変更は行っていない。
