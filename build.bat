@echo off
"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:winexe /optimize /out:"%~dp0JamNet.exe" /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll "%~dp0JamNet.cs"
