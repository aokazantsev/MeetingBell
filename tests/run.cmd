@echo off
setlocal
cd /d "%~dp0.."
if not exist tests\obj mkdir tests\obj
"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /codepage:65001 /target:exe /platform:x64 /out:tests\obj\MeetingBellTests.exe /main:MeetingBell.Tests /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll src\*.cs tests\Tests.cs
if errorlevel 1 exit /b 1
tests\obj\MeetingBellTests.exe %*
