using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// メソッド本体・状態フィールド・初期化順序を変更せず、レビュー用patchを出力する。
// 自分では製品ソースを書き換えない。patch適用は通常の編集ツールで行う。
if (args.Length != 3) throw new ArgumentException("作業ルート・移動マップ・項目IDを指定してください。");
var workspace = Path.GetFullPath(args[0]);
var entries = JsonSerializer.Deserialize<MoveEntry[]>(File.ReadAllText(args[1]), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
var entry = entries.Single(e => e.Id == args[2]);
string Resolve(string relative)
{
    var path = Path.GetFullPath(Path.Combine(workspace, relative));
    if (!path.StartsWith(workspace + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        throw new IOException("作業ルート外は編集できません。");
    return path;
}
string Tokens(SyntaxNode node) => string.Join("|", node.DescendantTokens().Select(t => t.RawKind + ":" + t.Text));
var originalText = File.ReadAllText(Resolve(entry.Source));
var root = CSharpSyntaxTree.ParseText(originalText).GetCompilationUnitRoot();
if (root.ContainsDirectives || root.GetDiagnostics().Any(d => d.Severity == DiagnosticSeverity.Error))
    throw new IOException("ディレクティブまたは構文エラーを含むソースは自動分離しません。");
var ns = root.Members.OfType<FileScopedNamespaceDeclarationSyntax>().Single();
var files = new Dictionary<string, CompilationUnitSyntax>();
CompilationUnitSyntax FileFor(IEnumerable<MemberDeclarationSyntax> members) =>
    root.WithMembers(SyntaxFactory.SingletonList<MemberDeclarationSyntax>(ns.WithMembers(SyntaxFactory.List(members))));
if (entry.Types is not null)
{
    foreach (var (destination, names) in entry.Types)
    {
        var moved = ns.Members.OfType<BaseTypeDeclarationSyntax>().Where(t => names.Contains(t.Identifier.Text)).ToArray();
        if (moved.Length != names.Length) throw new IOException("型の移動対象を一意に特定できません：" + destination);
        files.Add(destination, FileFor(moved));
        ns = ns.RemoveNodes(moved, SyntaxRemoveOptions.KeepNoTrivia)!;
    }
}
if (entry.Members is not null)
{
    var declaration = ns.Members.OfType<ClassDeclarationSyntax>().Single(c => c.Identifier.Text == entry.Type);
    var remaining = declaration;
    foreach (var (destination, names) in entry.Members)
    {
        var moved = remaining.Members.OfType<MethodDeclarationSyntax>().Where(m => names.Contains(m.Identifier.Text)).ToArray();
        if (names.Any(n => !moved.Any(m => m.Identifier.Text == n))) throw new IOException("メソッドがありません：" + destination);
        var modifiers = declaration.Modifiers.Add(SyntaxFactory.Token(SyntaxKind.PartialKeyword));
        var part = SyntaxFactory.ClassDeclaration(declaration.Identifier).WithModifiers(modifiers)
            .WithTypeParameterList(declaration.TypeParameterList).WithMembers(SyntaxFactory.List<MemberDeclarationSyntax>(moved));
        files.Add(destination, FileFor([part]));
        remaining = remaining.RemoveNodes(moved, SyntaxRemoveOptions.KeepNoTrivia)!;
    }
    remaining = remaining.WithModifiers(remaining.Modifiers.Add(SyntaxFactory.Token(SyntaxKind.PartialKeyword)));
    ns = ns.ReplaceNode(declaration, remaining);
}
files.Add(entry.Source, root.WithMembers(SyntaxFactory.SingletonList<MemberDeclarationSyntax>(ns)));
// 分離前後の全メンバーをtokenで照合する。空白・partial修飾以外の無意識な変更を拒否。
string[] Members(IEnumerable<CompilationUnitSyntax> roots) => roots.SelectMany(r => r.DescendantNodes().OfType<TypeDeclarationSyntax>())
    .SelectMany(t => t.Members.Select(m => t.Identifier.Text + ":" + Tokens(m)))
    .Order(StringComparer.Ordinal).ToArray();
if (!Members([root]).SequenceEqual(Members(files.Values))) throw new IOException("メンバーの内容が変わりました。");
var patch = new StringBuilder("*** Begin Patch\n");
foreach (var (destination, syntax) in files)
{
    var normalized = syntax.NormalizeWhitespace(indentation: "    ", eol: "\n");
    if (Tokens(syntax) != Tokens(normalized)) throw new IOException("整形によるtoken変更を検出しました。");
    var text = normalized.ToFullString().TrimEnd() + "\n";
    var path = Resolve(destination).Replace('\\', '/');
    if (destination == entry.Source)
    {
        patch.AppendLine("*** Update File: " + path).AppendLine("@@");
        foreach (var line in originalText.Replace("\r\n", "\n").TrimEnd('\n').Split('\n')) patch.AppendLine("-" + line);
    }
    else
    {
        if (File.Exists(path)) throw new IOException("既存ファイルへ上書きしません：" + path);
        patch.AppendLine("*** Add File: " + path);
    }
    foreach (var line in text.TrimEnd('\n').Split('\n')) patch.AppendLine("+" + line);
}
patch.AppendLine("*** End Patch");
Console.OutputEncoding = Encoding.UTF8;
Console.Write(patch.ToString());

internal sealed record MoveEntry(string Id, string Source, string? Type,
    Dictionary<string, string[]>? Types, Dictionary<string, string[]>? Members);
