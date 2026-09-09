' WebView2 运行时「重新检测」（迭代 44，决策 #99 D2）：
' 向导内重新读取 EdgeUpdate Clients 的 WebView2 产品键（per-machine / per-user 双视图），
' 注册表键随运行时安装即写入——装完点「重新检测」即可继续安装，无需重启安装程序。
' 函数名必须与 CustomAction Id（RecheckWebView2）一致；检测到时设置属性 WEBVIEW2_OK=1。
Function RecheckWebView2()
  On Error Resume Next
  Dim sh, v
  v = ""
  Set sh = CreateObject("WScript.Shell")
  v = sh.RegRead("HKLM\SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}\pv")
  If Err.Number <> 0 Or v = "" Then
    Err.Clear
    v = sh.RegRead("HKCU\Software\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}\pv")
  End If
  If Err.Number = 0 And v <> "" Then
    Session.Property("WEBVIEW2_OK") = "1"
  End If
  RecheckWebView2 = 1
End Function
