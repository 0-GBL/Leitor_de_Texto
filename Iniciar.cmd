@echo off
setlocal
cd /d "%~dp0"
if exist "artifacts\Leitor-portatil\Leitor.exe" (
  start "" "artifacts\Leitor-portatil\Leitor.exe"
) else (
  set "DOTNET_ROOT=%~dp0.tools\dotnet"
  start "" "src\Leitor\bin\Release\net10.0-windows\Leitor.exe"
)
