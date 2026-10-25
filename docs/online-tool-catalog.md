# GitHub設定でツール一覧を更新する（開発者向け）

画面の操作だけを確認したい場合は[利用者向けガイド](USERS_GUIDE.md)を参照してください。本書は配信用定義の形式と運用を扱います。

## 一覧取得と保持の動作

この機能を含む本体へ一度更新した後は、本体をバージョンアップせずに一覧を差し替えられます。設定の正本は `KotorinChunChun/KotorinToys` リポジトリの **mainブランチの [config/tools.json](../config/tools.json)** です。ファイルの公開はソースのpush・mainへの反映で行います。Releasesの作り直しは不要です。

本体起動時（管理画面を表示しないサインイン起動を含む）と「状態を更新」で取得します。4秒ごとの状態監視ではGitHubへアクセスしません。表示中に更新したい場合は「状態を更新」を押してください。GitHub側の反映・キャッシュには時間がかかることがあります。

通常時の取得件数や時刻はメイン画面へ常設表示しません。ネットワーク障害、404、形式エラー、未対応の設定、キャッシュ保存失敗では現在の一覧を維持し、理由を表の「詳細」と「状態を更新」のツールチップへ表示します。次回起動は最後の検証済み一覧を使い、それも壊れていれば同梱一覧へ戻ります。利用者の設定・履歴とは別に、設定フォルダーの `tool-catalog-cache.json` へ保存します。配置ルートの異なるキャッシュは採用しません。

一覧の更新でアプリの起動・停止・インストール・アンインストールや自動起動予約の変更は行いません。削除された項目はメイン一覧から非表示になるだけです。自動起動の希望方式と予約は維持し、「本体設定・自動起動」には以前の登録も残して、確認・解除できるようにします。非表示にしても自動起動を止めたことにはならないので、止めたい場合は先に希望方式を変更してください。

## 管理者向け：変更できるもの

- `tools` 配列への追加・削除、配列の順序、一言解説 `summary`、カテゴリ、文書・開発EXEの相対パス、版・依存情報、GitHubトップページ `repositoryUrl`。
- `tools: []` で全件非表示にできます。空一覧では選択ツールの操作を無効にし、理由を表示します。
- `repositoryUrl` の省略・null・空文字はGitHubリンクの未設定として扱い、ボタンを無効にします。
- 新規ツールは同梱フォルダーがなくても登録できます。起動には正式導入済みの実体、または開発配置が必要です。文書を同梱していない場合は導入先の文書か「GitHubを開く」を利用してください。

この拡張を含む本体では、現行の同一ユーザー・同一セッション・PID／EXE照合を伴う共通管理契約と標準Inno形式を実装した新規製品を、定義だけで導入・更新・削除へ追加できます。[新規管理製品の契約](development/definition-managed-products.md)を確認してください。製品・版・容量・ハッシュ照合は維持し、定義を渡さない未知IDや任意URLを許可しません。未知の管理プロトコル、新しいインストーラー方式、未知のMSIX署名・信頼条件は本体側の実装が必要です。`version` は定義の対象版であり、導入済み版・最新公開版の確定値ではありません。公開版の追加はReleasesの新規公開で反映し、同版assetを差し替えません。

`id` は永続キーです。登録後の `appId`・MSIX識別を別製品へ差し替える更新は拒否します。別製品は新しいID・AppIdで追加してください。以前に非表示になった登録とのAppId重複も拒否します。`name` はEXEの製品識別にも使うため、装飾した表示文字列へ安易に変更しないでください。

## 設定形式

`schemaVersion: 1` と `tools` を持つ自己完結JSONです。各要素は既存の子 `tool.json` の管理情報に `toolRoot` を加えます（子のschemaVersionは取り除きます）。子ファイルへの `definition` 参照は使用しません。未対応のキーやスペルミスも拒否します。現在の [完成した設定](../config/tools.json)を正本として編集してください。必須キーは省略せず、`runtimes` がない場合も `[]` を指定します。

```json
{
  "schemaVersion": 1,
  "tools": [
    {
      "id": "example-tool",
      "name": "ExampleTool",
      "version": "1.0.0",
      "summary": "このツールの一言解説",
      "category": "作業支援",
      "toolRoot": "tools/ExampleTool",
      "executable": "release/ExampleTool.exe",
      "installedExecutable": "ExampleTool.exe",
      "appId": "KotorinToys.ExampleTool",
      "manual": "README.md",
      "readme": "README.md",
      "shortcuts": "config/shortcuts.json",
      "distributionStatus": "unpublished",
      "supportedEnvironment": "windows11-x64",
      "investigationStatus": "confirmed",
      "capabilities": ["status", "show", "stop", "silent-start"],
      "runtimes": [],
      "repositoryUrl": "https://github.com/KotorinChunChun/ExampleTool"
    }
  ]
}
```

上のURLと製品は形式説明用の例であり、公開先の存在を保証しません。

取得先は `https://raw.githubusercontent.com/KotorinChunChun/KotorinToys/main/config/tools.json` に固定し、認証情報を送りません。転送先を追跡せず、10秒、512 KiB、128ツール、JSON深さ32を上限とします。ID・AppId・キー重複、未対応schema、範囲外パス、スクリプトの実行先、任意サイトのリンクを拒否します。`toolRoot` は `tools/製品フォルダー`、文書はmd/txt/pdf、GitHubリンクは指定所有者のHTTPSリポジトリトップページに限定します。HTTPSとGitHubリポジトリ管理者を信頼する境界であり、独立した電子署名の検証ではありません。

## 開発・検証

開発時の採用コミットと管理定義は引き続き `catalog/tools.json` と各子の `tool.json` で管理します。**配信用の一覧はconfigが正本**です。開発定義の更新を自動的に公開一覧へ混ぜません。必要な変更をconfigへ明示的に反映し、独立Gitや設定・履歴を含めずにレビューしてください。

```powershell
cd C:\develop\KotorinToys
dotnet build tests/KotorinToys.Tests/KotorinToys.Tests.csproj --artifacts-path artifacts/remote-catalog
.\artifacts\remote-catalog\bin\KotorinToys.Tests\debug\KotorinToys.Tests.exe --validate-online-catalog config/tools.json
.\artifacts\remote-catalog\bin\KotorinToys.Tests\debug\KotorinToys.Tests.exe --online-catalog-tests
```

検証コマンドは通信・公開を行いません。テストは版別logs内の専用キャッシュ・模擬予約を使います。公開前に検証し、mainへ反映してください。公開操作と実PCの本体更新は別の工程です。
