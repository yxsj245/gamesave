<#
.SYNOPSIS
    GameSave Manager MSI 安装包构建脚本

.DESCRIPTION
    此脚本执行以下步骤：
    1. 发布 GameSave 应用（Release 模式，SelfContained）
    2. 自动扫描发布目录，生成文件清单 WXS
    3. 使用 wix build 构建 MSI 安装包

.PARAMETER Platform
    目标平台，支持 x64、x86、ARM64，默认为 x64

.PARAMETER Version
    安装包版本号，格式为 Major.Minor.Build，默认为 1.0.0

.PARAMETER SkipPublish
    跳过发布步骤，直接使用已有的发布文件构建 MSI

.EXAMPLE
    .\build-msi.ps1
    .\build-msi.ps1 -Platform x64 -Version 1.2.0
    .\build-msi.ps1 -Platform ARM64 -SkipPublish
#>

param(
    [ValidateSet("x64", "x86", "ARM64")]
    [string]$Platform = "x64",
    
    [string]$Version = "1.0.0",
    
    [switch]$SkipPublish
)

$ErrorActionPreference = "Stop"

# 定义路径
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$SrcDir = Join-Path $ScriptDir "src"
$GameSaveDir = Join-Path $SrcDir "GameSave"
$InstallerDir = Join-Path $SrcDir "GameSave.Installer"
$OutputDir = Join-Path $ScriptDir "installer-output"

$RuntimeId = switch ($Platform) {
    "x64"   { "win-x64" }
    "x86"   { "win-x86" }
    "ARM64" { "win-arm64" }
}

$WixArch = switch ($Platform) {
    "x64"   { "x64" }
    "x86"   { "x86" }
    "ARM64" { "arm64" }
}

$PublishDir = Join-Path $GameSaveDir "bin\Release\net10.0-windows10.0.19041.0\$RuntimeId\publish"
$IconsDir = Join-Path $GameSaveDir "Assets"

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  GameSave Manager MSI 构建工具" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "  平台:       $Platform ($RuntimeId)" -ForegroundColor Yellow
Write-Host "  版本:       $Version" -ForegroundColor Yellow
Write-Host "  发布目录:   $PublishDir" -ForegroundColor Yellow
Write-Host ""

