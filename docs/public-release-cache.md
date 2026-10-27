# 公開情報一覧とキャッシュ（開発者向け）

利用者向けの操作は[よくある質問](FAQ.md)を参照してください。

## 公開情報の生成と取得

本体は、各利用者のPCからGitHub REST APIへ製品別の問い合わせを行わず、`release-index`ブランチの`config/release-index.json`をGitHubの公開ファイルとして取得します。複数アプリと本体の更新確認で同じ一覧を共有します。アプリ側にGitHubの認証情報は保存しません。

GitHub Actionsの「公開情報一覧の更新」は、公開mainの[一覧config](../config/tools.json)をもとにGitHub Releasesを確認します。標準インストーラー、対応MSIX、ShortcutKeyCounterの公開ZIP、および本体が対象です。生成処理は1時間ごと、configや生成スクリプトのmain更新時、手動実行時に動き、一覧専用ブランチへ結果を保存します。新規の標準管理製品も、configへ公開先を追加すると対象になります。実際の反映はActionsの実行時間やGitHubの配信状況に左右されます。

Actionsによる収集にはGitHubの`GITHUB_TOKEN`を使います。取得できない製品や100件以上のため全件を確認できない製品は未確認として記録し、それ以外の製品の更新を続けます。公開一覧の生成日時が24時間を超えて古い場合や未来の日時が不正な場合、本体は最新確認済みとして扱いません。

この仕組みを公開する際は、workflowとconfigをmainへ反映した後、Actionsが成功し、`release-index`ブランチの一覧が取得できることを確認してから、この方式を使う本体を配布してください。初回はGitHub Actionsの手動実行も利用できます。workflowには`contents: write`が必要です。専用ブランチへの通常のpushが拒否された場合、更新は失敗し、既存一覧の日時が進まないため、24時間後には本体で未確認になります。

利用者の公開情報取得はREST APIの匿名枠を消費しません。公開ファイルやインストーラーの配信自体について、GitHub側の一時的なアクセス制限や通信障害が起きないことを保証するものではありません。Actionsの収集にもGitHubのAPI制限があります。

## キャッシュと通信失敗

公開情報一覧、製品別配布台帳、SHA256SUMSの成功応答は5分間再利用し、同時要求をまとめます。メモリーと`%LOCALAPPDATA%\KotorinToys\public-release-cache`に保存するため、本体を再起動しても有効期限内は再利用できます。新しいReleaseの一覧への反映は通常約1時間以内で、その後さらに最大5分のキャッシュが残る場合があります。期限後は再取得し、ETagがある場合は条件付き要求を使います。

本文キャッシュはメモリー/ディスク各64件までです。壊れたファイル、別URL、未来の取得時刻は採用しません。ディスクへ保存できない場合も、通信とメモリーでの再利用を続けます。利用者設定やツール一覧configの保存とは別の領域です。

HTTP 429や取得制限と判別できる403では、`Retry-After`や取得可能な解除予定時刻まで通信を控えます。解除時刻が分からない場合は最低1分待ちます。これらは再起動後も保持します。有効期限内の正常なキャッシュは引き続き利用できます。GitHubへのログインは不要です。

## 照合

一覧はHTTPSとKotorinToysリポジトリの管理者を信頼する仕組みです。読み出すたびに製品、版、所有者、公開先、asset、容量、ハッシュを検査し、製品別台帳またはSHA256SUMSとも照合します。取得後の配布物についても容量とSHA-256を検査します。同じ版の同梱済み台帳との差し替えは拒否します。ハッシュは独立した電子署名による発行元の保証ではありません。

古い一覧、取得失敗した製品、期限切れ本文や通信失敗時の同梱台帳は、導入可能な確認済み公開版にはしません。本体自身の正式版／開発版選択、MSIXの署名・Publisher検証とポータブル版の導入不可も維持します。

実装と再現可能な試験は`dev/scripts/update-release-index.ps1`、`tests/ReleaseIndexGenerator.Tests.ps1`、`tests/ReleaseIndexWorkflow.Tests.ps1`、`--release-index-tests`と`--public-release-cache-tests`を参照してください。[GitHub APIの取得制限](https://docs.github.com/en/rest/using-the-rest-api/rate-limits-for-the-rest-api)と[API利用の推奨事項](https://docs.github.com/en/rest/using-the-rest-api/best-practices-for-using-the-rest-api)も参照できます。
