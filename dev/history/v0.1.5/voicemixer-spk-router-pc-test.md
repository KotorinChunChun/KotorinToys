# VoiceMixerSpkRouter の取り込みとPC検証

実施日: 2026-10-19

## 取り込み

- tools/VoiceMixerSpkRouter に独立Gitとして取得。親にはsubmoduleの採用コミットを記録。
- 版: 1.0.9、採用コミット: 3019e565bf0870fed553ad4270d449d95cd92ba8。
- origin: https://github.com/KotorinChunChun/VoiceMixerSpkRouter.git
- 本体ランチャー管理には未接続。配信一覧9件は変更しない。
- 親の既存 tools/HttpSelector の変更は保持した。

## 検証結果

| 検証 | 結果 |
|---|---|
| Rustデバッグビルド（locked） | 成功 |
| 本体通常テスト | 57成功、6件は別途実行対象 |
| ignoredのWin32・GUI試験 | 6成功。専用キーの合成入力・専用窓による試験 |
| 常駐ライフサイクル試験 | 13成功。設定保持、IPC、診断、多重起動拒否、終了、再起動 |
| ブラウザー拡張 | npm ci、型検査、Viteビルド、回帰28件成功 |
| 実デバイス列挙 | 現在のセッションでは「リモート オーディオ」1台 |
| 実デバイス10秒取得診断 | 終了コード0、waitingからrunningへ遷移、出力OFFで取得 |
| 起動状態 | デバッグEXEの応答を確認。設定表示を指定して起動、停止・出力OFFで残した |
| 最終画面確認 | 対象窓の動画をデコードし開始・中間・終了フレームでオーバーレイ表示を確認 |

Windows操作ヘルパーは再試行・カーネル再初期化後も「Computer Use helper already has an active request」で失敗。追加の手操作相当の操作確認・右半分への配置は未実施。録画された窓はオーバーレイであり、設定画面全体の目視合格やUI操作完了とは扱わない。

動画: [対象窓の表示記録](logs/recording-c12a56eee41a446188a23a070558b1fd/2026年10月19日_01時50分21秒_VoiceMixerSpkRouter%20設定画面と終了確認のテスト風景_c12a56.mp4)。動画名の終了確認は未実施。終了・再起動は上記13件の別試験で検証済み。

## 制限と残件

- 物理スピーカーでの聴感、2台同時出力、ブラウザー拡張の実インストール・タブ音声転送、実MSTSC接続での物理キーは未検証。
- npm auditで開発依存 source-map-js にhigh 1件（GHSA-68fv-2mgg-jv7q、1.2.2未満）。固定依存は変更していない。
- Release配布、インストーラー作成、Startup登録、GitHubへのpushは行っていない。
- 認証トークンを含む起動用設定は C:\AI\env\VoiceMixerSpkRouter\spk-settings.json。内容は報告・Gitへ含めない。

## 再現コマンド

```powershell
cd C:\develop\KotorinToys\tools\VoiceMixerSpkRouter
$env:PATH = "$env:CARGO_HOME\bin;$env:PATH"
cargo test --locked --bin voicemixer-spk -- --test-threads=1
cargo test --locked --bin voicemixer-spk -- --ignored --test-threads=1
cargo build --locked --bin voicemixer-spk
npm --prefix browser-extension run typecheck
npm --prefix browser-extension test
pwsh -NoProfile -File .\dev\scripts\tests\Test-SpkRouterLifecycle.ps1 -Executable .\target\debug\voicemixer-spk.exe
```

デバッグ起動（現在起動中。既存インスタンスがあれば設定画面の表示を要求）:

```powershell
cd C:\develop\KotorinToys\tools\VoiceMixerSpkRouter
$env:PATH = "$env:CARGO_HOME\bin;$env:PATH"
pwsh -NoProfile -File .\dev\scripts\Start-SpkRouter.ps1 -Debug -Mode Settings -Directory C:\AI\env\VoiceMixerSpkRouter
```
