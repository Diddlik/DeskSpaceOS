@echo off
setlocal

pushd "%~dp0"

set PROJECT=DeskSpaceOS.SettingsApp\DeskSpaceOS.SettingsApp.csproj

echo Closing existing Settings app instance...
taskkill /IM DeskSpaceOS.SettingsApp.exe /F >nul 2>nul

echo Starting Settings app from current source...
dotnet run --project "%PROJECT%" -c Debug -p:Platform=x64

popd
