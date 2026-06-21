# PostToolUse Hook: .cs ファイル編集後に dotnet format を自動実行する
$jsonText = [System.Console]::In.ReadToEnd()
try {
    $json = $jsonText | ConvertFrom-Json
    $file = $json.tool_input.file_path
} catch {
    exit 0
}

if ($null -eq $file) { exit 0 }

# .cs ファイルのみ処理
if ($file -notlike "*.cs") { exit 0 }

# dotnet format を実行（エラーは無視してフォーマットできた分だけ適用）
dotnet format "reloaded-helper.slnx" 2>&1 | Out-Null

exit 0
