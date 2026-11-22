$ErrorActionPreference = "Stop"

$projectDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$deliveryDir = [IO.Path]::GetFullPath((Join-Path $projectDir "..\..\outputs"))
$outputDir = Join-Path $projectDir "outputs"
$packageDir = Join-Path $outputDir "BilingualOverlay"
$reportPath = Join-Path $projectDir "self-test.txt"
$compiler = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$frameworkDir = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319"
$wpfDir = Join-Path $frameworkDir "WPF"
$appVersion = "1.1.0"
$portableZip = Join-Path $outputDir "BilingualOverlay-Windows.zip"
$setupExe = Join-Path $outputDir "BilingualOverlaySetup.exe"
$mainExe = Join-Path $outputDir "BilingualOverlay.exe"
$manifestExample = Join-Path $outputDir "update-manifest.example.json"
$checksums = Join-Path $outputDir "SHA256SUMS.txt"

New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
New-Item -ItemType Directory -Path $packageDir -Force | Out-Null
New-Item -ItemType Directory -Path $deliveryDir -Force | Out-Null

$arguments = @(
    "/nologo",
    "/target:winexe",
    "/platform:anycpu",
    "/optimize+",
    "/codepage:65001",
    "/utf8output",
    "/win32manifest:$projectDir\app.manifest",
    "/out:$mainExe",
    "/r:System.dll",
    "/r:System.Core.dll",
    "/r:System.Drawing.dll",
    "/r:System.Windows.Forms.dll",
    "/r:System.Net.Http.dll",
    "/r:$frameworkDir\System.Web.Extensions.dll",
    "/r:$wpfDir\UIAutomationClient.dll",
    "/r:$wpfDir\UIAutomationTypes.dll",
    "$projectDir\AssemblyInfo.cs",
    "$projectDir\BilingualOverlay.cs"
)

& $compiler $arguments
if ($LASTEXITCODE -ne 0) {
    throw "C# compilation failed with exit code $LASTEXITCODE"
}

foreach ($name in @(
    "BilingualOverlaySetup.exe",
    "dictionary.tsv",
    "glossary.tsv",
    "README-使用说明.txt",
    "BilingualOverlay-Windows.zip",
    "update-manifest.example.json",
    "SHA256SUMS.txt"
)) {
    $path = Join-Path $outputDir $name
    if (Test-Path -LiteralPath $path) {
        Remove-Item -LiteralPath $path -Force
    }
}

$testProcess = $null
for ($attempt = 1; $attempt -le 6; $attempt++) {
    Start-Sleep -Seconds 1
    try {
        $testProcess = Start-Process `
            -FilePath $mainExe `
            -ArgumentList @("--self-test", "--report", $reportPath) `
            -Wait `
            -PassThru
        break
    }
    catch {
        if ($attempt -eq 6) {
            throw
        }
    }
}
if ($testProcess.ExitCode -ne 0) {
    throw "Self-test failed. See $reportPath"
}
$testOutput = Get-Content -LiteralPath $reportPath
$testOutput | Write-Output
if (-not ($testOutput -contains "PASS")) {
    throw "Self-test did not report PASS."
}

Copy-Item -LiteralPath (Join-Path $projectDir "dictionary.tsv") -Destination $packageDir -Force
Copy-Item -LiteralPath (Join-Path $projectDir "glossary.tsv") -Destination $packageDir -Force
Copy-Item -LiteralPath (Join-Path $projectDir "README-使用说明.txt") -Destination $packageDir -Force
Copy-Item -LiteralPath $mainExe -Destination $packageDir -Force
Copy-Item -LiteralPath $mainExe -Destination $setupExe -Force

$packageFiles = @(
    (Join-Path $packageDir "BilingualOverlay.exe"),
    (Join-Path $packageDir "dictionary.tsv"),
    (Join-Path $packageDir "glossary.tsv"),
    (Join-Path $packageDir "README-使用说明.txt")
)
Compress-Archive -LiteralPath $packageFiles -DestinationPath $portableZip -Force

$mainHash = (Get-FileHash -LiteralPath $mainExe -Algorithm SHA256).Hash
$setupHash = (Get-FileHash -LiteralPath $setupExe -Algorithm SHA256).Hash
$zipHash = (Get-FileHash -LiteralPath $portableZip -Algorithm SHA256).Hash
@(
    "$mainHash  BilingualOverlay.exe",
    "$setupHash  BilingualOverlaySetup.exe",
    "$zipHash  BilingualOverlay-Windows.zip"
) | Set-Content -LiteralPath $checksums -Encoding ASCII

$updateExample = [ordered]@{
    Version = $appVersion
    DownloadUrl = "https://example.com/BilingualOverlaySetup.exe"
    Sha256 = $setupHash
    Notes = "P1：翻译历史与收藏、集中设置、安装与更新、多显示器和高 DPI 改进。"
}
$updateExample |
    ConvertTo-Json |
    Set-Content -LiteralPath $manifestExample -Encoding UTF8

foreach ($name in @(
    "BilingualOverlay.exe",
    "BilingualOverlaySetup.exe",
    "BilingualOverlay-Windows.zip",
    "update-manifest.example.json",
    "SHA256SUMS.txt"
)) {
    Copy-Item -LiteralPath (Join-Path $outputDir $name) -Destination $deliveryDir -Force
}
$deliveryPackage = Join-Path $deliveryDir "BilingualOverlay"
New-Item -ItemType Directory -Path $deliveryPackage -Force | Out-Null
Copy-Item -Path (Join-Path $packageDir "*") -Destination $deliveryPackage -Force

Write-Host "Build complete."
Write-Host "  Program : $mainExe"
Write-Host "  Setup   : $setupExe"
Write-Host "  Portable: $portableZip"
Write-Host "  Hashes  : $checksums"
