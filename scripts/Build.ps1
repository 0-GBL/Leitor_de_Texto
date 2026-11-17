param([switch]$Package)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
Set-Location -LiteralPath $projectRoot
$dotnetExe = Join-Path $projectRoot '.tools\dotnet\dotnet.exe'
if (!(Test-Path $dotnetExe)) {
    New-Item -ItemType Directory -Force -Path '.tools' | Out-Null
    Invoke-WebRequest 'https://dot.net/v1/dotnet-install.ps1' -OutFile '.tools/dotnet-install.ps1'
    & ./.tools/dotnet-install.ps1 -Channel 10.0 -InstallDir (Join-Path $projectRoot '.tools\dotnet') -NoPath
}
$env:DOTNET_CLI_HOME = Join-Path $projectRoot '.tools\cli'
$env:NUGET_PACKAGES = Join-Path $projectRoot '.tools\packages'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
& $dotnetExe build src/Leitor/Leitor.csproj -c Release
if ($LASTEXITCODE -ne 0) { throw 'Compilação falhou.' }
& $dotnetExe run --project tests/Leitor.Tests/Leitor.Tests.csproj -c Release
if ($LASTEXITCODE -ne 0) { throw 'Testes falharam.' }
if ($Package) {
    & $dotnetExe publish src/Leitor/Leitor.csproj -c Release -r win-x64 --self-contained true -o artifacts/Leitor-portatil
    if ($LASTEXITCODE -ne 0) { throw 'Publicação falhou.' }
    Copy-Item -LiteralPath LICENSE -Destination artifacts/Leitor-portatil/LICENSE.txt
    Copy-Item -LiteralPath THIRD-PARTY-NOTICES.md -Destination artifacts/Leitor-portatil/
    Copy-Item -LiteralPath README.md -Destination artifacts/Leitor-portatil/
    New-Item -ItemType Directory -Force -Path artifacts/Leitor-portatil/docs | Out-Null
    Get-ChildItem -LiteralPath docs -File | Copy-Item -Destination artifacts/Leitor-portatil/docs -Force
    Compress-Archive -Path 'artifacts/Leitor-portatil/*' -DestinationPath artifacts/Leitor-portatil.zip -Force
    & $dotnetExe publish installer/Leitor.Setup.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o artifacts/Instalador
    if ($LASTEXITCODE -ne 0) { throw 'Instalador falhou.' }
    Copy-Item -LiteralPath artifacts/Instalador/Leitor-Instalador.exe -Destination artifacts/Leitor-Instalador.exe -Force
    $setupCheck = Start-Process -FilePath (Join-Path $projectRoot 'artifacts\Leitor-Instalador.exe') -ArgumentList '--verify-payload' -WindowStyle Hidden -Wait -PassThru
    if ($setupCheck.ExitCode -ne 0) { throw "Verificação do instalador falhou: $($setupCheck.ExitCode)" }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $sourceArchive = Join-Path $projectRoot 'artifacts\Leitor-codigo-fonte.zip'
    if (Test-Path -LiteralPath $sourceArchive) { Remove-Item -LiteralPath $sourceArchive }
    $sourceZip = [IO.Compression.ZipFile]::Open($sourceArchive, [IO.Compression.ZipArchiveMode]::Create)
    try {
        $sourceFiles = Get-ChildItem -LiteralPath src,tests,installer,runtime,scripts,docs -Recurse -File | Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' }
        $sourceFiles += Get-Item -LiteralPath LICENSE,README.md,THIRD-PARTY-NOTICES.md,Directory.Build.props,NuGet.Config,Iniciar.cmd,.gitignore
        foreach ($file in $sourceFiles) {
            $entryName = $file.FullName.Substring($projectRoot.TrimEnd('\').Length + 1).Replace('\', '/')
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($sourceZip, $file.FullName, $entryName) | Out-Null
        }
    } finally { $sourceZip.Dispose() }
    Get-FileHash artifacts/Leitor-Instalador.exe,artifacts/Leitor-portatil.zip,artifacts/Leitor-codigo-fonte.zip -Algorithm SHA256 | ForEach-Object { "$($_.Hash)  $([IO.Path]::GetFileName($_.Path))" } | Set-Content -LiteralPath artifacts/SHA256SUMS.txt
    Get-FileHash artifacts/Leitor-Instalador.exe,artifacts/Leitor-portatil.zip -Algorithm SHA256 | Format-Table -AutoSize
}
