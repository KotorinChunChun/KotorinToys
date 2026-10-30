# 本体自己更新の開発資料

ユーザー向け操作は[本体のアップデート](../self-update.md)を正本とする。

## GUI表示時の自動更新

HostApplicationContext.ShowDashboardの通常・トレイ・既存インスタンス表示で、起動対象の確認完了を待ってHostGuiUpdateCheckを実行する。正式導入だけを対象とし、現在のチャネルと同じ公開最新版を検証してHostUpdateFormへ渡す。ダイアログのShownで取得・更新を開始する。同版・旧版・種類不明・確認失敗・非表示・既に開いている更新画面では開始しない。同じ公開版の自動案内はプロセス内で一度とし、再試行は手動更新から行う。

## 版の種類と配布物

本体は所有者の公開情報一覧から自分の正確な数値版のReleaseを調べ、GitHubのprerelease属性で通常版・開発版を判定する。一致する版がない場合は不明とし、推測しない。検出した種類と同じ種類の最新数値版を自動検索する。公開情報一覧はキャッシュ・鮮度制約を持ち、draftは対象外。

同種の更新は現在より新しい版のみ許可する。他の種類への切替は明示選択・確認が必要で、古い版への切替では注意を表示する。種類不明からの導入も明示確認が必要。公開情報・実行版の種類を引継ぎ直前に再確認し、変化した場合は中止する。

配布元は`KotorinChunChun/KotorinToys`のGitHub Releasesに固定する。資産は`KotorinToys-X.Y.Z-setup.exe`、`KotorinToys-packages.json`、`KotorinToys-SHA256SUMS.txt`。製品識別・版・固定URL・容量・SHA-256を照合し、GitHub digestがある場合も照合する。インストーラーは512 MiBまで。未署名配布物の照合は電子署名による発行元保証ではない。

## 切替と退避

切替時は本体設定フォルダー（通常`%LocalAppData%\KotorinToys`）の設定・履歴を`updates/<操作ID>/backup/files/`にコピーし、SHA-256を記録・再照合する。`updates`配下は再帰的な配布物コピーを避けて前回結果`latest.json`のみ退避し、元ファイルや旧ジョブは削除しない。リンクを含む対象や件数・容量上限超過では中止する。退避が正常に検証できない場合、親を終了しない。退避されたファイルは更新処理の終了まで読取ロックする。

更新失敗時の設定や本体の自動復元は実装していない。古い版は新しい設定形式に対応しない可能性がある。利用者が確認して切り替え、必要に応じて退避から手動で復元できるよう、退避と更新記録は自動削除しない。

## 引継ぎと結果

本体を導入先と分離した`updates/<操作ID>/`にworkerをSHA-256照合付きで複製する。操作ID・親PID・起動時刻・セッション・実行先・正式導入先と版を確認し、台帳とインストーラーを再検証する。検証済み台帳とEXEを更新終了まで読取ロックする。準備完了応答と開始承認を分け、終了を確認できない場合はインストーラーを実行しない。

結果は`updates/<操作ID>/result.json`と`updates/latest.json`に保存する。終了コード3010は再起動待ちとし、OSを自動再起動しない。成功終了後も正式登録・実EXE・版と本体再起動を検証する。子ツールは停止しない。

## 開発時の検証

```powershell
cd C:\develop\KotorinToys
dotnet build tests/KotorinToys.Tests/KotorinToys.Tests.csproj --artifacts-path artifacts/self-update
dotnet build src/KotorinToys.Maintenance/KotorinToys.Maintenance.csproj --artifacts-path artifacts/self-update
./artifacts/self-update/bin/KotorinToys.Tests/debug/KotorinToys.Tests.exe --self-update-tests
./artifacts/self-update/bin/KotorinToys.Tests/debug/KotorinToys.Tests.exe --public-release-cache-ui-tests
# 本体のRelease情報の確認のみ。インストールはしない。
./artifacts/self-update/bin/KotorinToys.Tests/debug/KotorinToys.Tests.exe --host-release-check
```

隔離fixtureは版の種類、旧版切替の確認、退避の改変、停止拒否、取消、導入失敗と再起動失敗を検証する。実Releases間の切替・OS再起動の代替ではない。過去の実更新確認は[0.1.1の実装報告](../../dev/history/v0.1.1/v0.1.1-imp.md)を参照する。
