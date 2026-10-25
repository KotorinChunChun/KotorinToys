# 開発規約

- 質問には回答のみを行い、明確な変更指示なしに変更を始めない。
- PowerShellはpwshを使う。文書とコミットは日本語で記録する。
- READMEは利用者向けの説明書とし、開発手順・進捗・検証報告を本文へ混在させない。[文書の編集方針](docs/development/documentation-policy.md)に従う。
- 版別要求・計画・進捗・報告はdev/history/vX.Y.Z/へ配置する。現行仕様と再現可能な手順はdocsへ置く。
- フェーズごとに適切な検証を行い、問題がなければコミットする。
- 本体と収録ツールのタイトルは[共通タイトル規約](docs/window-title-rule.md)に従う。新規画面もWindowTitle.Createを使用する。
- 本体のアイコンは[共通アイコン規約](docs/app-icon-rule.md)に従う。新規の独自画面もBrandedFormを継承し、採用04の共通ICOを使用する。
- 収録ツールの独立Git管理を維持する。
- 起動は[共通起動規約](docs/development/startup-settings.md)に従う。通常の引数なし起動は利用者向け画面を表示し、自動・管理起動は`--startup`で無表示とする。新規の自動起動はOFFとし、単体と本体でOS予約を二重管理しない。
- 開発用の秘密情報は `C:\AI\env\<ツール名>\` に保存する。ソース配下や版別ログへ秘密値を置かない。
