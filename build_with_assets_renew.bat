@echo off
cd /d "%~dp0"
dotnet publish BudgetForecast.sln -c ExportRelease
pause
