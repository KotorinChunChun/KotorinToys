namespace KotorinToys;

internal static class AppIcon
{
    internal const string ResourceName = "KotorinToys.AppIcon.ico";

    public static Icon Create()
    {
        using var stream = typeof(AppIcon).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException("本体のアイコンリソースがありません。");
        // 全フレームを保持し、タイトルバー・トレイのサイズ選択はWindowsに任せる。
        // 呼出側ごとに所有するIconを返すため、別画面の破棄の影響を受けない。
        return new Icon(stream, 32, 32);
    }
}
