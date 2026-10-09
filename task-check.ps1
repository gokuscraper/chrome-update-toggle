# 只读巡检（提权）：任务 + 服务 + 注册表 + DENY
$log = Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) 'task-check.log'
"" | Out-File -FilePath $log -Encoding UTF8
function W($s) { Add-Content -Path $log -Value $s -Encoding UTF8 }
W '== Google计划任务 =='
Get-ScheduledTask | Where-Object { $_.TaskPath -like '*Google*' } |
  Select-Object TaskPath, TaskName, State |
  Format-Table -AutoSize | Out-String | ForEach-Object { W $_ }
W '== Google服务 =='
Get-CimInstance Win32_Service | Where-Object { $_.Name -match 'gupdate|GoogleUpdater|Elevation' } |
  Select-Object Name, StartMode, State |
  Format-Table -AutoSize | Out-String | ForEach-Object { W $_ }
W ("== 注册表策略键存在: " + (Test-Path 'HKLM:\SOFTWARE\Policies\Google\Update') + " ==")
W '== DENY残留 =='
$deny = @()
foreach ($f in @('C:\Program Files (x86)\Google\Update\GoogleUpdate.exe',
  'C:\Program Files (x86)\Google\GoogleUpdater\156.0.8067.0\updater.exe')) {
  $o = & icacls.exe $f 2>&1 | Out-String
  if ($o -match '(?i)DENY') { $deny += $f }
}
W $(if ($deny.Count -eq 0) { '(无DENY残留)' } else { $deny -join ' | ' })
W 'DONE'
