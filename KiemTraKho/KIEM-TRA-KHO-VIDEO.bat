@echo off
rem Keo tha thu muc kho video len file nay, hoac chay roi dan duong dan.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0KIEM-TRA-KHO-VIDEO.ps1" %1
echo.
pause
