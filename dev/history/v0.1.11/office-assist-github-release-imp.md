# OfficeAssist v0.1.0のGitHub公開

2026-10-28。利用者の明示的な同意を受けて、OfficeAssistの独立Git履歴と配布物を公開した。[リポジトリ](https://github.com/KotorinChunChun/OfficeAssist)と[リリース](https://github.com/KotorinChunChun/OfficeAssist/releases/tag/v0.1.0)を参照する。

公開用ビルドと配布入力・Git履歴の秘密情報検査を実施し、GitHub上のsetup・ZIPのSHA-256とローカル配布物の一致を確認した。動作テストは利用者の指示により追加実施していない。セットアップは署名なし、.NET Desktop Runtime 10 x64が別途必要。

本体の配布一覧・本体からのインストール／更新機能は今回対象外。子の`tool.json`は本体配布の未対応を表す`unpublished`を保持し、GitHubでの単体公開と区別する。詳細は[子の版別報告](../../../tools/OfficeAssist/dev/history/v0.1.0/v0.1.0-imp.md)に記載する。
