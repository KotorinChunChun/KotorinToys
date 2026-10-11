namespace KotorinToys;
internal static class WindowTitle
{
    public static string Create(string? screen = null)
    {
        var version = typeof(WindowTitle).Assembly.GetName().Version!;
        var product = $"KotorinToys - ver {version.Major}.{version.Minor}.{version.Build}";
        return string.IsNullOrWhiteSpace(screen) ? product : screen + " - " + product;
    }
}
