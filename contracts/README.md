# 共通管理契約

KotorinToysと管理対象アプリの起動引数・IPC・状態の契約です。追加／更新の全体手順と文書更新表は[管理アプリ追加・更新手順](../docs/development/managed-app-integration.md)、予約の所有・復旧は[自動起動規約](../docs/development/startup-settings.md)を参照してください。

## 1. 起動引数

| 引数 | 用途・動作 | 適用範囲 |
| --- | --- | --- |
| 引数なし | 主画面／設定画面を表示。既存インスタンスがあればその画面を開く | 通常の単体利用 |
| `--startup` | 画面を出さず管理窓口の準備を行う。自動起動の登録を意味しない | 共通管理起動・自動起動 |
| `--launch-origin host_manual` | 本体の手動操作で開始した現在プロセス | 本体から子へ渡す共通引数 |
| `--launch-origin via_kotorintoys` | サインイン時に本体経由で開始した現在プロセス | 本体経由の自動起動 |
| `--launch-origin direct_task` | 管理対象のOSタスクから直接開始した現在プロセス | 直接タスク。MSIXでは使わない |
| `--settings-dir <専用フォルダー>` | 利用者データと試験設定を分離 | 本体は対応。子は各製品の対応を確認して隔離試験で使う。通常の管理起動には付けない |
| `--hidden` | 明示的な無表示指定 | 対応する製品だけ。全製品必須の引数ではない |

本体の管理起動は `--startup --launch-origin <起動元>` です。手動の「起動 / 設定を開く」もいったん無表示で開始し、準備完了後にIPCの `show` を送ります。既存プロセスを再利用した場合、実際の起動元を書き換えて手動起動扱いにしません。

`standalone` は通常の単体起動時に応答する起動元です。本体が子へ送る値は上表の3種類です。起動元は表示・診断情報であり、認証情報でも次回の希望方式でもありません。自動起動の既定はOFFです。

本体専用の `--root`・`--local-catalog`・`--self-test`、メンテナンスEXEの操作、製品固有の `--settings` 等を子の共通引数と混同しません。値の不足・未知引数・組み合わせの扱いは各製品のCLIとテストで確認します。configには任意の起動引数を追加しません。

## 2. 管理窓口

- Windowsの名前付きパイプ、現在ユーザー専用（C#では `PipeOptions.CurrentUserOnly` または同等の制限）。ワーカーは別の管理窓口を作らない。
- 通常の名前は `KotorinToys.<WindowsユーザーSID>.<tool-id>`。
- MSIXの名前は上のtool-idに `.<Package Family名のUTF-8をSHA-256にした小文字64桁16進数>` を加える。正式MSIXと開発EXEを同一視しない。
- 本体は応答PIDの同一セッション・実EXEを確認し、MSIXでは実プロセスのPackage Familyも照合する。別配置・応答なし・照合不能を正常稼働として操作しない。
- 要求と応答は各1行。通常はJSONを使い、状態取得には下記の旧形式も使う。単なるプロセス存在と、UI・管理部の準備完了を区別する。

本体は現在、状態取得で1行の `status`、変更操作で次のJSONを送ります。statusのJSON形式にも対応します。

```json
{"command":"show","expectedPid":1234,"requestId":"操作を識別する文字列"}
```

| `command` | 処理 | 期待する結果 |
| --- | --- | --- |
| `status` | 現在のPID・版・準備・起動元等を読む | 正常応答と `ready` の確認 |
| `show` | 既存画面を復元・表示・前面化する | UIへ処理した後の `completed` |
| `stop` | 製品本来の保存・後始末を伴う正常終了を要求する | `accepted` または理由付き `refused` |
| `shortcuts` | 現在有効なキー情報を読む | 能力がある場合の情報。未対応なら `unsupported` |

`show`／`stop`は事前に確認した `expectedPid` を付け、別PIDなら拒否します。`requestId` は対応付けのため応答へ返します。これだけで呼出し元を認証したことにはなりません。旧形式のshow／stopを受ける実装は互換用であり、新しい本体からPID照合を省略する理由にしません。

## 3. 管理契約2の応答

