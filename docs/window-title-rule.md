# ウィンドウタイトル共通規約

KotorinToys本体と収録ツールのすべてのアプリ独自ウィンドウに適用する。

- メイン画面: `製品名 - ver X.Y.Z`（4成分の製品は `製品名 - ver X.Y.Z.W`）
- 設定・操作・確認・エラー・プレビュー画面: `画面名 - 製品名 - ver X.Y.Z`（4成分の製品も同じ形式）
- 区切りは半角スペース付きの半角ハイフン ` - ` に統一する。
- 製品名はカタログの表示名と一致させる。現在はKotorinToys、LiveTextMarker、SmartPaste、SSCapture、ShortcutKeyCounter、CapchunScreen、ChulipVideo、ChunapTool、HttpSelector。
- タイトルのバージョン番号は、実行中の製品から3成分または4成分の数値を取得する。タイトルに固定値は書かない。画面表示、管理定義、配布パッケージで同じ製品バージョンを使い、4成分の番号は省略しない。
- Rustは各製品のCargo製品版から数値版を生成する。HttpSelectorはCargoの `0.0.3+2` を製品版 `0.0.3.2` に変換し、表示とMSIXに同じ数値を使用する。
- 作者名、操作説明、実行状態はタイトルに追加せず、それぞれ製品情報、画面内の案内、状態表示へ記載する。
- Windows標準ダイアログと通知領域のツールチップはウィンドウタイトル規約の対象外。
- タイトル生成は各独立リポジトリ内の `WindowTitle.Create`（Rustは`WindowTitle::create`）に集約する。新しい画面にも同じ生成処理を使用する。

例: `KotorinToys - ver 0.1.0`、`設定 - SmartPaste - ver 0.1.4`、`設定 - LiveTextMarker - ver 1.1.0`、`録画 - SSCapture - ver 0.1.0`、`高リスクURLの確認 - HttpSelector - ver 0.0.3.2`。
