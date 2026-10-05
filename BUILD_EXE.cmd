@echo off
setlocal
cd /d "%~dp0"

set "NO_PAUSE="
if /I "%~1"=="--no-pause" set "NO_PAUSE=1"

set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"

if not exist "%CSC%" (
    echo.
    echo [ERROR] Windows .NET Framework C# compiler csc.exe was not found.
    echo Enable or install .NET Framework 4.x, then run this file again.
    echo.
    if not defined NO_PAUSE pause
    exit /b 1
)

if not exist "Build\CompilerSources.rsp" (
    echo.
    echo [ERROR] Build\CompilerSources.rsp was not found.
    echo.
    if not defined NO_PAUSE pause
    exit /b 1
)

echo Building MangaAuthorSorter.exe...

"%CSC%" /nologo /target:winexe /platform:anycpu /optimize+ ^
 /out:"MangaAuthorSorter.exe" ^
 /win32icon:"Assets\AppIcon.ico" ^
 /resource:"Assets\AppIcon.ico",MangaAuthorSorter.AppIcon.ico ^
 /resource:"Assets\AppIcon64.png",MangaAuthorSorter.AppIcon64.png ^
 /reference:System.dll ^
 /reference:System.Core.dll ^
 /reference:System.Drawing.dll ^
 /reference:System.Windows.Forms.dll ^
 /reference:System.Web.Extensions.dll ^
 /reference:System.IO.Compression.dll ^
 /reference:System.IO.Compression.FileSystem.dll ^
 @"Build\CompilerSources.rsp"

if errorlevel 1 (
    echo.
    echo [ERROR] C# compilation failed.
    echo Please send the compiler output to ChatGPT.
    echo.
    if not defined NO_PAUSE pause
    exit /b 1
)

echo.
echo [OK] Generated:
echo %CD%\MangaAuthorSorter.exe
echo.
if not defined NO_PAUSE pause
exit /b 0
