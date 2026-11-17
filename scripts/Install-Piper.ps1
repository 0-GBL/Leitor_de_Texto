param([string]$Destination = (Join-Path $env:LOCALAPPDATA 'Leitor\piper'))
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false)
$OutputEncoding = [Console]::OutputEncoding
New-Item -ItemType Directory -Force -Path $Destination | Out-Null
$readyFile = Join-Path $Destination 'ready'
if (Test-Path -LiteralPath $readyFile) { Remove-Item -LiteralPath $readyFile }
# These resources live alongside the installed script; the repository keeps them in runtime/.
$resourceRoot = $PSScriptRoot
if (!(Test-Path -LiteralPath (Join-Path $resourceRoot 'check_piper.py'))) {
    $resourceRoot = Join-Path (Split-Path $PSScriptRoot -Parent) 'runtime'
}
$archive = Join-Path $Destination 'python.zip'
if (!(Test-Path (Join-Path $Destination 'python.exe'))) {
    Write-Output 'Baixando Python local...'
    Invoke-WebRequest 'https://www.python.org/ftp/python/3.11.9/python-3.11.9-embed-amd64.zip' -OutFile $archive
    Expand-Archive -LiteralPath $archive -DestinationPath $Destination -Force
    Remove-Item -LiteralPath $archive
}
$pth = Join-Path $Destination 'python311._pth'
@('python311.zip', '.', 'Lib\site-packages', 'import site') | Set-Content -LiteralPath $pth -Encoding ascii
$pythonExe = Join-Path $Destination 'python.exe'
Write-Output 'Preparando instalador de pacotes...'
$bootstrap = Join-Path $Destination 'get-pip.py'
Invoke-WebRequest 'https://bootstrap.pypa.io/get-pip.py' -OutFile $bootstrap
& $pythonExe $bootstrap --no-warn-script-location
if ($LASTEXITCODE -ne 0) { throw 'Falha ao instalar pip.' }
Write-Output 'Instalando Piper TTS 1.8.0 e dependências locais...'
& $pythonExe -m pip install --disable-pip-version-check --no-warn-script-location -r (Join-Path $resourceRoot 'piper-requirements.txt')
if ($LASTEXITCODE -ne 0) { throw 'Falha ao instalar Piper.' }
& $pythonExe -m pip check
if ($LASTEXITCODE -ne 0) { throw 'Dependências incompatíveis no ambiente Piper.' }
& $pythonExe (Join-Path $resourceRoot 'check_piper.py')
if ($LASTEXITCODE -ne 0) { throw 'Não foi possível carregar o Piper.' }
Set-Content -LiteralPath $readyFile -Value 'piper-tts 1.8.0'
Write-Output 'Instalação concluída. Baixe uma voz na aplicação.'
