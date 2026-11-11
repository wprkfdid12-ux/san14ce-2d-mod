@echo off
setlocal
cd /d "%~dp0"
if not exist "%~dp0support\installer\BuildAndRun.ps1" (
 echo [오류] ZIP 을 새 폴더에 통째로 푼 뒤, 그 폴더의 설치.cmd 를 실행해 주세요.
 echo        support\installer 폴더가 없습니다. ZIP 안에서 바로 실행하면 이렇게 됩니다.
 pause
 exit /b 1
)
echo 2D 모드 설치기를 이 PC 에서 만드는 중입니다... (몇 초)
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0support\installer\BuildAndRun.ps1"
if errorlevel 1 (
 echo.
 echo [오류] 설치기를 시작하지 못했습니다. 뜬 창의 글자를 댓글로 알려 주세요.
 pause
 exit /b 1
)
exit /b 0
