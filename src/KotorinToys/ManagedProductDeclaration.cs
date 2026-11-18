using System.Text.Json;

namespace KotorinToys;

// 単体setupの更新・削除補助も、同梱の標準製品定義を使う。
// この読取だけで実行しない。呼出元が正式登録・EXE製品名・配置を再照合する。
internal static class ManagedProductDeclaration
{
    public static ToolDefinition Read(string product, string root)
    {
        if (ProductIdentity.FromName(product) is { } known)
            return new(known.Id, product, product + ".exe", "README.md", AppId: known.AppId, InstalledExecutable: product + ".exe");
        var path = PathSafety.Resolve(root, "tool.json");
        if (new FileInfo(path).Length > OnlineToolCatalog.MaximumBytes) throw new IOException("製品定義の容量上限を超えています。");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        if (document.RootElement.GetProperty("schemaVersion").GetInt32() != 1) throw new IOException("未対応の製品定義です。");
        var definition = document.RootElement.Deserialize<ToolDefinition>(ToolCatalog.Json) ?? throw new IOException("製品定義が空です。");
        if (product == "OperationRecorder" &&
            definition is { Id: "operation-recorder", Name: "OperationRecorder", AppId: "KotorinToys.OperationRecorder",
                Version: "0.1.8" or "0.1.9", DistributionStatus: "development",
                InstalledExecutable: "OperationRecorder.exe", RepositoryUrl: "https://github.com/KotorinChunChun/OperationRecorder" })
        {
            // この2版のセットアップに開発用の値が入っていた。ほかの項目は通常の製品照合を通す。
            definition = definition with { DistributionStatus = "published" };
        }
        if (definition.Name != product || ProductIdentity.FromTool(definition) == null) throw new IOException("未対応または不一致の製品定義です。");
        return definition with { ToolRoot = "", Executable = definition.InstalledExecutable };
    }
}
