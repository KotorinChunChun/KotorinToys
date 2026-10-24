namespace KotorinToys;

internal static class ToolSearch
{
    internal static string[] Tags(ToolDefinition tool)
    {
        var tags = DashboardText.Categories(tool.Category).Split('、', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return tags.Length == 0 ? ["未分類"] : tags;
    }

    internal static bool Matches(ToolDefinition tool, string query, string? category)
    {
        var tags = Tags(tool);
        if (category != null && !tags.Contains(category, StringComparer.OrdinalIgnoreCase)) return false;
        var content = tool.Name + "\n" + tool.Summary + "\n" + string.Join("\n", tags);
        return query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .All(word => content.Contains(word, StringComparison.OrdinalIgnoreCase));
    }
}
