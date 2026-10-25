# READMEのツールリンク・公開参照の修正記録

確認日: 2026-10-25

ユーザーから、他ツールのREADMEリンク・親の参照コミット・HttpSelectorの公開URL登録を修正し、pushする指示を受けた。公開mainの専用作業ツリーで修正し、既存の作業ブランチと子リポジトリの未コミット変更を保持する。

## 修正

- [x] 一覧の9リンク、本文の4ガイド、末尾の音声2ツール紹介を、独立リポジトリの完全URLへ変更する。
- [x] HttpSelectorのURLを.gitmodulesへ登録し、update=noneを外す。
- [x] READMEと開発者ガイドに、公開登録済み12ツールを明示した取得コマンドを記載する。
- [x] 開発者ガイドの既存3か所の子文書リンクも公開URLへ変更する。
- [x] 参照切れの7件を、確認時点の各公開mainの固定SHAへ変更する。他の5件は既存の公開済み参照を維持する。
- [x] 公開URL・全Gitリンク・相対文書リンクを検査する。

## 採用する参照

| 子フォルダー | 旧参照（GitHubで404） | 新しい公開済み参照 |
|---|---|---|
| LiveTextMarker | bfc85c6 | 2fb56fafb729a62fcc711f7e9229fe3f41205a4f |
| SmartPaste | 3afd956 | 14e8685eb2c9b741d39b58cf1ac0d137a2023388 |
| ShortcutKeyCounter | aac94cc | 3d02a721d6043aa96a1d2f52972d8671646da7d3 |
| CapchunScreen（CapchunWindow） | e5aadd6 | 169f6d376a0233ca62ed550719467fd69de8dd3a |
| EveryDB | d8c2953 | 8992d19860931ec08656374287d0e9414afaae65 |
| VoiceMixerMicRouter | a958bde | fc82835331f26de13da66528518839f0a4a9bd4c |
| VoiceMixerSpkRouter | 4c73b8f | b3b484291f987ae5f7ee539d44e712e47daf87e1 |

子リポジトリの未公開ローカル変更を公開する作業は含めず、GitHubで取得できるソースへ親の参照だけを固定する。製品の配布版・導入済みバイナリの更新やReleaseの再公開は行わない。

## 検証

検証ログはこの版の`logs/20261025-all-tool-link-fix/`へ保存する。READMEのツール関連HTTPリンク、12件の.gitmodules登録と採用SHAの到達性、同一リポジトリ内の相対文書リンク、Git差分を確認する。検証は文書とGit参照に限定し、アプリ本体のビルド・GUIテストは実施しない。

- READMEと開発者ガイドのツール関連URL: 重複を除く21件すべてHTTP 200。
- 12ツールの公開URL登録と固定SHA: 全件対応し、各コミットのGitHubツリーがHTTP 200。HttpSelectorも公開URLが設定され、既存の公開済み採用SHAを維持した。
- 変更した3文書の同一リポジトリ内の相対リンク: 27件すべて存在。READMEに子リポジトリ配下への相対リンクは残っていない。
- 子ソースや製品配布定義は変更せず、専用作業ツリーで今回の文書・登録情報・7件のGitリンクだけをコミットしてmainへpushする。

HTTP検査と採用SHAの詳細は`validation.json`へ保存した。公開後はGitHubのmainからREADME・.gitmodules・Gitツリーを再取得して、反映内容を照合する。
