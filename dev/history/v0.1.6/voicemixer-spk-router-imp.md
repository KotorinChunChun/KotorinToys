# VoiceMixerSpkRouter 追加 計画・報告

対象は本体0.1.6の登録拡張。2026-10-19実施。

- [x] 公開v1.0.10（prerelease）、管理契約2、setupと製品台帳を確認。
- [x] 固定製品識別、開発カタログ、配信用config、固定リリース台帳を登録。
- [x] 本体回帰・管理経路での実接続、起動・終了・再起動と公開照合を検証。
- [x] 差分・文書リンクを検査して今回の差分だけコミット。

採用ソースはorigin/main b3b4842、公開タグv1.0.10は87742ce。公開setupは38,649,681 bytes。ダウンロードした実体のSHA-256は `76f8c2ac87dc470e07ad439eba1468e68f209f7062d1b589507a6eb3caab9dd4` で、GitHub asset digest・製品台帳・SHA256SUMSが一致。

Windows 11 x64、利用者単位の未署名開発版。ブラウザー拡張と案内を同梱するがブラウザーへの登録は別途必要。Rust・Node.js・.NETの追加インストールは不要。

前回検証で残した1.0.9のPID 8856は互換CLIで正常終了し、1.0.10をビルドした。開発カタログ指定先release/SpkRouter/voicemixer-spk.exeへデバッグビルドを配置。本番配布setupのインストールは今回行っていない。

MicRouter追加の既存差分を保持し、共有ファイルの登録を併存させる。配信用configのGitHub公開および本体リリース・導入は別工程であり、今回はソース側の追加と検証を行う。

## 検証結果

- 本体回帰837件成功。通常ユーザーセッションで実行。サンドボックス内ではUI/IPC準備判定で失敗するため、その結果を合格に含めず通常セッションで再検証した。
- ToysのToolManager実接続9ケース成功。無表示起動、管理契約2、版1.0.10、既存PID保持、別PID拒否、正常終了、再起動、再終了、公開版・製品台帳照合を確認。
- 製品付属の管理試験15ケース成功。画面表示、編集中の停止拒否、設定保持、終了受理後の実終了も確認。
- 最新版デバッグビルド成功。公開setupは取得と容量・ハッシュ照合まで。Toysの実UIからのインストール・更新・削除は未実施。
- 物理スピーカー2台への同時出力・聴感・ブラウザー拡張の実登録は今回の追加確認に含めない。
- NuGet監査サービスへの通信失敗NU1900が出たため、.NET依存の脆弱性監査成功とは扱わない。

再検証:

```powershell
cd C:\develop\KotorinToys
dotnet run --project tests/KotorinToys.Tests --artifacts-path artifacts/spk-registration
& .\artifacts\spk-registration\bin\KotorinToys.Tests\debug\KotorinToys.Tests.exe --spk-router-ipc
```

本体のデバッグ起動（今回の確認済み出力）:

```powershell
cd C:\develop\KotorinToys
& .\artifacts\spk-registration\bin\KotorinToys\debug\KotorinToys.exe
```

本体の起動時オンライン一覧はGitHub mainのconfigを使う。ローカルソースに追加しただけでは、既に導入済みの本体へ自動反映されない。GitHubへのconfig公開と新しい固定製品識別を含む本体の更新は未実施。
