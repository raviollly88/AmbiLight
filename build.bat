@echo off
echo Compiling Ambilight...
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /platform:x64 /target:winexe /optimize+ /win32manifest:app.manifest /out:Ambilight.exe /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.dll AssemblyInfo.cs Program.cs
if %errorlevel% equ 0 (
    echo [OK] Build succeeded: Ambilight.exe
) else (
    echo [ERROR] Build failed!
)
pause
