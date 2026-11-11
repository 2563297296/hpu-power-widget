param([string]$NodeExe)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$csc = Join-Path $framework 'csc.exe'
$dist = Join-Path $root 'dist'
if (!(Test-Path -LiteralPath $csc -PathType Leaf)) { throw "找不到 .NET Framework C# 编译器：$csc" }
if (!$NodeExe) { $NodeExe = (Get-Command node.exe -ErrorAction Stop).Source }
$NodeExe = (Resolve-Path -LiteralPath $NodeExe).Path
$nodeVersion = (& $NodeExe -p 'process.version')
if ($LASTEXITCODE -ne 0 -or $nodeVersion -notmatch '^v\d+\.\d+\.\d+$') { throw '无法确定 Node.js 版本' }
$nodeArchitecture = (& $NodeExe -p 'process.arch')
if ($LASTEXITCODE -ne 0 -or $nodeArchitecture -ne 'x64') { throw '需要 Windows x64 Node.js' }
$license = Join-Path $root "NODE-LICENSE-$nodeVersion.txt"
if (!(Test-Path -LiteralPath $license -PathType Leaf)) { throw "缺少对应的 Node.js 官方许可证：$license" }

New-Item -ItemType Directory -Force -Path $dist | Out-Null
$stage = Join-Path $dist ('bundle-' + [Guid]::NewGuid().ToString('N'))
$directory = Join-Path $stage 'room-directory'
New-Item -ItemType Directory -Path $stage | Out-Null
try {
    & $NodeExe (Join-Path $root 'build-room-directory.js') $directory
    if ($LASTEXITCODE -ne 0) { throw '房间目录生成失败' }

    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $bundle = Join-Path $stage 'runtime.zip'
    $zip = [IO.Compression.ZipFile]::Open($bundle, [IO.Compression.ZipArchiveMode]::Create)
    try {
        $embeddedFiles = @{
            'node.exe' = $NodeExe
            'NODE-LICENSE.txt' = $license
            'fetch-usage.js' = Join-Path $root 'fetch-usage.js'
            'room_catalog.json' = Join-Path $root 'room_catalog.json'
        }
        foreach ($file in $embeddedFiles.GetEnumerator()) {
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $file.Value, $file.Key) | Out-Null
        }
        foreach ($file in Get-ChildItem -LiteralPath $directory -Filter '*.json' -Recurse -File) {
            $entry = 'room-directory/' + $file.FullName.Substring($directory.Length + 1).Replace('\','/')
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $file.FullName, $entry) | Out-Null
        }
    } finally { $zip.Dispose() }

    $sources = @(
        'AssemblyInfo.cs','Models.cs','AppData.cs','RoomDirectory.cs','SchoolClient.cs','Program.cs',
        'SettingsForm.cs','WidgetForm.cs','WidgetAppearance.cs','TaskbarWidget.cs','IconButton.cs',
        'BundledAssets.cs','StartupShortcut.cs','DiagnosticLog.cs','WidgetContextMenu.cs','TrayIcon.cs','LowBalanceReminder.cs'
    ) | ForEach-Object { Join-Path $root $_ }
    $refs = @(
        'System.dll','System.Core.dll','System.Windows.Forms.dll','System.Drawing.dll',
        'System.Web.Extensions.dll','System.Net.Http.dll','System.Xml.dll',
        'System.IO.Compression.dll','Microsoft.CSharp.dll'
    ) | ForEach-Object { '/reference:' + (Join-Path $framework $_) }
    $exe = Join-Path $dist 'HPU-Power-Widget-Windows.exe'
    $icon = Join-Path $root 'hpu-power.ico'
    $manifest = Join-Path $root 'app.manifest'
    & $csc /nologo "/win32icon:$icon" "/win32manifest:$manifest" /target:winexe /platform:x64 /optimize+ "/resource:$bundle,HpuPowerWindows.Runtime.zip" "/out:$exe" $refs $sources
    if ($LASTEXITCODE -ne 0) { throw '编译失败' }
    Write-Host "已生成单文件程序：$exe"
} finally {
    $resolvedStage = (Resolve-Path -LiteralPath $stage).Path
    $resolvedDist = (Resolve-Path -LiteralPath $dist).Path
    if ([IO.Path]::GetDirectoryName($resolvedStage) -eq $resolvedDist -and
        [IO.Path]::GetFileName($resolvedStage) -match '^bundle-[0-9a-f]{32}$') {
        Remove-Item -LiteralPath $resolvedStage -Recurse -Force
    }
}
