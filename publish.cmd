@echo off
setlocal
cd /d "%~dp0"

rem Framework-dependent build (~5 MB, needs .NET 8 Desktop Runtime installed)
dotnet publish -c Release -r win-x64 --self-contained false ^
  /p:PublishSingleFile=true ^
  /p:IncludeNativeLibrariesForSelfExtract=true ^
  /p:DebugType=embedded ^
  -o publish-fd

rem Self-contained build (~70 MB compressed, no runtime needed)
dotnet publish -c Release -r win-x64 --self-contained true ^
  /p:PublishSingleFile=true ^
  /p:IncludeNativeLibrariesForSelfExtract=true ^
  /p:EnableCompressionInSingleFile=true ^
  /p:DebugType=embedded ^
  -o publish-sc

echo.
echo Framework-dependent: %CD%\publish-fd\GroqVoice.exe
echo Self-contained:      %CD%\publish-sc\GroqVoice.exe
endlocal
