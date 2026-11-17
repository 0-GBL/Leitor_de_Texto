param([string]$Destination = (Join-Path $env:LOCALAPPDATA 'Leitor\piper'))
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false)
$OutputEncoding = [Console]::OutputEncoding
Add-Type -AssemblyName System.IO.Compression.FileSystem
$resourceRoot = $PSScriptRoot
if (!(Test-Path -LiteralPath (Join-Path $resourceRoot 'check_piper.py'))) { $resourceRoot = Join-Path (Split-Path $PSScriptRoot -Parent) 'runtime' }
$manifest = Get-Content -LiteralPath (Join-Path $resourceRoot 'download-manifest.json') -Raw | ConvertFrom-Json
$runtimeTarget = [IO.Path]::GetFullPath($Destination).TrimEnd('\')
if ([IO.Path]::GetFileName($runtimeTarget) -ne 'piper') { throw 'O diretório final do runtime deve se chamar piper.' }
$runtimeParent = [IO.Path]::GetDirectoryName($runtimeTarget)
New-Item -ItemType Directory -Force -Path $runtimeParent | Out-Null
if ((Test-Path -LiteralPath $runtimeTarget) -and ((Get-Item -LiteralPath $runtimeTarget).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Links não são aceitos como destino do runtime.' }
if ((Test-Path -LiteralPath $runtimeTarget) -and !(Test-Path -LiteralPath (Join-Path $runtimeTarget 'python.exe')) -and (Get-ChildItem -LiteralPath $runtimeTarget -Force | Select-Object -First 1)) { throw 'A pasta de destino contém arquivos que não pertencem ao runtime.' }
$stagePath = Join-Path $runtimeParent ('piper.stage-' + [Guid]::NewGuid().ToString('N'))
$backupPath = Join-Path $runtimeParent ('piper.backup-' + [Guid]::NewGuid().ToString('N'))
function Assert-Contained([string]$Candidate) {
    $full = [IO.Path]::GetFullPath($Candidate)
    $allowed = [IO.Path]::GetFullPath($runtimeParent).TrimEnd('\') + '\'
    if (!$full.StartsWith($allowed, [StringComparison]::OrdinalIgnoreCase) -or $full -eq $allowed.TrimEnd('\')) { throw 'Caminho fora da pasta autorizada.' }
}
function Download-Verified([string]$Url, [string]$Hash, [string]$File) {
    if (([Uri]$Url).Scheme -ne 'https') { throw 'É obrigatório utilizar HTTPS.' }
    Invoke-WebRequest -UseBasicParsing -Uri $Url -OutFile $File
    if ((Get-FileHash -LiteralPath $File -Algorithm SHA256).Hash -ne $Hash) { throw 'O hash do download não corresponde ao publicado. Nenhum código foi executado.' }
}
function Expand-Safe([string]$Archive, [string]$Folder) {
    $root = [IO.Path]::GetFullPath($Folder).TrimEnd('\') + '\'
    New-Item -ItemType Directory -Force -Path $Folder | Out-Null
    $zip = [IO.Compression.ZipFile]::OpenRead($Archive)
    try {
        foreach ($entry in $zip.Entries) {
            if ([IO.Path]::IsPathRooted($entry.FullName) -or $entry.FullName.Contains(':') -or (($entry.ExternalAttributes -shr 16) -band 61440) -eq 40960) { throw 'Entrada de pacote inválida.' }
            $entryPath = [IO.Path]::GetFullPath((Join-Path $Folder $entry.FullName))
            if (!$entryPath.StartsWith($root, [StringComparison]::OrdinalIgnoreCase)) { throw 'O pacote tenta sair do diretório autorizado.' }
            if ($entry.FullName.EndsWith('/') -or $entry.FullName.EndsWith('\')) { New-Item -ItemType Directory -Force -Path $entryPath | Out-Null; continue }
            New-Item -ItemType Directory -Force -Path ([IO.Path]::GetDirectoryName($entryPath)) | Out-Null
            [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $entryPath, $false)
        }
    } finally { $zip.Dispose() }
}
$published = $false
try {
    Assert-Contained $stagePath; Assert-Contained $backupPath; Assert-Contained $runtimeTarget
    New-Item -ItemType Directory -Path $stagePath | Out-Null
    Write-Output ('Baixando Python ' + $manifest.pythonVersion + ' com verificação SHA-256...')
    $archive = Join-Path $stagePath 'python.zip'
    Download-Verified $manifest.pythonUrl $manifest.pythonSha256 $archive
    Expand-Safe $archive $stagePath; Remove-Item -LiteralPath $archive
    @('python314.zip', '.', 'Lib\site-packages', 'import site') | Set-Content -LiteralPath (Join-Path $stagePath 'python314._pth') -Encoding ascii
    $pipWheel = Join-Path $stagePath 'pip.whl'
    Download-Verified $manifest.pipUrl $manifest.pipSha256 $pipWheel
    Expand-Safe $pipWheel (Join-Path $stagePath 'Lib\site-packages'); Remove-Item -LiteralPath $pipWheel
    $pythonExe = Join-Path $stagePath 'python.exe'
    Write-Output 'Instalando Piper e dependências com hashes fixados...'
    & $pythonExe -m pip install --isolated --disable-pip-version-check --no-warn-script-location --index-url https://pypi.org/simple --only-binary=:all: --require-hashes -r (Join-Path $resourceRoot 'piper-requirements.txt')
    if ($LASTEXITCODE -ne 0) { throw 'Falha na instalação verificada dos pacotes.' }
    & $pythonExe -m pip check
    if ($LASTEXITCODE -ne 0) { throw 'Dependências incompatíveis.' }
    & $pythonExe (Join-Path $resourceRoot 'check_piper.py')
    if ($LASTEXITCODE -ne 0) { throw 'Falha ao verificar o novo runtime.' }
    Set-Content -LiteralPath (Join-Path $stagePath 'ready') -Value ('piper-tts 1.8.0; Python ' + $manifest.pythonVersion + '; SHA-256 verificado')
    if (Test-Path -LiteralPath $runtimeTarget) { Move-Item -LiteralPath $runtimeTarget -Destination $backupPath }
    try { Move-Item -LiteralPath $stagePath -Destination $runtimeTarget; $published = $true }
    catch { if (Test-Path -LiteralPath $backupPath) { Move-Item -LiteralPath $backupPath -Destination $runtimeTarget }; throw }
    Write-Output 'Piper instalado. Biblioteca e modelos de voz foram preservados.'
} finally {
    if (Test-Path -LiteralPath $stagePath) { Assert-Contained $stagePath; Remove-Item -LiteralPath $stagePath -Recurse -Force }
    if ($published -and (Test-Path -LiteralPath $backupPath)) { Assert-Contained $backupPath; Remove-Item -LiteralPath $backupPath -Recurse -Force }
}