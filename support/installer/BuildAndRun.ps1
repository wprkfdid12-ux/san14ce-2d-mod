# 삼국지 14 · 2D 모드 설치기 — 윈도우에 들어 있는 C# 컴파일러(csc.exe)로 설치기를 이 PC 에서 만들어 실행
#  (인터넷에서 받은 .exe 를 그대로 실행하면 '일반적으로 다운로드되지 않습니다'·'PC 보호' 경고가 떠서, 소스로 받아 여기서 만듦)
$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
if ([string]::IsNullOrEmpty($here)) { $here = Split-Path -Parent $MyInvocation.MyCommand.Path }
try {
    $root = Split-Path -Parent (Split-Path -Parent $here)
    $sources = @('Engine.cs','Json.cs','Gh.cs','Art.cs','BuildInfo.cs','App.cs') | ForEach-Object { Join-Path $here $_ }
    foreach ($p in $sources) { if (-not (Test-Path -LiteralPath $p)) { throw ('설치기 소스가 없습니다: ' + $p + "`r`nZIP 을 통째로 새 폴더에 풀었는지 확인해 주세요.") } }
    $csc = $null
    foreach ($c in @((Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'), (Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'))) { if (Test-Path -LiteralPath $c) { $csc = $c; break } }
    if ($null -eq $csc) { throw '.NET Framework C# 컴파일러(csc.exe)를 찾을 수 없습니다. Windows 10/11 에는 기본으로 들어 있습니다.' }
    $out = Join-Path $here 'San14_2D_Setup.exe'
    if (Test-Path -LiteralPath $out) {
        try { Remove-Item -LiteralPath $out -Force }
        catch { throw ('2D 모드 설치기가 이미 켜져 있습니다. 켜져 있는 설치기 창을 닫고 설치.cmd 를 다시 실행해 주세요.') }
    }
    $args2 = @('/nologo','/codepage:65001','/optimize+','/target:winexe','/main:SetupForm',
        '/reference:System.dll','/reference:System.Core.dll','/reference:System.Drawing.dll','/reference:System.Windows.Forms.dll',
        '/reference:System.IO.Compression.dll','/reference:System.IO.Compression.FileSystem.dll',('/out:' + $out)) + $sources
    $log = & $csc @args2 2>&1
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $out)) { throw ('설치기 만들기 실패 (게임 파일은 그대로입니다). 종료 코드 ' + $LASTEXITCODE + "`r`n" + ($log -join "`r`n")) }
    Start-Process -FilePath $out -WorkingDirectory $root | Out-Null
    exit 0
} catch {
    $msg = '2D 모드 설치기를 시작하지 못했습니다. 게임 파일은 바꾸지 않았습니다.' + "`r`n`r`n" + $_.Exception.Message + "`r`n`r`n" +
        '자주 있는 원인: ZIP 을 풀지 않고 안에서 바로 실행 / 설치기 창이 이미 켜져 있음 / 백신이 막음(이 폴더를 예외에 추가)' + "`r`n" +
        '이 창에서 Ctrl+C 를 누르면 글자가 복사됩니다. 그대로 댓글로 알려 주세요.'
    try { [IO.File]::WriteAllText((Join-Path $here 'BUILD_ERROR.txt'), $msg, [Text.Encoding]::UTF8) } catch {}
    try { Add-Type -AssemblyName System.Windows.Forms | Out-Null; [System.Windows.Forms.MessageBox]::Show($msg, '2D 모드 설치기', 'OK', 'Warning') | Out-Null } catch { Write-Host $msg }
    exit 1
}
