# 製品名と配布リポジトリ、ソース配置を分ける。任意URLの注入には使わない。
function Get-DistributionProduct([string]$Name) {
    $products = @{
        KotorinToys = @{ Id='host'; AppId='KotorinToys'; Folder=''; Repository='KotorinToys' }
        LiveTextMarker = @{ Id='live-text-marker'; AppId='KotorinToys.LiveTextMarker'; Folder='LiveTextMarker'; Repository='LiveTextMarker' }
        SmartPaste = @{ Id='smart-paste'; AppId='KotorinToys.SmartPaste'; Folder='SmartPaste'; Repository='SmartPaste' }
        SSCapture = @{ Id='ss-capture'; AppId='KotorinToys.SSCapture'; Folder='SSCapture'; Repository='SSCapture'; PackagingScript='tools/SSCapture/scripts/package.ps1' }
        CapchunWindow = @{ Id='capchun-screen'; AppId='KotorinToys.CapchunScreen'; Folder='CapchunScreen'; Repository='CapchunWindow'; Project='tools/CapchunScreen/src/CapchunScreen/CapchunScreen.csproj' }
        ChulipVideo = @{ Id='chulip-video'; AppId='KotorinToys.ChulipVideo'; Folder='ChulipVideo'; Repository='ChulipVideo-CSharp'; Project='tools/ChulipVideo/src/ChulipVideo/ChulipVideo.csproj' }
        EveryDB = @{ Id='every-db'; AppId='KotorinToys.EveryDB'; Folder='EveryDB'; Repository='EveryDB'; Native=$true }
        ChunapTool = @{ Id='chunap-tool'; AppId='KotorinToys.ChunapTool'; Folder='ChunapTool'; Repository='ChunapTool'; Project='tools/ChunapTool/src/ChunapTool/ChunapTool.csproj' }
    }
    if (-not $products.ContainsKey($Name)) { throw "配布対象外の製品です：$Name" }
    return [pscustomobject]$products[$Name]
}

# 専用maintenanceとtransaction付きsetupを汎用版で置き換えない。全対象を副作用前に確認する。
function Assert-CommonDistributionProducts([string[]]$Names) {
    foreach ($name in $Names) {
        $identity = Get-DistributionProduct $name
        if ($identity.PSObject.Properties['PackagingScript']) {
            throw "$name は専用配布が必要です。$($identity.PackagingScript) と tools/$($identity.Folder)/dev/RELEASE_RULE.md を参照してください。共通経路では生成・公開しません。"
        }
    }
}
