# VoiceMixerSpkRouter の取り込みとPC検証

- 2026-10-19、VoiceMixerSpkRouter 1.0.9（3019e565bf0870fed553ad4270d449d95cd92ba8）を tools/VoiceMixerSpkRouter に取得。
- 独立Gitとoriginを保持し、親にはsubmoduleの採用コミットを記録する。
- 本体ランチャーの管理契約には未接続。単独ツールとして取り込み、既存9件の配信一覧は変更しない。
- ブラウザー拡張: npm ci、型検査、Viteビルド、28件の回帰テスト成功。
- npm audit: 開発依存 source-map-js にhigh 1件（GHSA-68fv-2mgg-jv7q）。固定依存の変更は行っていない。
- Rustは既存Scoop環境をプロセスPATHに追加して検証する。
- 本体のビルド・実行検証は進行中。
