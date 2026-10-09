# C# 版 ChromeUpdateToggle 测试（管理员运行）
# 流程: 记录基线 -> --disable -> 断言 -> --enable -> 断言 -> --reset -> 断言出厂默认
$ErrorActionPreference = 'Continue'
$dir = Split-Path -Parent $MyInvocation.MyCommand.Path
$exe = Join-Path $dir 'bin\Release\fx\publish\ChromeUpdateToggle.exe'
$log = Join-Path $dir 'test-csharp-result.log'
"" | Out-File -FilePath $log -Encoding UTF8
function W($s) { Write-Host $s; Add-Content -Path $log -Value $s -Encoding UTF8 }
$pass = 0; $fail = 0
function Ok($name, $cond, $detail) {
  if ($cond) { $script:pass++; W "[PASS] $name -- $detail" }
  else { $script:fail++; W "[FAIL] $name -- $detail" }
}
function Run-Exe($a) {
  $p = Start-Process -FilePath $exe -ArgumentList $a -Wait -PassThru -WindowStyle Hidden
  return $p.ExitCode
}
function Get-UpdaterServices {
  Get-CimInstance Win32_Service | Where-Object { $_.Name -match 'gupdate|GoogleUpdater' }
}
function Get-UpdaterExeList {
  $list = @()
  $lu = 'C:\Program Files (x86)\Google\Update\GoogleUpdate.exe'
  if (Test-Path -LiteralPath $lu) { $list += $lu }
  $gd = 'C:\Program Files (x86)\Google\GoogleUpdater'
  if (Test-Path -LiteralPath $gd) {
    Get-ChildItem -Path $gd -Filter 'updater.exe' -Recurse -ErrorAction SilentlyContinue | ForEach-Object { $list += $_.FullName }
  }
  return $list
}
function Has-Deny($f) {
  $o = & icacls.exe $f 2>&1 | Out-String
  return ($o -match '(?i)DENY')
}
function Test-WriteBlocked($f) {
  try {
    $fs = [System.IO.File]::Open($f, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Write, [System.IO.FileShare]::None)
    $fs.Close()
    return $false
  } catch { return $true }
}

net session >$null 2>&1
if ($LASTEXITCODE -ne 0) { W '[FAIL] 需要管理员身份运行'; W 'DONE FAIL'; exit 1 }
Ok '管理员身份' $true '已提升'
if (-not (Test-Path -LiteralPath $exe)) { W "[FAIL] 找不到EXE: $exe"; W 'DONE FAIL'; exit 1 }
Ok 'EXE存在' $true $exe

$svcBase = Get-UpdaterServices | Select-Object Name, StartMode, State
W ("基线服务: " + (($svcBase | ForEach-Object { "$($_.Name)=$($_.StartMode)/$($_.State)" }) -join ' | '))
$taskBase = Get-ScheduledTask | Where-Object { $_.TaskPath -like '*Google*' -and $_.TaskName -match 'Update' } |
  Select-Object @{n='Full';e={$_.TaskPath + $_.TaskName}}, State
W ("基线任务: " + $(if ($taskBase) { ($taskBase | ForEach-Object { "$($_.Full)=$($_.State)" }) -join ' | ' } else { '(无)' }))
$regKey = 'HKLM:\SOFTWARE\Policies\Google\Update'
$regExisted = Test-Path $regKey
W ("基线注册表存在: $regExisted")
$exeList = Get-UpdaterExeList
W ("基线待锁文件: " + ($exeList -join ' | '))
$baseDir = Join-Path (Split-Path -Parent $exe) 'baseline'
$baseCountBefore = 0
if (Test-Path -LiteralPath $baseDir) { $baseCountBefore = (Get-ChildItem -Path $baseDir -Directory -ErrorAction SilentlyContinue | Measure-Object).Count }
$elevBase = (Get-CimInstance Win32_Service | Where-Object { $_.Name -eq 'GoogleChromeElevationService' } | Select-Object -First 1).StartMode
Ok '基线记录' ($svcBase.Count -ge 1 -and $exeList.Count -ge 1) "服务$($svcBase.Count)个 文件$($exeList.Count)个"

