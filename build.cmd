@echo off
REM DiscordStereo derleyici - .NET Framework csc ile tek exe uretir (harici bagimlilik yok).
setlocal
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist "%CSC%" (
  echo C# derleyici bulunamadi. .NET Framework 4.x gerekli.
  exit /b 1
)
"%CSC%" /nologo /optimize+ /target:exe /platform:x64 /out:"%~dp0DiscordStereo.exe" "%~dp0src\Program.cs"
if errorlevel 1 ( echo Derleme basarisiz. & exit /b 1 )
echo Derlendi: %~dp0DiscordStereo.exe
