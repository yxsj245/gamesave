<#
.SYNOPSIS
    GameSave Manager 便携版构建脚本（单文件自解压 EXE）

.DESCRIPTION
    此脚本执行以下步骤：
    1. 发布 GameSave 应用（Release 模式，SelfContained + SingleFile）
    2. 将所有发布文件打包为 ZIP
    3. 使用 C# 编译一个自解压启动器，将 ZIP 嵌入 EXE 末尾

    最终生成一个单独的 EXE 文件，双击即可运行：
    - 首次运行时自动解压到 %LOCALAPPDATA%\GameSave-Portable
    - 后续运行直接启动，版本更新时自动覆盖

.PARAMETER Platform
    目标平台，支持 x64、x86、ARM64，默认为 x64

.PARAMETER Version
    版本号，格式为 Major.Minor.Build，默认为 1.0.0

.PARAMETER SkipPublish
    跳过发布步骤，直接使用已有的发布文件

.EXAMPLE
    .\build-portable.ps1
    .\build-portable.ps1 -Platform x64 -Version 1.2.0
    .\build-portable.ps1 -SkipPublish
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
$OutputDir = Join-Path $ScriptDir "installer-output"

$RuntimeId = switch ($Platform) {
    "x64"   { "win-x64" }
    "x86"   { "win-x86" }
    "ARM64" { "win-arm64" }
}

$PublishDir = Join-Path $GameSaveDir "bin\Release\net10.0-windows10.0.19041.0\$RuntimeId\publish"

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  GameSave Manager 便携版构建工具" -ForegroundColor Cyan
Write-Host "  (单文件自解压 EXE 模式)" -ForegroundColor Cyan
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

    dotnet publish "$GameSaveDir\GameSave.csproj" -c Release -p:Platform=$Platform -p:RuntimeIdentifier=$RuntimeId -p:SelfContained=true -p:PublishReadyToRun=true -p:PublishTrimmed=true -p:AppVersion=$Version

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
    Write-Host "错误：GameSave.exe 不存在于发布目录中" -ForegroundColor Red
    exit 1
}

# 统计发布文件信息
$allFiles = Get-ChildItem -Path $PublishDir -Recurse -File | Where-Object { $_.Extension -ne ".pdb" }
$publishSizeMB = [math]::Round(($allFiles | Measure-Object -Property Length -Sum).Sum / 1MB, 2)
Write-Host "  文件数: $($allFiles.Count) | 大小: ${publishSizeMB} MB" -ForegroundColor DarkGray
Write-Host ""

# ========================================
# 步骤 2：打包为 ZIP
# ========================================
Write-Host "[2/3] 正在打包发布文件..." -ForegroundColor Green

if (-not (Test-Path $OutputDir)) {
    New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
}

$TempDir = Join-Path $env:TEMP "GameSave-Build-$([System.Guid]::NewGuid().ToString('N').Substring(0,8))"
$TempZip = Join-Path $TempDir "payload.zip"
$TempPayloadDir = Join-Path $TempDir "payload"