W '--- EXE --disable ---'
$e1 = Run-Exe '--disable'
Ok 'disable退出码=0' ($e1 -eq 0) "exit=$e1"
$ud = (Get-ItemProperty $regKey -ErrorAction SilentlyContinue).UpdateDefault
Ok '禁止-注册表UpdateDefault=0' ($ud -eq 0) "实际值=$ud"
$allDis = $true; $detail = @()
foreach ($s in (Get-UpdaterServices)) { $detail += "$($s.Name)=$($s.StartMode)"; if ($s.StartMode -ne 'Disabled') { $allDis = $false } }
Ok '禁止-Updater服务全Disabled' $allDis ($detail -join ' | ')
$elevAfter1 = (Get-CimInstance Win32_Service | Where-Object { $_.Name -eq 'GoogleChromeElevationService' } | Select-Object -First 1).StartMode
Ok '禁止-Elevation保持不动' ($elevAfter1 -eq $elevBase) "实际=$elevAfter1 基线=$elevBase"
$t = Get-ScheduledTask | Where-Object { $_.TaskPath -like '*Google*' -and $_.TaskName -match 'Update' }
if ($t) { $okT = ($t | Where-Object { $_.State -eq 'Disabled' }).Count -eq $t.Count; Ok '禁止-任务全Disabled' $okT (($t | ForEach-Object { $_.State }) -join ',') }
else { Ok '禁止-任务(无则SKIP)' $true '无任务' }
$baseCountAfter1 = 0
if (Test-Path -LiteralPath $baseDir) { $baseCountAfter1 = (Get-ChildItem -Path $baseDir -Directory -ErrorAction SilentlyContinue | Measure-Object).Count }
Ok '禁止-基线目录已生成' ($baseCountAfter1 -gt $baseCountBefore) "前=$baseCountBefore 后=$baseCountAfter1"
$denyAll = $true; $dd = @()
foreach ($f in $exeList) { $h = Has-Deny $f; $dd += "$([IO.Path]::GetFileName($f))=$h"; if (-not $h) { $denyAll = $false } }
Ok '禁止-exe全有DENY' $denyAll ($dd -join ' | ')
$blockAll = $true; $bd = @()
foreach ($f in $exeList) { $b = Test-WriteBlocked $f; $bd += "$([IO.Path]::GetFileName($f))写拦截=$b"; if (-not $b) { $blockAll = $false } }
Ok '禁止-exe写全被拦截' $blockAll ($bd -join ' | ')

W '--- EXE --enable ---'
$e2 = Run-Exe '--enable'
Ok 'enable退出码=0' ($e2 -eq 0) "exit=$e2"
$denyGone = $true; $dg = @()
foreach ($f in $exeList) { $h = Has-Deny $f; $dg += "$([IO.Path]::GetFileName($f))残留=$h"; if ($h) { $denyGone = $false } }
Ok '恢复-DENY全清除' $denyGone ($dg -join ' | ')
$svcMatch = $true; $md = @()
foreach ($b in $svcBase) {
  $cur = Get-CimInstance Win32_Service | Where-Object { $_.Name -eq $b.Name } | Select-Object -First 1
  $md += "$($b.Name)基线=$($b.StartMode)/当前=$($cur.StartMode)"
  if ($cur.StartMode -ne $b.StartMode) { $svcMatch = $false }
}
Ok '恢复-服务回到基线' $svcMatch ($md -join ' | ')
$elevAfter2 = (Get-CimInstance Win32_Service | Where-Object { $_.Name -eq 'GoogleChromeElevationService' } | Select-Object -First 1).StartMode
Ok '恢复-Elevation仍不动' ($elevAfter2 -eq $elevBase) "实际=$elevAfter2"

W '--- EXE --reset (回出厂默认) ---'
$e3 = Run-Exe '--reset'
Ok 'reset退出码=0' ($e3 -eq 0) "exit=$e3"
$allAuto = $true; $ad = @()
foreach ($s in (Get-UpdaterServices)) { $ad += "$($s.Name)=$($s.StartMode)"; if ($s.StartMode -ne 'Auto') { $allAuto = $false } }
Ok '出厂-服务全Auto' $allAuto ($ad -join ' | ')
$t3 = Get-ScheduledTask | Where-Object { $_.TaskPath -like '*Google*' -and $_.TaskName -match 'Update' }
if ($t3) { $okT3 = ($t3 | Where-Object { $_.State -ne 'Disabled' }).Count -eq $t3.Count; Ok '出厂-任务无Disabled' $okT3 (($t3 | ForEach-Object { $_.State }) -join ',') }
else { Ok '出厂-任务(无则SKIP)' $true '无任务' }
Ok '出厂-策略键已删' (-not (Test-Path $regKey)) "存在=$((Test-Path $regKey))"
$denyGone3 = $true
foreach ($f in $exeList) { if (Has-Deny $f) { $denyGone3 = $false } }
Ok '出厂-DENY全清' $denyGone3 '无残留'
$elevAfter3 = (Get-CimInstance Win32_Service | Where-Object { $_.Name -eq 'GoogleChromeElevationService' } | Select-Object -First 1).StartMode
Ok '出厂-Elevation仍不动' ($elevAfter3 -eq $elevBase) "实际=$elevAfter3"

W "================ 共 PASS=$pass FAIL=$fail ================"
if ($fail -eq 0) { W 'DONE PASS' } else { W 'DONE FAIL' }
