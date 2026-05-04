# Stops local dotnet hosts that are running this app so MSBuild can overwrite the output DLL.
# Only matches processes whose command line references EBookDashboard output or entry assembly.
$ErrorActionPreference = 'SilentlyContinue'
$rx = [regex]::new(
    'EBookDashboard\.(?:dll|exe)|[\\/]\.msbuild-out[\\/]EBookDashboard[\\/]',
    [System.Text.RegularExpressions.RegexOptions]::IgnoreCase
)

Get-CimInstance Win32_Process -Filter "Name = 'dotnet.exe'" |
    Where-Object { $_.CommandLine -and $rx.IsMatch($_.CommandLine) } |
    ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }

Get-Process -Name 'EBookDashboard' -ErrorAction SilentlyContinue |
    ForEach-Object { Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue }

Start-Sleep -Milliseconds 400
