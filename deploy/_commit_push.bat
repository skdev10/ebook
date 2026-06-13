@echo off
cd /d c:\Users\Hp\Desktop\newEbook
git add Program.cs appsettings.Production.json deploy
git commit -F .git\COMMIT_MSG_TMP
git push origin Clean_Code
