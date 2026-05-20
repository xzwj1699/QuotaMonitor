Set shell = CreateObject("WScript.Shell")
Set fso = CreateObject("Scripting.FileSystemObject")
exePath = fso.BuildPath(fso.GetParentFolderName(WScript.ScriptFullName), "QuotaMonitor.exe")
shell.Run """" & exePath & """", 1, False
