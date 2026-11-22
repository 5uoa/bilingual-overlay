param(
    [Parameter(Mandatory = $true)]
    [string[]] $Files,

    [string] $PfxPath,

    [Security.SecureString] $PfxPassword,

    [string] $Thumbprint,

    [string] $TimestampUrl = "http://timestamp.digicert.com"
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($PfxPath) -eq [string]::IsNullOrWhiteSpace($Thumbprint)) {
    throw "请只提供 PfxPath，或只提供 Thumbprint。"
}
if ($PfxPath -and -not (Test-Path -LiteralPath $PfxPath -PathType Leaf)) {
    throw "找不到 PFX 文件：$PfxPath"
}
if ($TimestampUrl -notmatch "^https?://") {
    throw "TimestampUrl 必须是 HTTP 或 HTTPS 地址。"
}

$signTool = Get-Command "signtool.exe" -ErrorAction SilentlyContinue
if ($signTool -eq $null) {
    $candidates = @()
    foreach ($root in @(
        "C:\Program Files (x86)\Windows Kits\10\bin",
        "C:\Program Files\Windows Kits\10\bin"
    )) {
        if (Test-Path -LiteralPath $root) {
            $candidates += Get-ChildItem `
                -Path $root `
                -Directory `
                -ErrorAction SilentlyContinue |
                Sort-Object Name -Descending |
                ForEach-Object {
                    Join-Path $_.FullName "x64\signtool.exe"
                } |
                Where-Object { Test-Path -LiteralPath $_ }
        }
    }
    $signToolPath = $candidates | Select-Object -First 1
    if ([string]::IsNullOrWhiteSpace($signToolPath)) {
        throw "未找到 signtool.exe。请安装 Windows SDK 的 Signing Tools。"
    }
} else {
    $signToolPath = $signTool.Source
}

$targets = @()
foreach ($file in $Files) {
    if (-not (Test-Path -LiteralPath $file -PathType Leaf)) {
        throw "找不到待签名文件：$file"
    }
    $targets += (Resolve-Path -LiteralPath $file).Path
}

$arguments = @("sign", "/fd", "sha256", "/tr", $TimestampUrl, "/td", "sha256")
if ($PfxPath) {
    $arguments += @("/f", (Resolve-Path -LiteralPath $PfxPath).Path)
    if ($PfxPassword) {
        $passwordPointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($PfxPassword)
        try {
            $passwordText = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($passwordPointer)
            $arguments += @("/p", $passwordText)
        } finally {
            [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($passwordPointer)
        }
    }
} else {
    $normalizedThumbprint = ($Thumbprint -replace "\s", "").ToUpperInvariant()
    if ($normalizedThumbprint -notmatch "^[0-9A-F]{40,64}$") {
        throw "证书指纹格式无效。"
    }
    $arguments += @("/sha1", $normalizedThumbprint, "/s", "My")
}
$arguments += $targets

& $signToolPath @arguments
if ($LASTEXITCODE -ne 0) {
    throw "signtool 签名失败，退出代码：$LASTEXITCODE"
}

$verification = @()
foreach ($target in $targets) {
    $signature = Get-AuthenticodeSignature -LiteralPath $target
    $verification += [pscustomobject]@{
        File = $target
        Status = $signature.Status
        Signer = if ($signature.SignerCertificate) {
            $signature.SignerCertificate.Subject
        } else {
            ""
        }
        Thumbprint = if ($signature.SignerCertificate) {
            $signature.SignerCertificate.Thumbprint
        } else {
            ""
        }
    }
    if ($signature.Status -ne "Valid") {
        throw "签名验证失败：$target ($($signature.Status))"
    }
}
$verification | Format-Table -AutoSize