try {
    New-Item -ItemType Directory -Path $TempPayloadDir -Force | Out-Null

    # 使用 Copy-Item 复制文件（排除 .pdb）
    Get-ChildItem -Path $PublishDir -Recurse -File | Where-Object { $_.Extension -ne ".pdb" } | ForEach-Object {
        $relativePath = $_.FullName.Substring($PublishDir.TrimEnd('\').Length + 1)
        $destPath = Join-Path $TempPayloadDir $relativePath
        $destDir = Split-Path -Parent $destPath
        if (-not (Test-Path $destDir)) {
            New-Item -ItemType Directory -Path $destDir -Force | Out-Null
        }
        Copy-Item -Path $_.FullName -Destination $destPath -Force
    }

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [System.IO.Compression.ZipFile]::CreateFromDirectory($TempPayloadDir, $TempZip, [System.IO.Compression.CompressionLevel]::Optimal, $false)

    $zipSize = [math]::Round((Get-Item $TempZip).Length / 1MB, 2)
    Write-Host "  ZIP 大小: ${zipSize} MB" -ForegroundColor DarkGray
    Write-Host ""

    # ========================================
    # 步骤 3：生成自解压启动器 EXE
    # ========================================
    Write-Host "[3/3] 正在生成自解压启动器..." -ForegroundColor Green

    $IconFile = Join-Path $GameSaveDir "Assets\app.ico"
    $OutputExe = Join-Path $OutputDir "GameSave-$Platform-Portable-v$Version.exe"

    # 自解压启动器 C# 源码
    $LauncherSource = @'
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;

namespace GameSaveLauncher
{
    class Program
    {
        const string APP_VERSION = "REPLACE_VERSION";

        [STAThread]
        static int Main(string[] args)
        {
            // 程序解压目录：%LOCALAPPDATA%\GameSave-Portable（用户不可见）
            string appDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "GameSave-Portable");

            // 便携版工作目录：便携版 EXE 旁边的 GameSave-Data 文件夹（用户数据在这里）
            string selfPath = Process.GetCurrentProcess().MainModule.FileName;
            string selfDir = Path.GetDirectoryName(selfPath);
            string portableWorkDir = Path.Combine(selfDir, "GameSave-Data");

            string versionFile = Path.Combine(appDir, ".version");
            string targetExe = Path.Combine(appDir, "GameSave.exe");

            bool needExtract = !File.Exists(targetExe) ||
                               !File.Exists(versionFile) ||
                               File.ReadAllText(versionFile).Trim() != APP_VERSION;

            if (needExtract)
            {
                try
                {
                    byte[] selfBytes = File.ReadAllBytes(selfPath);

                    long zipOffset = BitConverter.ToInt64(selfBytes, selfBytes.Length - 8);
                    int zipLength = selfBytes.Length - 8 - (int)zipOffset;

                    Directory.CreateDirectory(appDir);

                    using (var ms = new MemoryStream(selfBytes, (int)zipOffset, zipLength))
                    using (var archive = new ZipArchive(ms, ZipArchiveMode.Read))
                    {
                        foreach (var entry in archive.Entries)
                        {
                            if (string.IsNullOrEmpty(entry.Name)) continue;

                            string destPath = Path.Combine(appDir, entry.FullName);
                            string destDir = Path.GetDirectoryName(destPath);
                            if (!Directory.Exists(destDir))
                                Directory.CreateDirectory(destDir);

                            try
                            {
                                entry.ExtractToFile(destPath, true);
                            }
                            catch { }
                        }
                    }

                    File.WriteAllText(versionFile, APP_VERSION);
                }
                catch (Exception ex)
                {
                    System.Windows.Forms.MessageBox.Show(
                        "解压失败: " + ex.Message,
                        "GameSave Manager",
                        System.Windows.Forms.MessageBoxButtons.OK,
                        System.Windows.Forms.MessageBoxIcon.Error);
                    return 1;
                }
            }

            if (!File.Exists(targetExe))
            {
                System.Windows.Forms.MessageBox.Show(
                    "GameSave.exe 未找到，请重新下载。",
                    "GameSave Manager",
                    System.Windows.Forms.MessageBoxButtons.OK,
                    System.Windows.Forms.MessageBoxIcon.Error);
                return 1;
            }

            try
            {
                // 确保便携版工作目录存在
                Directory.CreateDirectory(portableWorkDir);

                // 构建启动参数：默认包含便携版工作目录参数
                string arguments = "--portable-workdir \"" + portableWorkDir + "\"";

                // 检查用户是否通过命令行传入了 --workdir 参数，如果有则透传（优先级最高）
                for (int i = 0; i < args.Length - 1; i++)
                {
                    if (args[i].Equals("--workdir", StringComparison.OrdinalIgnoreCase))
                    {
                        arguments += " --workdir \"" + args[i + 1] + "\"";
                        break;
                    }
                }

                // 检查是否有 --silent 参数（用于开机自启等场景）
                for (int i = 0; i < args.Length; i++)
                {
                    if (args[i].Equals("--silent", StringComparison.OrdinalIgnoreCase))
                    {
                        arguments += " --silent";
                        break;
                    }
                }

                var psi = new ProcessStartInfo
                {
                    FileName = targetExe,
                    // 通过命令行参数告诉应用便携版工作目录和其他参数
                    Arguments = arguments,
                    WorkingDirectory = appDir,
                    UseShellExecute = true
                };
                Process.Start(psi);
                return 0;
            }
            catch (Exception ex)
            {
                System.Windows.Forms.MessageBox.Show(
                    "启动失败: " + ex.Message,
                    "GameSave Manager",
                    System.Windows.Forms.MessageBoxButtons.OK,
                    System.Windows.Forms.MessageBoxIcon.Error);
                return 1;
            }
        }
    }
}
'@

    # 替换版本号
    $LauncherSource = $LauncherSource.Replace("REPLACE_VERSION", $Version)

    $LauncherCs = Join-Path $TempDir "Launcher.cs"
    [System.IO.File]::WriteAllText($LauncherCs, $LauncherSource, [System.Text.Encoding]::UTF8)

    # 查找 csc.exe
    $CscPath = $null
    $frameworkDirs = @("C:\Windows\Microsoft.NET\Framework64", "C:\Windows\Microsoft.NET\Framework")
    foreach ($fdir in $frameworkDirs) {
        if (Test-Path $fdir) {
            $found = Get-ChildItem $fdir -Recurse -Filter "csc.exe" -ErrorAction SilentlyContinue | Sort-Object FullName -Descending | Select-Object -First 1
            if ($found) {
                $CscPath = $found.FullName
                break
            }
        }
    }

    if (-not $CscPath) {
        Write-Host "错误：无法找到 csc.exe 编译器" -ForegroundColor Red
        exit 1
    }

    Write-Host "  使用编译器: $CscPath" -ForegroundColor DarkGray

    $LauncherExeTemp = Join-Path $TempDir "GameSave-Portable.exe"

    $cscArgList = "/target:winexe /out:`"$LauncherExeTemp`" /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll /reference:System.Windows.Forms.dll /optimize+"
    if (Test-Path $IconFile) {
        $cscArgList += " /win32icon:`"$IconFile`""
    }
    $cscArgList += " `"$LauncherCs`""

    # 使用 cmd /c 调用 csc 避免 PowerShell 参数解析问题
    $cscCmd = "`"$CscPath`" $cscArgList"
    cmd /c $cscCmd 2>&1 | ForEach-Object { Write-Host "  $_" -ForegroundColor DarkGray }

    if (-not (Test-Path $LauncherExeTemp)) {
        Write-Host "错误：启动器编译失败" -ForegroundColor Red
        exit 1
    }

    Write-Host "  启动器编译成功" -ForegroundColor DarkGray

    # 将 EXE + ZIP 合并为一个文件
    # 格式：[启动器 EXE][ZIP 数据][8字节ZIP偏移量]
    $launcherBytes = [System.IO.File]::ReadAllBytes($LauncherExeTemp)
    $zipBytes = [System.IO.File]::ReadAllBytes($TempZip)
    $offsetBytes = [BitConverter]::GetBytes([long]$launcherBytes.Length)

    $fs = [System.IO.File]::Create($OutputExe)
    try {
        $fs.Write($launcherBytes, 0, $launcherBytes.Length)
        $fs.Write($zipBytes, 0, $zipBytes.Length)
        $fs.Write($offsetBytes, 0, $offsetBytes.Length)
    } finally {
        $fs.Close()
    }

    Write-Host "  合并完成" -ForegroundColor DarkGray

} finally {
    # 清理临时目录
    if (Test-Path $TempDir) {
        Remove-Item -Path $TempDir -Recurse -Force -ErrorAction SilentlyContinue
    }
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Green
Write-Host "  便携版构建完成！" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host ""

$exeFile = Get-Item $OutputExe
$exeSizeMB = [math]::Round($exeFile.Length / 1MB, 2)
Write-Host "  输出文件: $($exeFile.FullName)" -ForegroundColor Cyan
Write-Host "  文件大小: ${exeSizeMB} MB" -ForegroundColor Cyan
Write-Host ""
Write-Host "  使用方法:" -ForegroundColor Yellow
Write-Host "    双击 GameSave-$Platform-Portable-v$Version.exe 即可运行！" -ForegroundColor Yellow
Write-Host "    程序解压到 %LOCALAPPDATA%\GameSave-Portable（用户无感）" -ForegroundColor Yellow
Write-Host "    工作目录自动创建在 EXE 旁边的 GameSave-Data 文件夹（配置和存档数据）" -ForegroundColor Yellow
Write-Host "    后续运行直接启动，版本更新时自动覆盖" -ForegroundColor Yellow
Write-Host ""
