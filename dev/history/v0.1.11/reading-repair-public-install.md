# ReadingRepair v0.0.1の公開・導入対応

2026-11-04の利用者指示により、ReadingRepairをpublicへ変更し、既存ZIPは保持したまま標準Innoインストーラーと製品別台帳を同じReleaseへ追加する。

本体は定義ベースの標準管理を使用するため製品コードの変更・本体バイナリの更新は不要。configの公開状態をpublishedにし、ツール一覧と子Git参照を公開mainへ反映する。公開情報workflowの更新後、本体クラスを使うCLIで公開照合・取得・実導入・status/show/stop・設定保持を確認する。専用試験の入口はtests/ReadingRepair.Acceptance。

結果は実施後に追記する。生ログは対象版logs内。無関係なfeature/full-refactoringの作業は公開しない。


公開準備の結果: ReadingRepairはpublic、ZIPは匿名再取得と既存SHA-256一致。公開用インストーラーは.NET 10.0.14、1859ファイルの秘密検査0件、backendの必要ファイル736件のSHA一致、実Whisperの文字起こし成功。setup容量236,040,348 bytes、SHA-256 `4665ca9ecfb5703b15ae1f271789d59e39447638fb05e6932f4a9c9e7c79a448`。台帳は標準の製品名・AppId・版・ファイル名で照合する。

子schemaVersionを配信用configの要素へ混ぜないことを実際のOnlineToolCatalog.Parseで確認。専用受入ビルドは警告0・エラー0。コードは本体のPublicReleaseClient、ToolInstallationRunner、InstallerAdapter、ToolManagerを呼び出し、別の導入処理を再実装しない。


## 完了結果

公開main 955948cへ定義・台帳・子gitlinkと限定受入を反映し、Actions 41734235528がsuccess。raw配信のキャッシュ反映後に、本体の通常の固定URLから公開確認が成功した。匿名HTTPのsetup取得・容量/SHA-256照合・ToolInstallationRunnerとInstallerAdapterによる実導入・正式登録検出まで確認。

CurrentUserのPrograms/ReadingRepairへ0.0.1を導入。ToolManagerによる無表示起動、管理ready、PID/EXE/版、show、既存PID保持、誤PID停止拒否、正常停止が成功。既存のReadingRepairデータ全件・本体設定のハッシュは一致。実行中だったポータブルReadingRepairだけを正常終了し、本体や他製品は停止・更新していない。OSの自動起動を新規有効化していない。

インストーラー同梱.NETは10.0.14。導入後の共通doctorはready=true、辞書35件、登録ヘルパーself-test 5件を確認。読み検証アプリの元ZIP/タグは変更せず、新規setup・製品別台帳・追加チェックサムだけをv0.0.1へ追加した。

本体の新しいsetupや製品コード変更は不要。利用者は「状態を更新」でReadingRepairを表示し、インストール操作を使用できる。生ログは子dev/history/v0.0.1/logs/public-install-20261104/、受入コードはtests/ReadingRepair.Acceptance。
