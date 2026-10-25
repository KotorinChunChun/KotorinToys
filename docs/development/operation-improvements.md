# 操作改善の設計と検証

利用者向けは[使い方](../USERS_GUIDE.md)。要求と実測結果は[0.1.8要求](../../dev/history/v0.1.8/v0.1.8-req.md)・[実装報告](../../dev/history/v0.1.8/v0.1.8-imp.md)を参照。実装・PCへの導入・config公開・Release公開は別工程とする。

## 詳細と通信失敗

`OperationDetailsForm`は共通タイトルと04アイコンを持つ読取専用画面。`Dashboard.Details.cs`が選択IDから状態・公開版・操作不可の理由をまとめる。詳細セルのクリック／Enterで開き、詳細セルのダブルクリックではアプリを起動しない。

`ReleaseProblem`は接続、取得回数制限、配布情報欠落、未公開、未対応形式、照合失敗を分ける。HTTP 429と回数制限の403はRateLimit、404はUnavailable、通信障害はConnectionとして案内する。更新列は新版ありを「要(対象版)」、同じ版以上を「なし」、未導入を「-」、照合できない場合などを「未対応」とする。取得できなかった版を「なし」や最新として扱わない。

## EXE欠落の修復

`InstallationDetector`はHKCU登録先と容量制限付き`tool.json`のID・AppId・製品名・版・EXE名を照合する。EXE欠落だけで無条件に`Repairable`を付けない。旧形式の情報不足は既存の通常検出を壊さず、修復だけ拒否する。

`ToolActionPolicy`と`ToolUpdateCoordinator`の両方で、正式なInno製品、修復可能な欠落、停止中、同じ版以上の確認済み公開物を要求する。元の登録先へ固定して既存の照合・setup・導入後検証を利用し、設定・履歴を保持する。状態照会の登録パス利用は通常起動のEXE存在確認を緩めるものではない。実行中・状態不明・別製品・ダウングレードを拒否する。MSIX破損はOSの修復手順へ案内し、リセットや信頼条件の緩和を行わない。

## 一覧の表示設定

`Dashboard.View.cs`で行の`Tag`をツールIDに固定し、選択・状態反映・操作は`RowFor`等でID照合する。並べ替えと非表示行によって別製品を操作しない。更新ありの抽出も表示上の機能で、公開定義や自動起動予約は変更しない。

`DashboardViewStore`はHostSettingsと別の`dashboard-view.json`へ閉じる際に保存する。スキーマ1、64KiB以下、通常サイズ・既知の列・幅と順序・既知のフィルターだけを読み込む。サイズは幅980～6000／高さ540～4000、列幅40～3000に制限し、未知の列は無視、不正値や破損JSONは既定値へ戻す。一時ファイルからの置換で保存し、失敗は表示設定の問題として案内する。起動予約・所有トークンには書き込まない。

## 一括操作

`InstallationForm.Modes.cs`はインストール・更新／アンインストール／FFmpeg／開発者向けを分離する。切替時に対象チェックと結果をリセットし、削除への選択持越しを防ぐ。実行中と照会中は切替不可。ハンドラーでも用途外の操作を拒否する。単体のインストール・更新・修復・削除画面にはタブを出さない。

## 公開前の検査

```powershell
cd C:\develop\KotorinToys
# 静的整合だけ。これだけで公開可とはしない
$recordVersion = 'v' + (Get-Content catalog/host.json -Raw | ConvertFrom-Json).version
pwsh -File dev/scripts/test-release-readiness.ps1 -RecordVersion $recordVersion
# GitHubの公開configと配布メタデータも照合
pwsh -File dev/scripts/test-release-readiness.ps1 -Online -RecordVersion $recordVersion
```

`ReleaseReadiness`は本体のconfig・製品識別・配布台帳の検証器を再利用する。公開configとソースの全製品ID・識別・配布先・形式を照合し、導入対応製品はGitHubの台帳・asset・容量・固定SHA-256等も確認する。未公開／ZIPは案内のみとして区別する。新規の定義管理製品に同梱fallback台帳を必須化しない。Innoの定義Versionは開発版の参考値であって公開版の固定値ではなく、MSIXの必要4部版は公開物が満たす必要がある。

`ReadyToPublish`はオンライン照合・公開config確認済み・エラーなしの場合だけ成立する。`publish-releases.ps1`の本体公開はこれを必須にし、全対象の識別・ハッシュ・採用mainを最初のRelease作成前に照合する。部分的な公開を防ぐため、1対象でも不適合なら先に中止する。スクリプトを編集しただけでは公開されない。

この検査は配布メタデータの整合確認であり、インストーラー全体のダウンロードや実行、独立コード署名の発行元保証、全機能のセキュリティ監査ではない。取得後の実ペイロード照合と実導入の検証は既存処理および別の受入工程で行う。

## 安全なUIプレビュー

```powershell
cd C:\develop\KotorinToys
dotnet build tests/KotorinToys.Tests/KotorinToys.Tests.csproj -c Debug --artifacts-path artifacts/ux-018
& .\artifacts\ux-018\bin\KotorinToys.Tests\debug\KotorinToys.Tests.exe --ux-preview
```

実際のDashboardとInstallationFormに架空状態を注入する。起動停止はメモリー上の応答、導入確認は常に取消し、表示保存先は版別logsのfixtureだけ。実インストール・利用者設定・自動起動予約・実子プロセスは変更しない。通常の本体とは別窓で、閉じれば終了する。これは実修復・実サインイン・物理音声の証拠ではない。
