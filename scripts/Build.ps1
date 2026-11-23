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
    $buildProps = [xml](Get-Content -Raw -LiteralPath Directory.Build.props)
    $version = $buildProps.Project.PropertyGroup.Version
    if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'Versão de distribuição inválida.' }
    $artifacts = New-Item -ItemType Directory -Force -Path (Join-Path $projectRoot 'artifacts')
    $target = Join-Path $artifacts.FullName $version
    $stagingParent = New-Item -ItemType Directory -Force -Path (Join-Path $projectRoot '.tools/release-staging')
    $staging = New-Item -ItemType Directory -Path (Join-Path $stagingParent.FullName ([guid]::NewGuid().ToString('N')))
    $delivery = (New-Item -ItemType Directory -Path (Join-Path $staging.FullName 'delivery')).FullName
    $portable = Join-Path $staging.FullName 'portable'
    $portableArchive = Join-Path $delivery 'Leitor-portatil.zip'
    $installer = Join-Path $delivery 'Leitor-Instalador.exe'
    & $dotnetExe publish src/Leitor/Leitor.csproj -c Release -r win-x64 --self-contained true -o $portable
    if ($LASTEXITCODE -ne 0) { throw 'Publicação falhou.' }
    Copy-Item -LiteralPath LICENSE -Destination (Join-Path $portable 'LICENSE.txt')
    Copy-Item -LiteralPath THIRD-PARTY-NOTICES.md,README.md,CHANGELOG.md -Destination $portable
    $portableDocs = New-Item -ItemType Directory -Force -Path (Join-Path $portable 'docs')
    Copy-Item -Path 'docs/*' -Destination $portableDocs.FullName -Recurse -Force
    Compress-Archive -Path (Join-Path $portable '*') -DestinationPath $portableArchive -Force
    $setupDirectory = Join-Path $staging.FullName 'installer'
    & $dotnetExe publish installer/Leitor.Setup.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true "-p:PayloadZip=$portableArchive" -o $setupDirectory
    if ($LASTEXITCODE -ne 0) { throw 'Instalador falhou.' }
    Move-Item -LiteralPath (Join-Path $setupDirectory 'Leitor-Instalador.exe') -Destination $installer
    $setupCheck = Start-Process -FilePath $installer -ArgumentList '--verify-payload' -WindowStyle Hidden -Wait -PassThru
    if ($setupCheck.ExitCode -ne 0) { throw "Verificação do instalador falhou: $($setupCheck.ExitCode)" }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $sourceArchive = Join-Path $delivery 'Leitor-codigo-fonte.zip'
    $sourceZip = [IO.Compression.ZipFile]::Open($sourceArchive, [IO.Compression.ZipArchiveMode]::Create)
    try {
        $sourceFiles = Get-ChildItem -LiteralPath src,tests,installer,runtime,scripts,docs,shared -Recurse -File | Where-Object { $_.FullName -notmatch '\\(bin|obj|__pycache__|piper|voices)\\' -and $_.Name -notmatch '\.(db|onnx|part|pyc)$|^(vault|settings)\.json$|^auth-state\.bin$' }
        $sourceFiles += Get-Item -LiteralPath LICENSE,README.md,CHANGELOG.md,THIRD-PARTY-NOTICES.md,Directory.Build.props,NuGet.Config,Iniciar.cmd,.gitignore
        foreach ($file in $sourceFiles) {
            $entryName = $file.FullName.Substring($projectRoot.TrimEnd('\').Length + 1).Replace('\', '/')
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($sourceZip, $file.FullName, $entryName) | Out-Null
        }
    } finally { $sourceZip.Dispose() }
    $hashes = Get-FileHash $installer,$portableArchive,$sourceArchive -Algorithm SHA256
    $hashes | ForEach-Object { "$($_.Hash)  $([IO.Path]::GetFileName($_.Path))" } | Set-Content -LiteralPath (Join-Path $delivery 'SHA256SUMS.txt')

    # Keep the previous version directory available for rollback until publication succeeds.
    $previous = Join-Path $staging.FullName 'previous'
    if (Test-Path -LiteralPath $target) {
        $existing = Get-Item -LiteralPath $target
        if (!$existing.PSIsContainer -or $existing.Attributes -band [IO.FileAttributes]::ReparsePoint -or $existing.Parent.FullName -ne $artifacts.FullName) {
            throw "Diretório de distribuição inesperado: $target"
        }
        Move-Item -LiteralPath $existing.FullName -Destination $previous
    }
    try {
        Move-Item -LiteralPath $delivery -Destination $target
    } catch {
        if (Test-Path -LiteralPath $previous) { Move-Item -LiteralPath $previous -Destination $target }
        throw
    }
    foreach ($hash in $hashes) {
        $file = Join-Path $target ([IO.Path]::GetFileName($hash.Path))
        if ((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ne $hash.Hash) {
            throw "Checksum inválido após publicação: $file. Backup em $previous."
        }
    }
    Write-Output "Pacotes conferidos em $target"
    if (Test-Path -LiteralPath $previous) { Write-Output "Distribuição anterior preservada em $previous" }
}
