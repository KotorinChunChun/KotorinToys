# ツールカタログ

収録対象ツールの一覧と、本体から参照する連携情報を置きます。

## 登録方針

- 実在する配布元、導入検出、実行対象、ランタイム、起動・終了方法を確認してから登録する。
- 未調査の項目は空欄または未調査として扱い、推測値で補完しない。
- 各ツールの単体利用を維持し、親プロジェクトから内部設定やフォルダーを一律に作り替えない。
- 既存ツールの配置方式とsubmodule採用は、実物確認後に決定する。

## 現在の登録状況

LiveTextMarker、SmartPaste、SSCapture、ShortcutKeyCounter、CapchunScreen、ChulipVideo、ChunapTool、HttpSelectorを `tools.json` に登録しています。Schema 2は各独立ツールのtool.jsonを参照します。開発EXEと資料は子の相対パス、MSIX製品はOSの導入登録を使い、確認できない項目を推測して埋めません。

## HttpSelectorの登録

現在のカタログはHttpSelectorを含む8件です。HttpSelectorの `tool.json` は製品版0.0.3.2と署名付きMSIXのPackage Family・Publisher・Application Idを持ちます。通常の管理対象は登録済みMSIXです。開発EXEの相対パスは隔離試験用に残します。MSIX未導入や旧版からの暗黙のフォールバックは行いません。

`distributionStatus: unpublished` は本体のInno台帳に含めない状態です。HttpSelectorの署名付きMSIX配布とは別に扱い、架空のInnoやGitの公開URLを追加しません。
