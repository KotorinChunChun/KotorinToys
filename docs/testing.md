# 管理機構の検証

普段の設定や起動予約を変えずに共通検査を行うには、リポジトリのルートで次のコマンドを実行します。

```powershell
cd C:/develop/KotorinToys
dotnet run --project tests/KotorinToys.Tests/KotorinToys.Tests.csproj --artifacts-path artifacts/f1-tests
```

本体の管理窓口が使用中でないとき、`-- --host-lifetime`を付けると、本体の無表示起動・画面の開閉・明示終了も検査します。通常起動予約や子ツールの設定は変更しません。

`-- --live`は停止中の3ツールを順に起動し、無表示、同一PID、実画面、前面PID、正常停止を検査します。テストで起動したツールはfinallyで正常停止します。画面を前面に出すには対話操作の権限が必要なので、バックグラウンドのCLIだけでは失敗する場合があります。その場合は失敗の記録を残し、前面の管理画面から開く操作や行のダブルクリックを実際に試して、その結果を別に記録します。

本体の`--self-test`は、既存ツールを変更せずSKIPとして記録し、停止中のツールだけを無表示で起動・再起動・正常停止して元へ戻します。SKIPは合格ではありません。結果は`dev/history/v0.1.0/logs/<実行識別子>/`へ保存します。

画面検証時の起動コマンド：

```powershell
cd C:/develop/KotorinToys
dotnet run --project src/KotorinToys/KotorinToys.csproj -- --root C:/develop/KotorinToys
```

管理画面を閉じても管理部と子ツールは継続します。本体の明示終了は通知領域の「KotorinToysを終了」です。