# ========================================
# 步骤 1：发布应用
# ========================================
if (-not $SkipPublish) {
    Write-Host "[1/3] 正在发布 GameSave 应用..." -ForegroundColor Green
    
    dotnet publish "$GameSaveDir\GameSave.csproj" `
        -c Release `
        -p:Platform=$Platform `
        -p:RuntimeIdentifier=$RuntimeId `
        -p:SelfContained=true `
        -p:PublishReadyToRun=true `
        -p:PublishTrimmed=true `
        -p:AppVersion=$Version
    
    if ($LASTEXITCODE -ne 0) {
        Write-Host "错误：应用发布失败！" -ForegroundColor Red
        exit 1
    }
    Write-Host "  发布完成！" -ForegroundColor Green
    Write-Host ""
} else {
    Write-Host "[1/3] 跳过发布步骤" -ForegroundColor Yellow
    Write-Host ""
}

# 验证发布目录
if (-not (Test-Path $PublishDir)) {
    Write-Host "错误：发布目录不存在: $PublishDir" -ForegroundColor Red
    exit 1
}
if (-not (Test-Path (Join-Path $PublishDir "GameSave.exe"))) {
    Write-Host "错误：GameSave.exe 不存在" -ForegroundColor Red
    exit 1
}

$allFiles = Get-ChildItem -Path $PublishDir -Recurse -File | Where-Object { $_.Extension -ne ".pdb" }
$publishSizeMB = [math]::Round(($allFiles | Measure-Object -Property Length -Sum).Sum / 1MB, 2)
Write-Host "  文件数: $($allFiles.Count) | 大小: ${publishSizeMB} MB" -ForegroundColor DarkGray
Write-Host ""

# ========================================
# 步骤 2：生成文件清单 WXS
# ========================================
Write-Host "[2/3] 正在生成文件清单..." -ForegroundColor Green

$HarvestFile = Join-Path $InstallerDir "HarvestedFiles.wxs"

# 使用 StringBuilder 构建 XML
$sb = New-Object System.Text.StringBuilder
[void]$sb.AppendLine('<?xml version="1.0" encoding="UTF-8"?>')
[void]$sb.AppendLine('<!-- 此文件由 build-msi.ps1 自动生成，请勿手动编辑 -->')
[void]$sb.AppendLine('<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">')
[void]$sb.AppendLine('    <Fragment>')
[void]$sb.AppendLine('        <DirectoryRef Id="INSTALLFOLDER">')

# 收集目录结构信息
$directories = @{}
$fileEntries = @()

# 辅助函数：确保目录及其所有父目录都被注册
function Register-Directory([string]$dirPath) {
    if ([string]::IsNullOrEmpty($dirPath) -or $directories.ContainsKey($dirPath)) { return }
    
    # 先递归注册父目录
    $parentDir = [System.IO.Path]::GetDirectoryName($dirPath)
    if (-not [string]::IsNullOrEmpty($parentDir)) {
        Register-Directory $parentDir
    }
    
    $safeDirBase = ($dirPath -replace '[^a-zA-Z0-9]', '_')
    if ($safeDirBase.Length -gt 60) {
        $hashBytes = [System.Security.Cryptography.MD5]::Create().ComputeHash(
            [System.Text.Encoding]::UTF8.GetBytes($dirPath)
        )
        $hash = [System.BitConverter]::ToString($hashBytes).Replace("-", "").Substring(0, 10)
        $safeDirBase = $safeDirBase.Substring(0, 50) + "_$hash"
    }
    $directories[$dirPath] = "dir_$safeDirBase"
}

foreach ($file in $allFiles) {
    $relativePath = $file.FullName.Substring($PublishDir.TrimEnd('\').Length + 1)
    $relativeDir = [System.IO.Path]::GetDirectoryName($relativePath)
    
    # 生成安全 ID
    $safeBase = ($relativePath -replace '[^a-zA-Z0-9]', '_')
    if ($safeBase.Length -gt 60) {
        $hashBytes = [System.Security.Cryptography.MD5]::Create().ComputeHash(
            [System.Text.Encoding]::UTF8.GetBytes($relativePath)
        )
        $hash = [System.BitConverter]::ToString($hashBytes).Replace("-", "").Substring(0, 10)
        $safeBase = $safeBase.Substring(0, 50) + "_$hash"
    }
    
    $fileEntries += [PSCustomObject]@{
        ComponentId = "cmp_$safeBase"
        FileId      = "fil_$safeBase"
        Source      = $file.FullName
        Dir         = $relativeDir
    }
    
    # 注册目录及其所有父目录
    if ($relativeDir) {
        Register-Directory $relativeDir
    }
}

# 辅助函数：获取直接子目录
function Get-ChildDirectories([string]$parentDir) {
    $result = @()
    foreach ($dir in ($directories.Keys | Sort-Object)) {
        $parent = [System.IO.Path]::GetDirectoryName($dir)
        if ($parent -eq $parentDir -or ([string]::IsNullOrEmpty($parentDir) -and [string]::IsNullOrEmpty($parent))) {
            if ([string]::IsNullOrEmpty($parentDir) -and [string]::IsNullOrEmpty($parent) -and -not [string]::IsNullOrEmpty($dir)) {
                $result += $dir
            } elseif (-not [string]::IsNullOrEmpty($parentDir) -and $parent -eq $parentDir) {
                $result += $dir
            }
        }
    }
    return $result
}

# 辅助函数：递归写入目录和文件
function Write-DirectoryContent([System.Text.StringBuilder]$builder, [string]$dirPath, [string]$indent) {
    # 获取此目录的直接子目录
    $childDirs = @()
    foreach ($dir in ($directories.Keys | Sort-Object)) {
        $parent = [System.IO.Path]::GetDirectoryName($dir)
        if ((-not $dirPath -and -not $parent) -or ($dirPath -and $parent -eq $dirPath)) {
            if ($dir -ne $dirPath) {
                $childDirs += $dir
            }
        }
    }
    
    foreach ($childDir in $childDirs) {
        $dirName = [System.IO.Path]::GetFileName($childDir)
        $dirId = $directories[$childDir]
        [void]$builder.AppendLine("$indent<Directory Id=`"$dirId`" Name=`"$dirName`">")
        
        # 递归子目录
        Write-DirectoryContent -builder $builder -dirPath $childDir -indent "$indent    "
        
        # 此目录中的文件
        foreach ($entry in ($fileEntries | Where-Object { $_.Dir -eq $childDir })) {
            [void]$builder.AppendLine("$indent    <Component Id=`"$($entry.ComponentId)`">")
            [void]$builder.AppendLine("$indent        <File Id=`"$($entry.FileId)`" Source=`"$($entry.Source)`" />")
            [void]$builder.AppendLine("$indent    </Component>")
        }
        
        [void]$builder.AppendLine("$indent</Directory>")
    }
}

