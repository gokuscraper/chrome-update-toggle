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
  Ok '版本.txt有版本号' ($vtxt -match 'v1\.1\.0') ($vtxt.Split("`n")[0])
  Remove-Item -LiteralPath $zip -Force
}
$logDir = Join-Path (Split-Path -Parent $exe) 'logs'
$today = Join-Path $logDir ((Get-Date).ToString('yyyy-MM-dd') + '.log')
Ok '常驻日志已落盘' (Test-Path -LiteralPath $today) $today
W "================ 共 PASS=$pass FAIL=$fail ================"
if ($fail -eq 0) { W 'DONE PASS' } else { W 'DONE FAIL' }
