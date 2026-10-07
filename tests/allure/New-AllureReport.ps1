# 產生 Allure 測試報告（單一 HTML 檔，直接用瀏覽器打開）
#
#   1. 先跑測試：dotnet test tests/TrafficSystem.Tests
#      Allure.Xunit 會把結果寫到測試輸出資料夾的 allure-results
#   2. 再產生報告：powershell -File tests/allure/New-AllureReport.ps1
#      需要 Allure 命令列工具（https://github.com/allure-framework/allure2/releases，需要 Java 8 以上）
#
# 測試依「功能區 → 單元測試／API 整合測試」分組，分組寫在各測試類別的 [AllureSuiteHierarchy]、[AllureBddHierarchy]
param(
    [string]$ResultsDir = "tests/TrafficSystem.Tests/bin/Debug/net8.0/allure-results",
    [string]$OutDir = "TestResults/allure-report",
    [string]$Allure = "allure"
)
$ErrorActionPreference = "Stop"

if (-not (Test-Path (Join-Path $ResultsDir "*-result.json"))) {
    throw "找不到測試結果：$ResultsDir（先執行 dotnet test，或用 -ResultsDir 指定 allure-results 的位置）"
}

# 報告「Environment」區塊：測試環境（用 xml 才能放中文；總覽頁只顯示前 5 項）
$commit = (git rev-parse --short HEAD 2>$null)
$branch = (git rev-parse --abbrev-ref HEAD 2>$null)
$runner = if ($env:GITHUB_ACTIONS -eq "true") { "GitHub Actions（ubuntu-latest）" } else { "本機（$([Environment]::OSVersion.VersionString)）" }
$env_items = [ordered]@{
    "分支／版本" = "$branch @ $commit"
    "執行環境"   = $runner
    "框架"       = ".NET 8、ASP.NET Core 8"
    "測試工具"   = "xUnit、WebApplicationFactory、coverlet"
    "測試資料庫" = "MySQL 8.0（每次自動建立獨立資料庫，測完刪除）"
}
$xml = "<environment>`n" + (($env_items.GetEnumerator() | ForEach-Object {
    "  <parameter><key>$([Security.SecurityElement]::Escape($_.Key))</key><value>$([Security.SecurityElement]::Escape($_.Value))</value></parameter>"
}) -join "`n") + "`n</environment>"
[IO.File]::WriteAllText((Join-Path $ResultsDir "environment.xml"), $xml, (New-Object Text.UTF8Encoding $false))

# 報告「Executors」區塊：在哪裡執行
if ($env:GITHUB_ACTIONS -eq "true") {
    $runUrl = "$env:GITHUB_SERVER_URL/$env:GITHUB_REPOSITORY/actions/runs/$env:GITHUB_RUN_ID"
    $executor = @{ name = "GitHub Actions"; type = "github"; buildName = "CI #$env:GITHUB_RUN_NUMBER"; buildUrl = $runUrl; reportName = "Play Taiwan 後端測試報告" }
} else {
    $executor = @{ name = "本機"; type = "local"; buildName = "$branch @ $commit"; reportName = "Play Taiwan 後端測試報告" }
}
[IO.File]::WriteAllText((Join-Path $ResultsDir "executor.json"), ($executor | ConvertTo-Json), (New-Object Text.UTF8Encoding $false))

$env:ALLURE_NO_ANALYTICS = "true"   # 不要在報告裡放 Google Analytics 追蹤碼
& $Allure generate $ResultsDir --clean --single-file --name "Play Taiwan 後端測試報告" -o $OutDir
if ($LASTEXITCODE -ne 0) { throw "Allure 產生報告失敗" }
Write-Host "報告：$(Join-Path (Resolve-Path $OutDir) 'index.html')"
