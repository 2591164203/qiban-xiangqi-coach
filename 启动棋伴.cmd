@echo off
chcp 65001 >nul
set "COACH_EXE=%~dp0dist\ChessCoach-v0.5\ChessCoach.exe"
if not exist "%COACH_EXE%" (
  echo 未找到棋伴程序，请先运行 scripts\build.ps1 -Publish。
  pause
  exit /b 1
)
start "" "%COACH_EXE%"
