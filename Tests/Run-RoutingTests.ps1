param(
    [string]$EnginePath,
    [string]$SwitcherPath = (Join-Path $PSScriptRoot '..\bin\Release\Switcher.exe')
)
$ErrorActionPreference = 'Stop'
if ($EnginePath) { $EnginePath = (Resolve-Path -LiteralPath $EnginePath).Path }
$SwitcherPath = (Resolve-Path -LiteralPath $SwitcherPath).Path
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('Switcher-routing-tests-' + [guid]::NewGuid().ToString('N'))
$mockRoot = Join-Path $testRoot 'mock'
if (-not $EnginePath) { $EnginePath = Join-Path $testRoot 'Engine\sing-box.exe' }
New-Item -ItemType Directory -Path $mockRoot -Force | Out-Null
Copy-Item -LiteralPath $SwitcherPath -Destination (Join-Path $testRoot 'Switcher.exe')
$compiler = Join-Path $env:SystemRoot 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { $compiler = Join-Path $env:SystemRoot 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
& $compiler /nologo /platform:x64 /r:System.Web.Extensions.dll /target:exe "/out:$mockRoot\sing-box.exe" (Join-Path $PSScriptRoot 'MockEngine.cs')
if ($LASTEXITCODE -ne 0) { throw 'Mock compilation failed.' }
foreach ($name in @('RoutingTests', 'LifecycleTests', 'ImportTests', 'ExitNodeTests', 'AutoRoutingTests', 'RoutingUiTests', 'TunnelAddressTests', 'LayoutTests')) {
    $testExe = Join-Path $testRoot ($name + '.exe')
    & $compiler /nologo /platform:x64 /r:System.Web.Extensions.dll /target:exe "/out:$testExe" "/r:$testRoot\Switcher.exe" /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll (Join-Path $PSScriptRoot ($name + '.cs'))
    if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
    $engine = if ($name -eq 'RoutingTests') { $EnginePath } else { Join-Path $mockRoot 'sing-box.exe' }
    & $testExe $testRoot $engine
    if ($LASTEXITCODE -ne 0) { throw "$name failed." }
}
Write-Output "No live VPN or service changes. Test results: $testRoot"


