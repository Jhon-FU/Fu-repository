@echo off
setlocal
chcp 65001 >nul
title 撤销单词机社区版开机自启动
"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoLogo -NoProfile -NonInteractive -Command "$ErrorActionPreference = 'Stop'; try { $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Software\Microsoft\Windows\CurrentVersion\Run', $true); if ($null -ne $key) { try { $key.DeleteValue('WordMachineCommunity', $false); if ($null -ne $key.GetValue('WordMachineCommunity')) { throw '启动项仍存在，撤销失败。' } } finally { $key.Dispose() } }; Write-Host '已撤销单词机社区版的开机自启动（原本未开启时无需更改）。'; Write-Host '下次登录 Windows 时不再自动启动；当前程序和学习记录保持不变。'; exit 0 } catch { Write-Host ('撤销失败：' + $_.Exception.Message); exit 1 }"
set "result=%errorlevel%"
if /i not "%~1"=="--quiet" pause
exit /b %result%
