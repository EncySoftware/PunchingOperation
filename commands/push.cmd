@echo off
cd /D %~dp0

call ..\.stbuild\build.cmd --Target Push --Variant Release

pause

EXIT /B %EXIT_CODE%