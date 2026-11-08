$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

$yml = Get-Content '.github/workflows/build.yml' -Raw
function Get-YmlValue([string] $name) {
    $match = [regex]::Match($yml, "(?m)^\s+$name\s*:\s*(\S+)\s*$")
    if (-not $match.Success) { throw "Falta $name en build.yml" }
    $match.Groups[1].Value
}
$appName = Get-YmlValue 'APP_NAME'
$outputDir = Get-YmlValue 'OUTPUT_DIR'

$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { throw "No se encontro csc.exe de .NET Framework 4" }

New-Item -ItemType Directory -Force $outputDir | Out-Null
$output = Join-Path $outputDir "$appName.exe"

# Graba el repositorio y el numero de version (el run de GitHub, que siempre sube de a uno) para el aviso de actualizacion.
# En GitHub Actions GITHUB_REPOSITORY y GITHUB_RUN_NUMBER llegan automaticamente; en local quedan vacios/0 (aviso desactivado).
$repo = if ($env:GITHUB_REPOSITORY) { $env:GITHUB_REPOSITORY } else { '' }
$version = if ($env:GITHUB_RUN_NUMBER) { $env:GITHUB_RUN_NUMBER } else { '0' }
$buildInfo = "namespace CandadoApp { internal static class BuildInfo { public const string Repo = `"$repo`"; public const int Version = $version; } }"
Set-Content (Join-Path $PSScriptRoot 'src\BuildInfo.cs') $buildInfo -Encoding UTF8

& $csc /nologo /target:winexe /optimize+ /platform:anycpu "/out:$output" `
    /win32icon:assets\app.ico /win32manifest:src\app.manifest `
    /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll `
    /reference:System.Windows.Forms.dll /reference:System.Xml.dll `
    src\*.cs
if ($LASTEXITCODE -ne 0) { throw "Error de compilacion" }

Write-Host "Creado: $output"
