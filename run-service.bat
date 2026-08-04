@echo off
setlocal

pushd "%~dp0"

set PROJECT=DeskSpaceOS.Service\DeskSpaceOS.Service.csproj

echo Closing existing service instance...
taskkill /IM DeskSpaceOS.Service.exe /F >nul 2>nul

echo Starting service from current source...
dotnet run --project "%PROJECT%" -c Debug -p:Platform=x64

pause
popd
