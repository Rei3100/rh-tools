# PreToolUse Hook: git commit 前に dotnet build でビルドチェックを実行する
$jsonText = [System.Console]::In.ReadToEnd()
try {
    $json = $jsonText | ConvertFrom-Json
    $cmd = $json.tool_input.command
} catch {
    exit 0
}

if ($null -eq $cmd) { exit 0 }

# git commit 以外のコマンドはスルー
if ($cmd -notmatch "git\s+commit") { exit 0 }

# --no-verify はグローバルでもチェックしているが念のため
if ($cmd -match "--no-verify") {
    Write-Error "BLOCKED: --no-verify は禁止されています。"
    exit 2
}

# ソリューションファイルのパスをフック自身の場所から解決する
$slnPath = Join-Path $PSScriptRoot "..\..\reloaded-helper.slnx"
if (-not (Test-Path $slnPath)) {
    Write-Error "ソリューションファイルが見つかりません: $slnPath"
    exit 2
}

# ビルドチェック
Write-Host "コミット前ビルドチェック中..."
$buildOutput = dotnet build "$slnPath" 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Error "ビルドエラーが検出されました。コミットをブロックします。`n$($buildOutput | Out-String)"
    exit 2
}

# テストチェック
Write-Host "コミット前テスト実行中..."
$testOutput = dotnet test "$slnPath" 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Error "テスト失敗が検出されました。コミットをブロックします。`n$($testOutput | Out-String)"
    exit 2
}
Write-Host "ビルド・テスト成功。コミットを許可します。"
exit 0
