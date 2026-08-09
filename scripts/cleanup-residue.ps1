# LabelFrame 安装残留清理（请在管理员 PowerShell 中运行）
# 删除：安装目录 / 快捷方式 / 开始菜单 / 卸载注册表项 / 数据目录
$ErrorActionPreference = 'Continue'

# 1) 停止残留服务
Get-Process -Name 'LabelFrame.WinHost' -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 1

# 2) 删除安装目录（旧版中文目录 + 新版 LabelFrame）
$dirs = @(
    'C:\Program Files (x86)\LabelFrame LabelFrame 标签打印',
    'C:\Program Files\LabelFrame',
    'C:\Program Files (x86)\LabelFrame'
)
foreach ($d in $dirs) { if (Test-Path $d) { Remove-Item -LiteralPath $d -Recurse -Force; Write-Host ('已删除目录：' + $d) } }

# 3) 删除快捷方式（公共 + 当前用户）
$lnks = @(
    'C:\ProgramData\Microsoft\Windows\Start Menu\Programs\LabelFrame',
    'C:\Users\Public\Desktop\LabelFrame 标签打印.lnk',
    'C:\Users\Public\Desktop\LabelFrame.lnk'
)
foreach ($l in $lnks) { if (Test-Path $l) { Remove-Item -LiteralPath $l -Recurse -Force; Write-Host ('已删除：' + $l) } }
Get-ChildItem "$env:APPDATA\Microsoft\Windows\Start Menu\Programs" -Recurse -Filter '*LabelFrame*' -ErrorAction SilentlyContinue | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
Get-ChildItem ([Environment]::GetFolderPath('Desktop')) -Filter '*LabelFrame*' -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue

# 4) 删除卸载注册表项
$uninstallRoots = @(
    'HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall',
    'HKLM:\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall',
    'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall'
)
foreach ($root in $uninstallRoots) {
    Get-ChildItem $root -ErrorAction SilentlyContinue | ForEach-Object {
        $props = Get-ItemProperty $_.PSPath -ErrorAction SilentlyContinue
        if ($props.DisplayName -like '*LabelFrame*') { Remove-Item -LiteralPath $_.PSPath -Recurse -Force; Write-Host ('已删除注册表项：' + $_.PSChildName) }
    }
}

# 5) 删除数据目录（作业 / 模板 / 日志测试数据）
$data = "$env:LOCALAPPDATA\LabelFrame"
if (Test-Path $data) { Remove-Item -LiteralPath $data -Recurse -Force; Write-Host '已删除数据目录。' }

Write-Host 'LabelFrame 残留清理完成。'