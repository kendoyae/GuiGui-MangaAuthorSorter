@echo off
setlocal
cd /d "%~dp0\.."
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "bin\Tests" mkdir "bin\Tests"
"%CSC%" /nologo /target:exe /main:MangaAuthorSorter.Tests.FileNameStructureTests /out:"bin\Tests\FileNameStructureTests.exe" /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Web.Extensions.dll /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll @"Build\CompilerSources.rsp" "Tests\FileNameStructureTests.cs"
if errorlevel 1 exit /b 1
"bin\Tests\FileNameStructureTests.exe"
exit /b %errorlevel%
