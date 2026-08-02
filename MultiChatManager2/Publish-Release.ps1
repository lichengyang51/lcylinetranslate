param(
    [string]$Version = "2.0.1",

    [string]$OutputRoot =
        (Join-Path $PSScriptRoot "Release"),

    [string]$PublicBaseUrl =
        "https://iyadtuwabsmiohkyfvqv.supabase.co/storage/v1/object/public/updates"
)

$ErrorActionPreference = "Stop"

$projectRoot =
    $PSScriptRoot

$solutionRoot =
    Split-Path $projectRoot -Parent

$appProject =
    Join-Path $projectRoot "LineTranslate.csproj"

$updaterProject =
    Join-Path $solutionRoot "LineTranslate.Updater\LineTranslate.Updater.csproj"

$releaseDirectory =
    Join-Path $OutputRoot "LineTranslate-$Version"

if (Test-Path -LiteralPath $releaseDirectory)
{
    throw "The release directory already exists: $releaseDirectory"
}

$appOutput =
    Join-Path $releaseDirectory "app"

$updaterOutput =
    Join-Path $releaseDirectory "updater-build"

$packageFileName =
    "LineTranslate-$Version-win-x64.zip"

$packagePath =
    Join-Path $releaseDirectory $packageFileName

$manifestPath =
    Join-Path $releaseDirectory "manifest.json"

New-Item -ItemType Directory -Path $releaseDirectory |
    Out-Null

try
{
    Write-Host "Publishing app version $Version..."

    & dotnet publish $appProject `
        --configuration Release `
        --runtime win-x64 `
        --self-contained true `
        --output $appOutput

    if ($LASTEXITCODE -ne 0)
    {
        throw "The app publish step failed."
    }

    $unexpectedDataPath =
        Join-Path $appOutput "Data"

    if (Test-Path -LiteralPath $unexpectedDataPath)
    {
        throw "The publish output contains a Data directory. Packaging stopped to protect user data."
    }

    Write-Host "Publishing updater..."

    & dotnet publish $updaterProject `
        --configuration Release `
        --runtime win-x64 `
        --self-contained true `
        --output $updaterOutput

    if ($LASTEXITCODE -ne 0)
    {
        throw "The updater publish step failed."
    }

    $updaterExecutable =
        Join-Path $updaterOutput "LineTranslate.Updater.exe"

    if (-not (Test-Path -LiteralPath $updaterExecutable))
    {
        throw "LineTranslate.Updater.exe was not produced."
    }

    $updaterTargetDirectory =
        Join-Path $appOutput "Updater"

    New-Item -ItemType Directory -Path $updaterTargetDirectory |
        Out-Null

    Copy-Item -Path (Join-Path $updaterOutput "*") `
        -Destination $updaterTargetDirectory `
        -Recurse

    Write-Host "Creating download and in-app update package..."

    Compress-Archive `
        -Path (Join-Path $appOutput "*") `
        -DestinationPath $packagePath `
        -CompressionLevel Optimal

    $packageInfo =
        Get-Item -LiteralPath $packagePath

    $packageHash =
        (Get-FileHash `
            -LiteralPath $packagePath `
            -Algorithm SHA256).Hash

    $publicUrl =
        ($PublicBaseUrl.TrimEnd('/')) +
        "/" +
        $packageFileName

    $manifest =
        [ordered]@{
            schemaVersion = 1
            payload = [ordered]@{
                productId = "LineTranslate"
                channel = "stable"
                version = $Version
                publishedAtUtc =
                    [DateTime]::UtcNow.ToString("o")
                mandatory = $false
                releaseNotes =
                    "1. 优化夜间模式配色：LINE 左侧功能栏、联系人与会话区域更协调，减少刺眼白色。`n2. 修复设置中【译文字体大小】和【译文显示方式】在夜间模式下文字看不清的问题。`n3. 优化 LINE 动态页面加载后的夜间模式稳定性。"
                package = [ordered]@{
                    url = $publicUrl
                    size = $packageInfo.Length
                    sha256 = $packageHash
                }
            }
        }

    $manifest |
        ConvertTo-Json -Depth 5 |
        Set-Content -LiteralPath $manifestPath -Encoding UTF8

    Remove-Item -LiteralPath $appOutput -Recurse -Force
    Remove-Item -LiteralPath $updaterOutput -Recurse -Force

    Write-Host ""
    Write-Host "Release complete: $releaseDirectory"
    Write-Host "Download package: $packagePath"
    Write-Host "Update manifest: $manifestPath"
    Write-Host "Website download URL: $publicUrl"
}
catch
{
    Write-Error $_
    throw
}
