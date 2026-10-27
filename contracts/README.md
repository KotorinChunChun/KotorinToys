# 共通契約

本体と各ツールの間で共有する情報の説明を置きます。

現在の管理連携は `ManagementEndpoint.cs` で定義しています。各独立ツールは承認済み実装のコピーを保持し、親フォルダーなしでもビルドできます。CapchunWindowは要求サイズ上限と応答書込みの期限を備える`ManagementEndpoint.Bounded.cs`のコピーを使用します。ChunapToolは不正要求後の受付継続と二重起動時の失敗理由を備える`ManagementEndpoint.Resilient.cs`のコピーを使用します。いずれも管理契約2の同じ応答形式を返します。

名前付きパイプは `KotorinToys.<WindowsユーザーSID>.<tool-id>`、現在のユーザー専用です。本体の`--management-contract-tests`は標準コピーと強化版コピーをそれぞれの正本と照合します。強化版の要求上限（4096文字）、超過拒否、停止ガードはCapchunWindowの`--management-tests --output <版別logs>`で検証します。ChunapToolの`--management-tests <版別logs>`は停止ガード、不正PID、上限超過、壊れたJSONの後の受付継続、既存プロセス表示を専用pipeで検証します。保護処理を標準版へ戻して一致させないでください。ワーカープロセスは窓口を持ちません。本体は応答PIDの実行パスと対話セッションも照合し、不一致のインスタンスを操作しません。

## 管理契約2

1行の旧形式 `status`、`show`、`stop` に加え、JSON要求 `command`、`expectedPid`、任意の`requestId`を受け付けます。本体の操作要求には確認したPIDを付けます。旧応答は情報取得だけとし、準備状態・停止結果が不明な旧ツールを自動操作しません。

応答は`protocolVersion: 2`、`pid`、`ready`、`version`、`launchOrigin`、`capabilities`、`requestId`、`result`、`detail`、`shortcuts`です。`ready`はUIディスパッチ後に判定します。表示の完了は`completed`、終了の受理は`accepted`、処理中等は`refused`、未対応は`unsupported`、UI例外は`failed`です。終了受理を停止完了とは扱いません。期限切れの要求は遅れて操作しません。

`launchOrigin`は`standalone`、`host_manual`、`direct_task`、`via_kotorintoys`です。次回の希望方式や実予約とは別の現在プロセス情報です。`--startup`は無表示開始、表示は準備完了後の手動`show`で行います。

## 定義とスナップショット

親カタログ2は各子の`tool.json`（形式1）を参照します。[カタログSchema](catalog.schema.json)と[ツールSchema](tool.schema.json)を公開仕様とし、本体は型・ID・版・参照パス・能力・依存物を読込時に検査します。ID重複と既存リンクによる範囲外アクセスも拒否します。旧カタログ1は互換読込し、追加未調査情報を推測で埋めません。

`ToolSnapshot`は導入検出、ランタイム、実行・起動経路、希望方式／実予約、取得時刻と根拠を分けます。予約アダプター未取得時は`ActualState=未確認`、希望方式未取得はnullです。既存EXEだけを正式導入としません。導入検出の確認不能や修復が必要な状態は開発EXEへフォールバックしません。

本体は終了要求を送信しただけでは停止完了にせず、プロセス終了を待って再検出します。タイムアウトや応答なしは確認できない状態として表示します。インストールと自動起動の契約は別途確定します。

## MSIXツールと4部版

HttpSelectorはRustの独立実装で管理契約2に適合します。製品版は数値3部または4部を保持し、HttpSelectorの表示・管理応答・MSIXは0.0.3.2で一致させます。

任意の `msix` 定義は `familyName`、`publisher`、`applicationId` を指定します。指定されたツールは署名付きの現在ユーザー登録、Manifest、実プロセスPackage Familyを照合し、未導入・旧版・確認不能を開発EXEへ切り替えません。管理パイプIDにはtool-idにPackage FamilyのSHA-256（小文字16進数）を付けます。無表示起動はOSのアプリ起動APIから行い、EXE直接予約は使用しません。

ショートカット宣言がないツールでは `shortcuts` を省略できます。明示された相対パスの検証は維持します。旧形式・既定の設定位置との互換性も維持します。