| フィールド | 内容 |
| --- | --- |
| `protocolVersion` | `2`。ツール定義のschemaVersionとは別 |
| `pid`・`ready` | 実プロセスPIDと、UI／管理部が要求を処理できる準備状態 |
| `version` | 製品の数値3成分または4成分。配布物・タイトルと一致させる |
| `launchOrigin` | `standalone`／`host_manual`／`direct_task`／`via_kotorintoys` |
| `capabilities` | IPCで対応する `status`／`show`／`stop`、任意の `shortcuts` |
| `requestId`・`result`・`detail` | 要求の対応、処理結果、失敗／拒否の理由 |
| `shortcuts` | 対応時の実効キー情報。任意 |

tool.jsonの能力 `silent-start` はCLIで無表示開始できることを宣言します。IPC操作名ではありません。応答capabilitiesと定義capabilitiesを同じ配列だと仮定しません。

| `result` | 意味 |
| --- | --- |
| `completed` | 表示等をUIへ処理した |
| `accepted` | 正常終了要求を受理した。停止完了ではない |
| `refused` | PID変更、記録／保存中等のため拒否した。理由をdetailへ返す |
| `unsupported` | 未対応の操作 |
| `failed` | UI操作等を完了できなかった |

準備完了を起動直後から無条件にtrueにせず、UI／管理処理が応答できる段階で判定します。期限切れの操作を後から実行しません。stopの受理を応答してから既存の正常終了へ渡し、本体は実際の終了を待って再検出します。旧プロトコルの応答は準備・結果を保証できないため、現在の本体では正常稼働として自動操作しません。

本体の現在の待機上限は[通信](../src/KotorinToys/ToolManager.Transport.cs)で2秒、[起動・停止](../src/KotorinToys/ToolManager.Lifecycle.cs)で準備30秒／停止15秒です。タイムアウトを強制終了・二重起動で補完しません。

## 4. 実装の共有と互換性

[ManagementEndpoint.cs](ManagementEndpoint.cs)と[ManagedToolContext.cs](ManagedToolContext.cs)はC#用の参照実装です。各子は親フォルダーなしでもビルドできるソースを保持します。Rust等は同じ通信契約へ適合する独立実装で構いません。全製品が同じ言語・同じ数のコピーを持つことは要求しません。

共通C#実装を変更する場合は、採用しているコピー・製品固有の派生実装・テストを特定し、各子の独立コミットで反映します。親だけの修正を子へ反映済みと扱わず、一括上書きもしません。参照C#実装のversion生成は3成分なので、4成分製品は適切な固有実装を使い、コピー時に4成分を失わないよう確認します。

## 5. 定義・配布・状態の区別

| 情報 | 正本・役割 |
| --- | --- |
| 子の宣言 | 子 `tool.json`、schemaVersion 1。型の説明は[ツールSchema](tool.schema.json) |
| 開発用一覧 | 親 `catalog/tools.json`、schemaVersion 2。子定義を参照する。[カタログSchema](catalog.schema.json) |
| 配信用一覧 | 親 `config/tools.json`、schemaVersion 1。子ファイルに依存しない。[一覧の運用](../docs/online-tool-catalog.md) |
| 製品別配布台帳 | 製品・版・ファイル名・長さ・ハッシュ等。定義のschemaやIPCのprotocolVersionとは別 |

Schemaファイルだけで全条件を保証するものではありません。[ToolCatalog](../src/KotorinToys/ToolCatalog.cs)・[OnlineToolCatalog](../src/KotorinToys/OnlineToolCatalog.cs)・[製品識別](../src/KotorinToys/ProductIdentity.cs)の実装上の検証と製品別テストも確認します。新規キー、未知の能力、任意URL・任意引数を定義更新だけで受け入れません。

`ToolSnapshot`は導入・依存・実行／起動元・希望方式／実予約・取得時刻と根拠を分離します。EXEがあるだけでは正式導入ではなく、起動中のlaunchOriginが次回の予約を表すわけでもありません。確認不能や修復が必要な正式配置を、開発EXEへの自動フォールバックで隠しません。

導入・更新・削除は[標準Innoの契約](../docs/development/definition-managed-products.md)または[MSIX管理](../docs/msix-management.md)で扱います。ショートカット宣言がない製品で架空のキー情報を返す必要はありません。明示したパスと対応能力だけを登録します。
