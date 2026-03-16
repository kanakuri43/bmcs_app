@echo off
setlocal EnableExtensions EnableDelayedExpansion
chcp 65001 >nul
title BMCS ANSI FIGlet

rem ESC文字を取得
for /f %%a in ('echo prompt $E^| cmd') do set "ESC=%%a"

rem 色定義
set "RESET=%ESC%[0m"
set "MAIN=%ESC%[38;5;208m"
set "SHADOW=%ESC%[34m"
set "TITLE=%ESC%[93m"

cls
echo %TITLE%
echo   BMCS - ANSI FIGlet Style
echo %RESET%


rem 本体を少し上に重ねる
<nul set /p="!ESC![6A"
echo %MAIN%██████╗  ███╗   ███╗  ██████╗ ███████╗%RESET%
echo %MAIN%██╔══██╗ ████╗ ████║ ██╔════╝ ██╔════╝%RESET%
echo %MAIN%██████╦╝ ██╔████╔██║ ██║      ███████╗%RESET%
echo %MAIN%██╔══██╗ ██║╚██╔╝██║ ██║      ╚════██║%RESET%
echo %MAIN%██████╦╝ ██║ ╚═╝ ██║ ╚██████╗ ███████║%RESET%
echo %MAIN%╚═════╝  ╚═╝     ╚═╝  ╚═════╝ ╚══════╝%RESET%

echo.
pause