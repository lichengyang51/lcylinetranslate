param(
    [string]$Version = "2.0.2",

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
                    "1. 新增【AI智能回复】：在对方消息译文下方可一键打开，生成可直接复制的中文回复。`n2. 支持目标分类与最多两个具体目标，并可选择关系、回复风格、情绪表达、回复范围和回复节奏；AI 会按所选要求生成。`n3. 优化 AI 智能回复窗口：同一窗口会随所点消息更新，具体目标可展开、收起并多选。`n4. 修复 LINE 长消息、手动【显示更多】、回复引用及不同语言互相回复时的翻译显示问题。"
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
