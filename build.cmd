@echo off
chcp 65001 >nul
rem 윈도우에 기본으로 있는 .NET Framework C# 컴파일러로 파자마캡쳐.exe 를 만든다(설치할 것 없음).
setlocal
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
  echo C# 컴파일러를 찾지 못했어요: %CSC%
  exit /b 1
)
cd /d "%~dp0"
"%CSC%" /nologo /utf8output /codepage:65001 /optimize+ /target:winexe /platform:anycpu /out:파자마캡쳐.exe /win32icon:icon.ico /win32manifest:app.manifest /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Core.dll src\Native.cs src\ScreenGrab.cs src\GifEncoder.cs src\Recorder.cs src\Overlay.cs src\Popups.cs src\PinkTheme.cs src\EditCore.cs src\EditorForms.cs src\FrameWindow.cs src\MainForm.cs src\SelfTest.cs
if errorlevel 1 (
  echo 빌드에 실패했어요.
  exit /b 1
)
echo 빌드 완료: %~dp0파자마캡쳐.exe
