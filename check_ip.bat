@echo off
chcp 65001 >nul
ipconfig | findstr /i "IPv4" > "%~dp0my_ip.txt"
echo IP 확인 완료! my_ip.txt에 저장됨
type "%~dp0my_ip.txt"
pause
