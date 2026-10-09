# 反向测试: 名字撞车(gupdate开头)但路径无Google的假服务, --disable 必须碰都不碰
$ErrorActionPreference = 'Continue'
$dir = Split-Path -Parent $MyInvocation.MyCommand.Path
$exe = Join-Path $dir 'bin\Release\fx\publish\ChromeUpdateToggle.exe'
$log = Join-Path $dir 'test-fake-service.log'
"" | Out-File -FilePath $log -Encoding UTF8
function W($s) { Write-Host $s; Add-Content -Path $log -Value $s -Encoding UTF8 }
$pass = 0; $fail = 0
function Ok($name, $cond, $detail) {
  if ($cond) { $script:pass++; W "[PASS] $name -- $detail" }
  else { $script:fail++; W "[FAIL] $name -- $detail" }
}
function SvcMode($n) {
  (Get-CimInstance Win32_Service | Where-Object { $_.Name -eq $n } | Select-Object -First 1).StartMode
}

net session >$null 2>&1
if ($LASTEXITCODE -ne 0) { W '[FAIL] 需要管理员身份运行'; W 'DONE FAIL'; exit 1 }
Ok '管理员身份' $true '已提升'

$fake = 'gupdate-fake-test'
sc.exe delete $fake >$null 2>&1
sc.exe create $fake binPath= "C:\Windows\System32\svchost.exe -k gupdatefake" start= demand >$null 2>&1
Ok '假服务已创建' ((SvcMode $fake) -eq 'Manual') "StartMode=$(SvcMode $fake)"
W ("真服务改前: " + ((Get-CimInstance Win32_Service | Where-Object { $_.Name -like 'GoogleUpdater*' } | ForEach-Object { "$($_.Name)=$($_.StartMode)" }) -join ' | '))

W '--- EXE --disable ---'
$p1 = Start-Process -FilePath $exe -ArgumentList '--disable' -Wait -PassThru -WindowStyle Hidden
Ok 'disable退出码=0' ($p1.ExitCode -eq 0) "exit=$($p1.ExitCode)"
Ok '假服务纹丝不动' ((SvcMode $fake) -eq 'Manual') "改后StartMode=$(SvcMode $fake)"
$realDis = ((Get-CimInstance Win32_Service | Where-Object { $_.Name -like 'GoogleUpdater*' -and $_.StartMode -eq 'Disabled' } | Measure-Object).Count -ge 1)
Ok '真服务照常被禁' $realDis '至少1个Disabled'

W '--- EXE --enable 还原 ---'
$p2 = Start-Process -FilePath $exe -ArgumentList '--enable' -Wait -PassThru -WindowStyle Hidden
Ok 'enable退出码=0' ($p2.ExitCode -eq 0) "exit=$($p2.ExitCode)"

sc.exe delete $fake >$null 2>&1
Start-Sleep -Seconds 2
Ok '假服务已清理' ($null -eq (Get-CimInstance Win32_Service | Where-Object { $_.Name -eq $fake })) 'sc delete完成'
W "================ 共 PASS=$pass FAIL=$fail ================"
if ($fail -eq 0) { W 'DONE PASS' } else { W 'DONE FAIL' }
