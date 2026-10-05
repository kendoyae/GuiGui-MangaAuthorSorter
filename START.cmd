@echo off
setlocal
cd /d "%~dp0"

if not exist "MangaAuthorSorter.exe" (
    call BUILD_EXE.cmd --no-pause
    if errorlevel 1 (
        pause
        exit /b 1
    )
)

start "" "MangaAuthorSorter.exe"
