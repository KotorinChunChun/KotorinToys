# VoiceMixerMicRouter追加 計画・報告

2026-10-19。対象は本体0.1.6の登録拡張。新しい本体リリースや外部公開は含めない。

- [x] P1: 公開v1.0.18、管理契約2、台帳とインストーラーの容量・SHA-256を確認。
- [x] P2: 独立Git・開発カタログ・配信用config・固定製品識別・公開台帳を追加し、ビルドと回帰検証。
- [x] P3: 管理経路の実接続・結果・利用手順を記録してコミット。

公開版はprerelease。採用ソースはorigin/mainのfc82835。旧ローカルmainの643e5eeと検証ログは保持し、親は公開済みコミットだけを参照する。

公開setupは38,521,655 bytes、SHA-256 `86a0f3a05a9975ec60f786c86548da6cdccaf243fd87783d1a426c1a8ff3ffe8`。実ダウンロード・Release digest・SHA256SUMS・製品台帳が一致。
## 検証結果

- 本体・テストのビルド成功。NuGet監査APIに到達できないNU1900警告4件、コンパイルエラー0。監査結果の取得は未完。
- MicRouter追加時点の10件config検証成功、全体回帰831件成功。固定配布先・台帳・画面上の操作可否を含む。
- 最新子ソースのDebugビルド成功。ToysのToolManagerから隔離設定で管理契約2に実接続。起動、1.0.18応答、既存PID保持、別PID停止拒否、shortcuts未対応応答、正常停止と実終了、実GitHub公開照合の8項目成功。
- 旧1.0.17のテスト用常駐を正常終了した。新しいテスト用常駐も正常終了済み。利用者の設定とモデルは変更していない。
- 公開setupの実取得・ハッシュ照合までを実施。インストーラー実行、Toys実画面からの実導入、ログイン、自動起動、音声品質は今回未検証。実画面操作・録画は今回は行っていない。
- 同じ作業フォルダーで別作業のSpkRouter登録が並行して始まったため、その変更を保持し、今回のコミットはMicRouter分だけを記録する。831件はSpkRouter追加前の結果。
- 開発・配布用のビルドスクリプトを更新し、PowerShell構文を検査した。
- GitHubへのpush・本体の新規リリース・稼働中本体の更新は行っていない。

## 起動

本体のデバッグ起動（既存本体が起動中ならその画面へ切り替わる）：

```powershell
cd C:\develop\KotorinToys
dotnet run --project src/KotorinToys/KotorinToys.csproj -- --root C:\develop\KotorinToys
```

MicRouter設定画面を単独で開く：

```powershell
& C:\develop\KotorinToys\tools\VoiceMixerMicRouter\src\src-tauri\target\debug\voicemixer-mic.exe --settings
```
