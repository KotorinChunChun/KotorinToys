param(
    [Parameter(Mandatory)][string]$OutputPath,
    [string]$ConfigPath = (Join-Path (Split-Path (Split-Path $PSScriptRoot)) 'config/tools.json'),
    [string]$FixtureDirectory
)

$ErrorActionPreference = 'Stop'
$catalog = Get-Content -LiteralPath $ConfigPath -Raw | ConvertFrom-Json
if ($catalog.schemaVersion -ne 1 -or $null -eq $catalog.tools -or @($catalog.tools).Count -gt 128) {
    throw '一覧configの形式が不正です。'
}
$repositories = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$null = $repositories.Add('KotorinToys')
foreach ($tool in $catalog.tools) {
    if ($tool.distributionStatus -ne 'published' -and $null -eq $tool.msix -and $tool.id -ne 'shortcut-key-counter') { continue }
    if ($tool.repositoryUrl -cnotmatch '^https://github\.com/KotorinChunChun/([A-Za-z0-9][A-Za-z0-9._-]{0,99})$') {
        throw "公開先の指定が不正です：$($tool.id)"
    }
    $null = $repositories.Add($Matches[1])
}

$items = foreach ($repository in ($repositories | Sort-Object)) {
    $available = $false
    $releases = @()
    try {
        if ($FixtureDirectory) {
            $source = Get-Content -LiteralPath (Join-Path $FixtureDirectory "$repository.json") -Raw
        } else {
            $source = & gh api "repos/KotorinChunChun/$repository/releases?per_page=100" 2>$null
            if ($LASTEXITCODE -ne 0) { throw 'GitHubの公開情報を取得できません。' }
            $source = $source -join "`n"
        }
        $parsed = ConvertFrom-Json -InputObject $source -NoEnumerate
        if ($parsed -isnot [array] -or $parsed.Count -ge 100) { throw '公開情報を全件確認できません。' }
        $releases = @($parsed | ForEach-Object {
            if ($_.tag_name -isnot [string] -or $_.draft -isnot [bool] -or $_.prerelease -isnot [bool] -or $_.assets -isnot [array]) {
                throw '公開情報の形式が不正です。'
            }
            [ordered]@{
                tag_name = $_.tag_name
                draft = $_.draft
                prerelease = $_.prerelease
                published_at = $_.published_at
                assets = @($_.assets | ForEach-Object {
                    [ordered]@{
                        name = $_.name
                        state = $_.state
                        size = $_.size
                        digest = $_.digest
                        browser_download_url = $_.browser_download_url
                    }
                })
            }
        })
        $available = $true
    } catch {
        $releases = @()
        Write-Warning "公開情報を取得できません：$repository"
    }
    [ordered]@{ name = $repository; available = $available; releases = @($releases) }
}
$index = [ordered]@{
    schemaVersion = 1
    generatedAt = [DateTimeOffset]::UtcNow.ToString('o')
    repositories = @($items)
}
$payload = $index | ConvertTo-Json -Depth 15
if ([Text.Encoding]::UTF8.GetByteCount($payload) -gt 2 * 1024 * 1024) { throw '公開情報一覧の容量が上限を超えました。' }
$path = [IO.Path]::GetFullPath($OutputPath)
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($path)) | Out-Null
[IO.File]::WriteAllText($path, $payload, [Text.UTF8Encoding]::new($false))
Write-Output "公開情報一覧を生成しました：$(@($items).Count)件"
