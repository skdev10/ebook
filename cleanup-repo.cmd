@echo off
cd /d "c:\Users\Hp\Desktop\newEbook"
git add -A
git status
git commit -m "chore: remove temporary git helper artifacts"
git push ebookai Clean_Code:Clean_Code
git push origin Clean_Code:Clean-Code
