# ウィンドウタイトル共通規約

KotorinToys本体と収録ツールのすべてのアプリ独自ウィンドウに適用する。

- メイン画面: `製品名 - ver X.Y.Z`
- 設定・操作・確認・エラー・プレビュー画面: `画面名 - 製品名 - ver X.Y.Z`
- 区切りは半角スペース付きの半角ハイフン ` - ` に統一する。
- 製品名はカタログの表示名と一致させる。現在はKotorinToys、LiveTextMarker、SmartPaste、SSCapture、ShortcutKeyCounter、CapchunScreen、ChulipVideo、ChunapTool。
- バージョンは実行アセンブリの製品版（RustはCargo製品版）の数値3成分から取得し、タイトルへ直接書き込まない。
- 作者名、操作説明、実行状態はタイトルに追加せず、それぞれ製品情報、画面内の案内、状態表示へ記載する。
- Windows標準ダイアログと通知領域のツールチップはウィンドウタイトル規約の対象外。
- タイトル生成は各独立リポジトリ内の `WindowTitle.Create`（Rustは`WindowTitle::create`）に集約する。新しい画面にも同じ生成処理を使用する。

例: `KotorinToys - ver 0.1.0`、`設定 - SmartPaste - ver 0.1.4`、`設定 - LiveTextMarker - ver 1.1.0`、`録画 - SSCapture - ver 0.1.0`。
