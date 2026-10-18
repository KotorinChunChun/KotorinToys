# 製品名と配布リポジトリ、ソース配置を分ける。任意URLの注入には使わない。
function Get-DistributionProduct([string]$Name) {
    $products = @{
        KotorinToys = @{ Id='host'; AppId='KotorinToys'; Folder=''; Repository='KotorinToys' }
        LiveTextMarker = @{ Id='live-text-marker'; AppId='KotorinToys.LiveTextMarker'; Folder='LiveTextMarker'; Repository='LiveTextMarker' }
        SmartPaste = @{ Id='smart-paste'; AppId='KotorinToys.SmartPaste'; Folder='SmartPaste'; Repository='SmartPaste' }
        SSCapture = @{ Id='ss-capture'; AppId='KotorinToys.SSCapture'; Folder='SSCapture'; Repository='SSCapture' }
        CapchunWindow = @{ Id='capchun-screen'; AppId='KotorinToys.CapchunScreen'; Folder='CapchunScreen'; Repository='CapchunWindow'; Project='tools/CapchunScreen/src/CapchunScreen/CapchunScreen.csproj' }
        ChulipVideo = @{ Id='chulip-video'; AppId='KotorinToys.ChulipVideo'; Folder='ChulipVideo'; Repository='ChulipVideo-CSharp'; Project='tools/ChulipVideo/src/ChulipVideo/ChulipVideo.csproj' }
        EveryDB = @{ Id='every-db'; AppId='KotorinToys.EveryDB'; Folder='EveryDB'; Repository='EveryDB'; Native=$true }
        ChunapTool = @{ Id='chunap-tool'; AppId='KotorinToys.ChunapTool'; Folder='ChunapTool'; Repository='ChunapTool'; Project='tools/ChunapTool/src/ChunapTool/ChunapTool.csproj' }
    }
    if (-not $products.ContainsKey($Name)) { throw "配布対象外の製品です：$Name" }
    return [pscustomobject]$products[$Name]
}
