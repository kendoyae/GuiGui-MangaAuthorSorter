@echo off
setlocal
cd /d "%~dp0"

echo.
echo ==========================================
echo   GuiGui Release Builder
echo ==========================================
echo.

echo [1/3] Compiling Release EXE...
call "%~dp0BUILD_EXE.cmd" --no-pause
if errorlevel 1 (
    echo.
    echo [ERROR] Build failed.
    echo Please send the compiler output shown above to ChatGPT.
    echo.
    pause
    exit /b 1
)

echo [2/3] Creating release folders...

if exist "RELEASE" rmdir /s /q "RELEASE"
mkdir "RELEASE"
mkdir "RELEASE\Single_EXE"
mkdir "RELEASE\Portable"

copy /y "bin\Release\GuiGui.exe" "RELEASE\Single_EXE\GuiGui.exe" >nul
copy /y "bin\Release\GuiGui.exe" "RELEASE\Portable\GuiGui.exe" >nul
if exist "README.txt" copy /y "README.txt" "RELEASE\Portable\README.txt" >nul
if exist "CHANGELOG.md" copy /y "CHANGELOG.md" "RELEASE\Portable\CHANGELOG.md" >nul
if exist "LICENSE_NOTICES.txt" copy /y "LICENSE_NOTICES.txt" "RELEASE\Portable\LICENSE_NOTICES.txt" >nul
if exist "Languages" xcopy /e /i /y "Languages" "RELEASE\Portable\Languages" >nul
if exist "Assets" xcopy /e /i /y "Assets" "RELEASE\Portable\Assets" >nul

echo [3/3] Done.
echo.
echo Single EXE:
echo   RELEASE\Single_EXE\GuiGui.exe
echo.
echo Portable package:
echo   RELEASE\Portable\
echo.
echo Notes:
echo - Internal Docs, Build files, source code and project files are not copied to Portable.
echo - LICENSE_NOTICES.txt is public release documentation and is copied to Portable.
echo - The single EXE creates its own config files and Languages folder on first run.
echo - Help, license notices, icons, and support QR images are embedded in the EXE.
echo - Everything SDK DLL is optional. If missing, the app can download it automatically.
echo - If Everything SDK is unavailable, the app falls back to normal filesystem scanning.
echo - Target PCs need a .NET Framework 4.8-compatible runtime.
echo.
pause
