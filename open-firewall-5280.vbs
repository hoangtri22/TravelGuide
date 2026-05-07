' Nhap dup file nay de mo UAC va chay lenh mo cong 5280 (on dinh hon .bat tren mot may).
Option Explicit
Dim sh, fso, scriptDir, cmdPath, args
Set sh = CreateObject("Shell.Application")
Set fso = CreateObject("Scripting.FileSystemObject")
scriptDir = fso.GetParentFolderName(WScript.ScriptFullName)
cmdPath = fso.BuildPath(scriptDir, "open-firewall-5280-admin.cmd")
If Not fso.FileExists(cmdPath) Then
  MsgBox "Khong tim thay: " & cmdPath, vbCritical, "TravelGuide"
  WScript.Quit 1
End If
' Runas = yeu cau quyen Administrator
args = "/k """ & cmdPath & """"
sh.ShellExecute "cmd.exe", args, "", "runas", 1
