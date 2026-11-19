$ErrorActionPreference = 'Stop'
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$libraries = @(
    'System.Net.Http.dll'
    'System.Web.Extensions.dll'
    'System.Windows.Forms.dll'
    'System.Drawing.dll'
    'WPF\PresentationCore.dll'
    'WPF\PresentationFramework.dll'
    'WPF\WindowsBase.dll'
    'WPF\UIAutomationClient.dll'
    'WPF\UIAutomationTypes.dll'
    'System.Xaml.dll'
)
$references = $libraries | ForEach-Object { '/reference:' + (Join-Path $framework $_) }
$sources = Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.cs' -File | ForEach-Object FullName
$output = Join-Path $PSScriptRoot 'CodeLens.exe'
$resource = (Join-Path $PSScriptRoot 'Popup.xaml') + ',Popup.xaml'
$manifest = Join-Path $PSScriptRoot 'app.manifest'

& (Join-Path $framework 'csc.exe') /nologo /target:winexe /optimize+ /platform:x64 /codepage:65001 "/out:$output" "/resource:$resource" "/win32manifest:$manifest" @references @sources
if ($LASTEXITCODE -ne 0) {
    throw "CodeLens build failed (exit $LASTEXITCODE)."
}
