@echo off
cd /d "c:\Users\Hp\Desktop\newEbook"
git add -A
git commit -m "fix: dashboard UI updates and Stripe configuration guard"
if errorlevel 1 exit /b 1
git push ebookai Clean_Code:Clean_Code
if errorlevel 1 exit /b 1
git push origin Clean_Code:Clean-Code
if errorlevel 1 exit /b 1
git log -1 --oneline > git-push-result.txt 2>&1
exit /b 0
