@echo off
chcp 65001 >nul
cd /d "%~dp0"

echo === Removing git lock file ===
if exist ".git\index.lock" (
    del /f ".git\index.lock"
    echo Lock file removed.
) else (
    echo No lock file found.
)

echo.
echo === Adding all changes ===
git add -A

echo.
echo === Committing ===
git commit -m "Add card game scenes, lobby, UI, Android/server updates, VRM character assets"

echo.
echo === Pushing to origin/main ===
git push origin main

echo.
echo === Done! ===
git log --oneline -3
echo.
pause