# 先写入根目录中的文件
foreach ($entry in ($fileEntries | Where-Object { [string]::IsNullOrEmpty($_.Dir) })) {
    [void]$sb.AppendLine("            <Component Id=`"$($entry.ComponentId)`">")
    [void]$sb.AppendLine("                <File Id=`"$($entry.FileId)`" Source=`"$($entry.Source)`" />")
    [void]$sb.AppendLine("            </Component>")
}

# 写入子目录及其文件
Write-DirectoryContent -builder $sb -dirPath "" -indent "            "

[void]$sb.AppendLine('        </DirectoryRef>')
[void]$sb.AppendLine('    </Fragment>')
[void]$sb.AppendLine('')
[void]$sb.AppendLine('    <Fragment>')
[void]$sb.AppendLine('        <ComponentGroup Id="PublishedFiles">')

foreach ($entry in $fileEntries) {
    [void]$sb.AppendLine("            <ComponentRef Id=`"$($entry.ComponentId)`" />")
}

[void]$sb.AppendLine('        </ComponentGroup>')
[void]$sb.AppendLine('    </Fragment>')
[void]$sb.AppendLine('</Wix>')

# 写入文件
[System.IO.File]::WriteAllText($HarvestFile, $sb.ToString(), [System.Text.Encoding]::UTF8)

Write-Host "  清单已生成: $($allFiles.Count) 个文件, $($directories.Count) 个目录" -ForegroundColor DarkGray
Write-Host ""

# ========================================
# 步骤 3：构建 MSI
# ========================================
Write-Host "[3/3] 正在构建 MSI 安装包..." -ForegroundColor Green

if (-not (Test-Path $OutputDir)) {
    New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
}

$OutputMsi = Join-Path $OutputDir "GameSave-$Platform-Setup.msi"

$IconFile = Join-Path $IconsDir "app.ico"

wix build `
    -arch $WixArch `
    -src "$InstallerDir\Package.wxs" `
    -src "$HarvestFile" `
    -ext WixToolset.UI.wixext `
    -d "ProductVersion=$Version" `
    -d "IconPath=$IconFile" `
    -o "$OutputMsi"

if ($LASTEXITCODE -ne 0) {
    Write-Host ""
    Write-Host "错误：MSI 构建失败！" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Green
Write-Host "  构建完成！" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host ""

$msiFile = Get-Item $OutputMsi
$sizeMB = [math]::Round($msiFile.Length / 1MB, 2)
Write-Host "  输出文件: $($msiFile.FullName)" -ForegroundColor Cyan
Write-Host "  文件大小: ${sizeMB} MB" -ForegroundColor Cyan
Write-Host ""
