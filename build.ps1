# =====================================================================
#  FPS Booster - compilacao
#  Gera FPS Booster.exe: executavel unico, sem dependencia externa.
#  Roda sobre o .NET Framework 4.8, que ja vem no Windows 11.
# =====================================================================
param([switch]$Test)
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

$saida = Join-Path $PSScriptRoot 'FPS Booster.exe'
$temporario = Join-Path $PSScriptRoot ('FPS Booster.build-' + [guid]::NewGuid().ToString('N') + '.exe')
$fontes = Get-ChildItem (Join-Path $PSScriptRoot 'src') -Filter *.cs | ForEach-Object { $_.FullName }
$manifesto = Join-Path $PSScriptRoot 'src\app.manifest'

if ($fontes.Count -eq 0) { throw "Nenhum arquivo .cs encontrado em src\" }

# 1) csc do .NET Framework: resolve as referencias do framework sozinho.
# 2) Roslyn do Build Tools como reserva.
$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) {
    $csc = Get-ChildItem 'C:\Program Files*\Microsoft Visual Studio\*\*\MSBuild\Current\Bin\Roslyn\csc.exe' -ErrorAction SilentlyContinue |
           Select-Object -First 1 -ExpandProperty FullName
}
if (-not $csc) { throw "Nenhum compilador C# encontrado (csc.exe)." }

Write-Host ""
Write-Host "  Compilador : $csc"
Write-Host "  Fontes     : $($fontes.Count) arquivo(s)"
Write-Host "  Saida      : $saida"
Write-Host ""

$refs = @(
    '/r:System.dll'
    '/r:System.Core.dll'
    '/r:System.Management.dll'
    '/r:System.ServiceProcess.dll'
    '/r:System.Web.Extensions.dll'
)

$argumentos = @(
    '/nologo'
    '/target:exe'
    '/platform:x64'
    '/optimize+'
    '/codepage:65001'
    '/utf8output'
    "/win32manifest:$manifesto"
    "/out:$temporario"
) + $refs + $fontes

try {
    & $csc $argumentos
    if ($LASTEXITCODE -ne 0) { throw "Falha na compilacao (codigo $LASTEXITCODE)." }
    if ($Test) {
        & $temporario /selftest
        if ($LASTEXITCODE -ne 0) { throw "Falha nos testes; executavel anterior preservado." }
    }
    if (Test-Path -LiteralPath $saida) {
        [System.IO.File]::Replace($temporario, $saida, (Join-Path $PSScriptRoot 'FPS Booster.previous.exe'))
    } else {
        Move-Item -LiteralPath $temporario -Destination $saida
    }
} finally {
    if (Test-Path -LiteralPath $temporario) { Remove-Item -LiteralPath $temporario -Force }
}

$info = Get-Item $saida
Write-Host ""
Write-Host ("  OK -> {0}  ({1:N0} KB)" -f $info.Name, ($info.Length / 1KB)) -ForegroundColor Green
Write-Host "  Execute como administrador." -ForegroundColor DarkGray
Write-Host ""
