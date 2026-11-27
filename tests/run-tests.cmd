@echo off
chcp 65001 >nul
rem 파자마 캡쳐 시험. 먼저 build.cmd 를 실행해 둘 것. 시험 파일은 임시 폴더에만 만들고 끝나면 지운다.
rem 마지막 화면 시험은 실제 마우스·키보드를 10초쯤 움직이니, 그동안 마우스를 만지지 마세요.
setlocal
set "FW=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319"
set "OUT=%TEMP%\PajamaCaptureTest"
set "EXE=%~dp0..\파자마캡쳐.exe"
if exist "%OUT%" rmdir /s /q "%OUT%"
mkdir "%OUT%"
set FAIL=0
set PYTHONIOENCODING=utf-8

echo [1] GIF 저장기(합성 장면)
"%EXE%" --selftest gif "%OUT%\gif" || set FAIL=1
python "%~dp0check_gif.py" "%OUT%\gif" || set FAIL=1

echo [2] 실제 화면 GIF 녹화 3초
"%EXE%" --selftest record gif 3 "%OUT%\rec.gif" || set FAIL=1

echo [3] MP4 경로(가짜 ffmpeg 로 넘겨주는 형식·장면 수 확인)
"%FW%\csc.exe" /nologo /out:"%OUT%\ffmpeg.exe" "%~dp0FakeFfmpeg.cs" || set FAIL=1
"%EXE%" --selftest record mp4 3 "%OUT%\rec.mp4" "%OUT%\ffmpeg.exe" || set FAIL=1
type "%OUT%\rec.mp4"

echo [3-1] 간단 편집 — GIF(홀짝 솎기·지우기·시간·잘라내기·크기 → 저장, Pillow 로 확인)
"%EXE%" --selftest edit "%OUT%\gif\test.gif" "%OUT%\edit" || set FAIL=1
python "%~dp0check_edit.py" "%OUT%\gif\test.gif" "%OUT%\edit" || set FAIL=1
"%EXE%" --selftest editor "%OUT%\gif\test.gif" "%OUT%\editor" || set FAIL=1

echo [3-2] 간단 편집 — MP4 크기 절반(진짜 ffmpeg 가 있을 때만)
set "REALFF="
for /f "delims=" %%F in ('where ffmpeg 2^>nul') do if not defined REALFF set "REALFF=%%F"
if not defined REALFF for /d %%D in ("%LOCALAPPDATA%\Microsoft\WinGet\Packages\*FFmpeg*") do for /r "%%D" %%F in (ffmpeg.exe) do if exist "%%F" if not defined REALFF set "REALFF=%%F"
if defined REALFF (
  "%REALFF%" -hide_banner -loglevel error -y -f lavfi -i testsrc=size=640x360:rate=15 -t 2 -c:v libx264 -pix_fmt yuv420p "%OUT%\원본 영상.mp4" || set FAIL=1
  "%EXE%" --selftest mp4edit "%OUT%\원본 영상.mp4" "%OUT%\결과 영상.mp4" "%REALFF%" || set FAIL=1
) else (
  echo ffmpeg 가 없어서 건너뜀
)

echo [3-3] 녹화 틀 — 창 자리·짝수 맞춤·모니터별 배율(진짜 ffmpeg 가 있으면 MP4 녹화 크기까지)
if defined REALFF (
  "%EXE%" --selftest frame "%OUT%\frame" "%REALFF%" || set FAIL=1
) else (
  "%EXE%" --selftest frame "%OUT%\frame" || set FAIL=1
)

echo [4] 실제 앱 화면 시험(영역 캡쳐·창 캡쳐·녹화·녹화 틀 — 배율이 다른 모니터가 있으면 그쪽에서도)
"%FW%\csc.exe" /nologo /utf8output /out:"%OUT%\UiDriver.exe" /r:"%FW%\WPF\UIAutomationClient.dll" /r:"%FW%\WPF\UIAutomationTypes.dll" /r:"%FW%\WPF\WindowsBase.dll" /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Core.dll "%~dp0UiDriver.cs" || set FAIL=1
"%OUT%\UiDriver.exe" "%EXE%" "%OUT%\ui" || set FAIL=1

rmdir /s /q "%OUT%"
if %FAIL%==0 (echo 전체 통과) else (echo 실패가 있어요)
exit /b %FAIL%
