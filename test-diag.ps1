# 诊断包验证（管理员运行）: 导出zip并检查四件套 + 回归21项不断
$ErrorActionPreference = 'Continue'
$dir = Split-Path -Parent $MyInvocation.MyCommand.Path
$exe = Join-Path $dir 'bin\Release\fx\publish\ChromeUpdateToggle.exe'
$log = Join-Path $dir 'test-diag-result.log'
"" | Out-File -FilePath $log -Encoding UTF8
function W($s) { Write-Host $s; Add-Content -Path $log -Value $s -Encoding UTF8 }
$pass = 0; $fail = 0
function Ok($name, $cond, $detail) {
  if ($cond) { $script:pass++; W "[PASS] $name -- $detail" }
  else { $script:fail++; W "[FAIL] $name -- $detail" }
}
net session >$null 2>&1
if ($LASTEXITCODE -ne 0) { W '[FAIL] 需要管理员身份运行'; W 'DONE FAIL'; exit 1 }

$zip = Join-Path $env:TEMP 'CUT-diag-test.zip'
if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }
$p = Start-Process -FilePath $exe -ArgumentList '--export-diagnostics', $zip -Wait -PassThru -WindowStyle Hidden
Ok 'export退出码=0' ($p.ExitCode -eq 0) "exit=$($p.ExitCode)"
Ok 'zip已生成' (Test-Path -LiteralPath $zip) $zip
if (Test-Path -LiteralPath $zip) {
  Add-Type -AssemblyName System.IO.Compression.FileSystem
  $entries = [System.IO.Compression.ZipFile]::OpenRead($zip).Entries.FullName
  W ("包内: " + ($entries -join ' | '))
  Ok '含状态.txt' ($entries -contains '状态.txt') '有'
  Ok '含baseline.json' ($entries -contains 'baseline.json') '有'
  Ok '含版本.txt' ($entries -contains '版本.txt') '有'
  Ok '含logs/' (($entries | Where-Object { $_ -like 'logs/*' }).Count -ge 1) '有'
  $st = [System.IO.Compression.ZipFile]::OpenRead($zip).GetEntry('状态.txt')
  $sr = New-Object IO.StreamReader($st.Open())
  $txt = $sr.ReadToEnd(); $sr.Close()
  Ok '状态.txt非空且有判定' ($txt -match '更新正常|已禁止更新|状态不一致') ($txt.Split("`n")[0])
  $vr = [System.IO.Compression.ZipFile]::OpenRead($zip).GetEntry('版本.txt')
  $vrR = New-Object IO.StreamReader($vr.Open())
  $vtxt = $vrR.ReadToEnd(); $vrR.Close()
  Ok '版本.txt有版本号' ($vtxt -match 'v\d+\.\d+\.\d+') ($vtxt.Split("`n")[0])
  Remove-Item -LiteralPath $zip -Force
}
$logDir = Join-Path (Split-Path -Parent $exe) 'logs'
$today = Join-Path $logDir ((Get-Date).ToString('yyyy-MM-dd') + '.log')
Ok '常驻日志已落盘' (Test-Path -LiteralPath $today) $today

W '--- i18n EN ---'
$pl = Start-Process -FilePath $exe -ArgumentList '--lang', 'en' -Wait -PassThru -WindowStyle Hidden
Ok 'lang en退出码=0' ($pl.ExitCode -eq 0) "exit=$($pl.ExitCode)"
$langFile = Join-Path (Split-Path -Parent $exe) 'lang.txt'
Ok 'lang.txt=en' ((Get-Content -LiteralPath $langFile -Raw).Trim() -eq 'en') (Get-Content -LiteralPath $langFile -Raw).Trim()
$zip2 = Join-Path $env:TEMP 'CUT-diag-test-en.zip'
if (Test-Path -LiteralPath $zip2) { Remove-Item -LiteralPath $zip2 -Force }
$pe = Start-Process -FilePath $exe -ArgumentList '--export-diagnostics', $zip2 -Wait -PassThru -WindowStyle Hidden
Ok 'EN export退出码=0' ($pe.ExitCode -eq 0) "exit=$($pe.ExitCode)"
Add-Type -AssemblyName System.IO.Compression.FileSystem
$en2 = [System.IO.Compression.ZipFile]::OpenRead($zip2).Entries.FullName
W ("EN包内: " + ($en2 -join ' | '))
Ok 'EN含status.txt' ($en2 -contains 'status.txt') '有'
Ok 'EN含version.txt' ($en2 -contains 'version.txt') '有'
$se = [System.IO.Compression.ZipFile]::OpenRead($zip2).GetEntry('status.txt')
$sr2 = New-Object IO.StreamReader($se.Open())
$etxt = $sr2.ReadToEnd(); $sr2.Close()
Ok 'EN状态行是英文' ($etxt -match 'Updates NORMAL|Updates DISABLED|INCONSISTENT') ($etxt.Split("`n")[0])
Remove-Item -LiteralPath $zip2 -Force
$pb = Start-Process -FilePath $exe -ArgumentList '--lang', 'zh' -Wait -PassThru -WindowStyle Hidden
Ok '切回中文' (((Get-Content -LiteralPath $langFile -Raw).Trim() -eq 'zh') -and ($pb.ExitCode -eq 0)) "lang=$((Get-Content -LiteralPath $langFile -Raw).Trim())"
W "================ 共 PASS=$pass FAIL=$fail ================"
if ($fail -eq 0) { W 'DONE PASS' } else { W 'DONE FAIL' }
