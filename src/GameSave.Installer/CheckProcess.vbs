On Error Resume Next
Dim wmi, processes
Set wmi = GetObject("winmgmts:\\.\root\cimv2")
Set processes = wmi.ExecQuery("SELECT * FROM Win32_Process WHERE Name = 'GameSave.exe'")
If processes.Count > 0 Then
    Session.Property("GAMESAVERUNNING") = "1"
End If
