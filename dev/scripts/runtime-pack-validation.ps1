function Assert-RuntimePackVersion($Assets, [string]$PackageId, [string]$Version) {
    $entries = @($Assets.project.frameworks.PSObject.Properties | ForEach-Object { $_.Value.downloadDependencies } |
        Where-Object { $_.name -ieq $PackageId })
    $expected = "[$Version, $Version]"
    if ($entries.Count -ne 1 -or $entries[0].version -ne $expected) {
        throw "実際に解決されたランタイムパックと同梱版が一致しません：$PackageId"
    }
}
