# 本体のReleases自己アップデート

本書は開発中のソースに対応します。この機能を含む本体を最初に手動導入する必要があります。既存の公開済みv0.1.0インストーラーを差し替えたものではありません。

## 利用方法

本体全体の操作行で「本体アップデート」を開きます。現在の本体版と、GitHub Releasesで確認した公開版を別々に表示します。子ツールの「選択ツールを更新」とは別の操作です。

1. 既定では正式版だけを確認します。必要な場合だけ「開発版（prerelease）も更新対象にする」を選びます。対象変更時には再確認・再同意が必要です。
2. 新しい公開版があり、登録済みの正式導入先から実行している場合だけ更新できます。同じ版・旧版・公開情報未確認・開発実行・導入先や版の不一致では更新しません。
3. 未署名配布物であることと、本体の終了・更新・再起動への同意にチェックし、「ダウンロードして本体を更新」を押します。公開情報を再照合し、取得・検証・更新プロセスの準備を行います。
4. 準備が終わると本体だけを正常終了し、同じ導入先へインストールして本体を再起動します。子ツールは停止せず、起動済みの子を二重起動しません。
5. 本体下部に前回の更新結果・対象版・本体の再起動確認を表示します。成功と失敗・要確認・再起動待ち・取消を区別します。

「取得・準備を取り消す」や準備中の画面を閉じる操作では準備を取り消します。更新プロセスへ開始承認を渡した後は強制取消しません。終了を確認できない場合はインストーラーを実行しません。OSの自動再起動や強制終了は行いません。

設定・履歴・自動起動予約を削除・初期化せず、既存のユーザー単位Inno Setupを使います。本体の再起動には`--startup`を付けず、自動起動対象の子を再実行しません。更新失敗時の自動ロールバックは実装していません。登録済みの元版または対象版の実行先を確認できるときだけ、本体の再起動を試みます。

## 配布元・確認内容

取得先は`KotorinChunChun/KotorinToys`のGitHub Releasesに固定します。draftは対象外です。選択した公開範囲の中で最新のReleaseを確認し、現在版より新しい数値版だけを導入します。

既存の配布手順で、次の資産を同一tagへ公開してください。

- `KotorinToys-X.Y.Z-setup.exe`：ユーザー単位・ランタイム同梱のInno Setup。
- `KotorinToys-packages.json`：`host`、`KotorinToys`、AppId `KotorinToys`、版・上記ファイル名・SHA-256を持つ製品別配布台帳。
- `KotorinToys-SHA256SUMS.txt`：既存配布手順のハッシュ一覧。

製品・版・固定所有者のtag/asset URL・容量・SHA-256を照合し、GitHubのdigestがある場合も照合します。台帳・応答容量と時間には上限があり、インストーラーの上限は512 MiBです。hash不一致や不完全な取得物は実行しません。SHA-256と固定配布元の照合は、電子署名による発行元保証ではありません。現在の未署名配布を署名済みと扱いません。

公開には本体の版を現在版より上げ、同梱する自己完結型`maintenance`も更新してください。同じ版のasset差し替えは自己更新の対象になりません。公開・次版番号・利用者PCへの導入は、実装コミットとは別の工程です。

## 更新プロセスと結果記録

本体の設定フォルダー（通常`%LocalAppData%\KotorinToys`）配下に`updates/<操作ID>/`を作ります。導入先と分離した領域へメンテナンスの依存ファイルをSHA-256照合付きでコピーします。導入先の中に設定フォルダーを置く構成は自己更新できません。

workerは操作ID・親PID・起動時刻・セッション・実行先・正式導入先と版を照合し、台帳・インストーラーを再検証します。検証済み台帳とEXEを更新終了まで読取ロックします。準備完了応答と本体からの開始承認を分け、同時更新はロックと操作IDの取得記録で拒否します。

`updates/<操作ID>/result.json`と`updates/latest.json`へ結果を保存します。終了コード3010は「再起動待ち」とし、勝手にPCを再起動しません。インストーラーの成功終了だけでは完了とせず、登録・実行ファイルの版と本体再起動を確認します。結果の保存自体ができない場合は表示できないことがあります。

失敗・起動未確認の場合は結果の理由を確認し、必要ならGitHub Releasesから手動で再導入してください。自動で旧版へ戻す機能はありません。取得物・コピーしたworker・操作記録は調査用に保持し、自動削除しません。不要になった操作IDフォルダーを整理する場合も、更新プロセスとインストーラーが終了していることを先に確認してください。

## 開発時の隔離検証

```powershell
cd C:\develop\KotorinToys
dotnet build tests/KotorinToys.Tests/KotorinToys.Tests.csproj --artifacts-path artifacts/self-update
dotnet build src/KotorinToys.Maintenance/KotorinToys.Maintenance.csproj --artifacts-path artifacts/self-update
./artifacts/self-update/bin/KotorinToys.Tests/debug/KotorinToys.Tests.exe --self-update-tests
# 固定GitHub配布元のメタデータだけを照合（導入しない）
./artifacts/self-update/bin/KotorinToys.Tests/debug/KotorinToys.Tests.exe --host-release-check
# 通常の本体を停止せず、本体更新画面だけを開く。開発実行なので実更新は不可。
./artifacts/self-update/bin/KotorinToys.Tests/debug/KotorinToys.Tests.exe --self-update-panel
```

自動試験は隔離fixtureとOS操作の差替えで順序・正常終了拒否・取消・改変・導入失敗・3010・再起動失敗を検証します。実Releases間のインストールやサインイン・再起動の受入を代替しません。
