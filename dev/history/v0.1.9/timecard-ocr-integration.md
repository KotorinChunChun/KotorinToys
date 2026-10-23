# TimecardOCRのツールメニュー追加

2026-10-24。依頼：Downloadsの「タイムカード_オフラインOCRプログラム_v1.zip」をツールメニューへ追加し、タイムカードのサンプル画像データをアップロードしない。

- [x] ソースを`tools/TimecardOCR`の独立Gitへ取り込み。ZIP自体、画像、画像由来の比較CSV・校正例・検証文書は取り込まない。
- [x] .NET 10の管理画面と共通のユーザー専用パイプ契約を追加。校正・OCR・SQLiteレビューを起動でき、実行中の停止は拒否する。
- [x] `catalog/tools.json`、同梱`config/tools.json`、開発ビルドへ追加。未公開・自動起動OFFとして扱う。架空のGitHub URLや配布物は登録しない。
- [x] 子ビルド成功（警告0・エラー0）。本体・テストビルド成功。NuGet脆弱性情報の取得不可によるNU1900警告あり。
- [x] 実プロセスの起動・状態・版・別PID拒否・表示要求・多重起動・PID維持・正常停止・再起動成功。
- [x] 本体回帰1,082件成功。Pythonの校正・レビューの合成データテスト14件成功。
- [x] 子Git登録候補に画像、CSV、SQLite、examples、validation、モデル、仮想環境がないことを検査。

通常権限で管理試験と本体回帰を実施した。サンドボックスではユーザー専用パイプへの接続が拒否される。生ログは`logs/timecard-ocr-integration/`へ保存。

取込時点では本PCのPython 3.12にcv2は未導入だった。下記の追加依頼で専用環境をセットアップ済み。実画像OCR・GUI目視は未確認。インストーラー作成、GitHub push、公開config更新、導入済み本体の更新は行っていない。

## 追加依頼によるセットアップとOCR確認（2026-10-24）

- 専用`.venv`へ固定版の依存パッケージを導入し、`pip check`成功。
- 検出・認識モデル2点を取得し、固定SHA-256と容量を照合。
- 全Python単体テスト26件成功。実モデルを使った合成画像のOCRは4セルすべて期待値と一致し、全体検出4件・原色と青除去の比較8件、CSV／SQLite出力に成功。
- Pythonの名前解決・ソケット接続を禁止した検証で通信試行0件。実タイムカード画像は使用せず、画像・結果・モデル・仮想環境はGit対象外のまま。外部アップロード・pushは行っていない。
- 再現手順と生ログの場所は子の`dev/history/v1.0.0/v1.0.0-imp.md`と利用手順へ記録。手書き画像の精度とGUI目視は未確認。

開発版の登録は次のコマンドで確認できる。公開configの再取得を避ける専用設定を使用する。

```powershell
cd C:\develop\KotorinToys
dotnet build src/KotorinToys/KotorinToys.csproj -c Debug
pwsh -ExecutionPolicy Bypass -File dev/scripts/start.ps1 -LocalCatalog
```

子の[利用手順](../../../tools/TimecardOCR/docs/kotorintoys-integration.md)は、リポジトリルートから`tools/TimecardOCR/docs/kotorintoys-integration.md`を参照。
