using System.Diagnostics;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// 元のmainと比較し、状態の配置・初期化順序と保存モデルの形が同じであることを確認する。
internal static class LayoutVerifier
{
    static string Tokens(SyntaxNode? node) => node is null ? "" : string.Join("|", node.DescendantTokens().Select(t => t.RawKind + ":" + t.Text));
    static string Key(TypeDeclarationSyntax type) => string.Join(".",
        type.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().Reverse().Select(n => n.Name.ToString())
            .Concat(type.Ancestors().OfType<TypeDeclarationSyntax>().Reverse().Select(t => t.Identifier.Text)).Append(type.Identifier.Text));
    static string Parameters(TypeDeclarationSyntax type) => type switch
    {
        ClassDeclarationSyntax c => Tokens(c.ParameterList),
        RecordDeclarationSyntax r => Tokens(r.ParameterList),
        _ => ""
    };
    static CompilationUnitSyntax Parse(string text)
    {
        var root = CSharpSyntaxTree.ParseText(text).GetCompilationUnitRoot();
        if (root.GetDiagnostics().Any(d => d.Severity == DiagnosticSeverity.Error)) throw new IOException("構文エラーがあるため配置を検証できません。");
        return root;
    }
    public static void Verify(string workspace, string map)
    {
        var entries = JsonSerializer.Deserialize<MoveEntry[]>(File.ReadAllText(map), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        var originals = new List<CompilationUnitSyntax>();
        foreach (var entry in entries)
        {
            var pieces = entry.Source.Split('/');
            var child = pieces[0] == "tools";
            var repo = child ? Path.Combine(workspace, pieces[0], pieces[1]) : workspace;
            var relative = child ? string.Join('/', pieces.Skip(2)) : entry.Source;
            var info = new ProcessStartInfo("git") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
            foreach (var argument in new[] { "-c", "safe.directory=" + repo.Replace('\\', '/'), "-C", repo, "show", "main:" + relative }) info.ArgumentList.Add(argument);
            using var git = Process.Start(info) ?? throw new IOException("基準ソースを読めません。");
            var text = git.StandardOutput.ReadToEnd();
            var error = git.StandardError.ReadToEnd();
            git.WaitForExit();
            if (git.ExitCode != 0)
            {
                if (entry.Id == "smartpaste-tray") continue; // 先行移動でProgramから生まれるファイル。
                throw new IOException("基準ソース取得失敗：" + entry.Source + " / " + error);
            }
            originals.Add(Parse(text));
        }
        var files = entries.SelectMany(e => new[] { e.Source }
                .Concat((IEnumerable<string>?)e.Types?.Keys ?? Array.Empty<string>())
                .Concat((IEnumerable<string>?)e.Members?.Keys ?? Array.Empty<string>()))
            .Append("tools/SmartPaste/SmartPaste/Ui.cs").Distinct().Select(p => Path.GetFullPath(Path.Combine(workspace, p))).Where(File.Exists).ToArray();
        if (files.Any(p => !p.StartsWith(workspace + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))) throw new IOException("検証対象がルート外です。");
        var current = files.Select(p => Parse(File.ReadAllText(p))).SelectMany(r => r.DescendantNodes().OfType<TypeDeclarationSyntax>()).GroupBy(Key).ToDictionary(g => g.Key, g => g.ToArray());
        int checkedTypes = 0;
        foreach (var type in originals.SelectMany(r => r.DescendantNodes().OfType<TypeDeclarationSyntax>()))
        {
            if (!current.TryGetValue(Key(type), out var parts)) throw new IOException("型が失われています：" + Key(type));
            var fields = type.Members.OfType<FieldDeclarationSyntax>().Select(Tokens).ToArray();
            var actual = parts.SelectMany(p => p.Members.OfType<FieldDeclarationSyntax>()).Select(Tokens).ToArray();
            if (!fields.SequenceEqual(actual)) throw new IOException("状態フィールド・初期化順序が変わりました：" + Key(type));
            if (Parameters(type).Length > 0 && parts.SingleOrDefault(p => Parameters(p).Length > 0) is not { } primary)
                throw new IOException("主コンストラクターが失われています：" + Key(type));
            if (Parameters(type).Length > 0 && Parameters(parts.Single(p => Parameters(p).Length > 0)) != Parameters(type))
                throw new IOException("主コンストラクターが変わりました：" + Key(type));
            if (type is RecordDeclarationSyntax || type.Identifier.Text == "AppSettings")
            {
                var expectedProperties = type.Members.OfType<PropertyDeclarationSyntax>().Select(Tokens);
                if (!expectedProperties.SequenceEqual(parts.SelectMany(p => p.Members.OfType<PropertyDeclarationSyntax>()).Select(Tokens)))
                    throw new IOException("保存モデルの形が変わりました：" + Key(type));
            }
            checkedTypes++;
        }
        Console.WriteLine(JsonSerializer.Serialize(new { Result = "pass", CheckedTypes = checkedTypes, Evidence = "元mainと状態フィールド・初期化順序・主コンストラクター・保存モデルのtokenが一致" }));
    }
}
