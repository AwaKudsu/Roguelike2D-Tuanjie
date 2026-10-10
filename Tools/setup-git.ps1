# ============================================================
#  Roguelike2D（团结引擎版）- 开发环境一键配置脚本
#
#  用途：在新机器上克隆仓库后，一条命令配好 Git / LFS / 场景合并驱动。
#        脚本是幂等的，可重复执行。
#
#  用法（必须在工程根目录，用 -ExecutionPolicy Bypass 绕过执行策略限制）：
#      powershell -ExecutionPolicy Bypass -File Tools\setup-git.ps1
#
#  注意：本文件必须保存为「UTF-8 带 BOM」。
#        Windows PowerShell 5.1 在没有 BOM 时会按系统 ANSI(GBK) 解码，
#        中文会变成乱码并导致语法错误。
# ============================================================

# 不使用 Stop：git 这类原生命令会往 stderr 写正常信息，Stop 会误判为错误
$ErrorActionPreference = 'Continue'

function Write-Step  { param($m) Write-Host $m -ForegroundColor Cyan }
function Write-Ok    { param($m) Write-Host "      [OK]   $m" -ForegroundColor Green }
function Write-Warn2 { param($m) Write-Host "      [警告] $m" -ForegroundColor Yellow }
function Write-Bad   { param($m) Write-Host "      [错误] $m" -ForegroundColor Red }

Write-Host ""
Write-Step "=== Roguelike2D（团结引擎版）开发环境配置 ==="
Write-Host ""

# ---------- 1. 定位工程根目录 ----------
$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not (Test-Path (Join-Path $repoRoot 'ProjectSettings\ProjectVersion.txt'))) {
    Write-Bad "找不到 ProjectSettings\ProjectVersion.txt，请在团结工程根目录下运行本脚本"
    exit 1
}
Write-Step "[1/5] 工程根目录：$repoRoot"

# ---------- 2. 读取引擎版本，定位 YAML 合并工具 ----------
# 本工程用团结引擎，ProjectVersion.txt 里的 m_TuanjieEditorVersion 是团结自有的版本号。
$versionFile   = Join-Path $repoRoot 'ProjectSettings\ProjectVersion.txt'
$versionLine   = Select-String -Path $versionFile -Pattern '^m_EditorVersion:\s*(.+)$'
$engineVersion = $versionLine.Matches[0].Groups[1].Value.Trim()
$tuanjieLine   = Select-String -Path $versionFile -Pattern '^m_TuanjieEditorVersion:\s*(.+)$'
$tuanjieVer    = if ($tuanjieLine) { $tuanjieLine.Matches[0].Groups[1].Value.Trim() } else { '' }
Write-Step "[2/5] 工程锁定的引擎版本：$engineVersion$(if ($tuanjieVer) { "（Tuanjie Editor $tuanjieVer）" })"
Write-Host "      团结 Hub 的编辑器装在 D:\Tuanjie\Editor\<版本>\Editor\ ，不在 Unity Hub 的目录下。"

# 团结 2022.3.62t16 的 Editor\Data\Tools 下【没有】UnityYAMLMerge.exe，
# 因此这里按「团结目录 → Unity Hub 目录」的顺序找第一个存在的工具。
$candidates = @(
    "D:\Tuanjie\Editor\$engineVersion\Editor\Data\Tools\UnityYAMLMerge.exe",
    "$env:ProgramFiles\Tuanjie\Hub\Editor\$engineVersion\Editor\Data\Tools\UnityYAMLMerge.exe",
    "$env:ProgramFiles\Unity\Hub\Editor\$engineVersion\Editor\Data\Tools\UnityYAMLMerge.exe"
)
$mergeExe = $null
foreach ($c in $candidates) {
    if (Test-Path $c) { $mergeExe = $c; break }
}
if (-not $mergeExe) {
    # 本机装了任何一版 Unity，就能白嫖它的 YAMLMerge（工具本身与引擎版本无关）
    $any = Get-ChildItem "$env:ProgramFiles\Unity\Hub\Editor\*\Editor\Data\Tools\UnityYAMLMerge.exe" -ErrorAction SilentlyContinue |
           Sort-Object FullName -Descending | Select-Object -First 1
    if ($any) { $mergeExe = $any.FullName }
}
if (-not $mergeExe) {
    Write-Warn2 "未找到 YAML 合并工具（UnityYAMLMerge.exe）"
    Write-Warn2 "场景 / 预制体的合并冲突将无法自动处理"
} else {
    Write-Ok "合并工具：$mergeExe"
}

# ---------- 3. Git 基础配置 ----------
Write-Step "[3/5] 配置 Git 基础项..."
$gitSettings = @{
    'init.defaultBranch' = 'main'
    'core.autocrlf'      = 'false'   # 换行符交给 .gitattributes，避免整文件 diff
    'core.safecrlf'      = 'false'
    'pull.rebase'        = 'true'    # 拉取用变基，历史保持直线
    'fetch.prune'        = 'true'
    'push.default'       = 'simple'
    'credential.helper'  = 'manager'
    'core.quotepath'     = 'false'   # 中文文件名不转义成 \xxx
    'commit.template'    = (Join-Path $repoRoot '.gitmessage')
}
foreach ($k in $gitSettings.Keys) {
    git config --global $k $gitSettings[$k] 2>&1 | Out-Null
}
Write-Ok "已应用 $($gitSettings.Count) 项 Git 配置"

# ---------- 4. YAML 合并驱动 ----------
# 注意：这里写进【本仓库】的 .git/config，不改全局设置。
# 路径含空格时 git 会存成断开的 "C:/Program"，所以统一转成 8.3 短路径。
Write-Step "[4/5] 注册场景/预制体合并驱动（merge.unityyamlmerge）..."
if ($mergeExe) {
    $shortExe = $mergeExe
    try {
        $fso = New-Object -ComObject Scripting.FileSystemObject
        $shortExe = $fso.GetFile($mergeExe).ShortPath
    } catch {
        Write-Warn2 "取短路径失败，改用原始路径（含空格时可能失效）"
    }
    $driver = ('"' + ($shortExe -replace '\\', '/') + '" merge -p "%O" "%B" "%A" "%A"')
    git config --local merge.unityyamlmerge.name      "Unity SmartMerge" 2>&1 | Out-Null
    git config --local merge.unityyamlmerge.driver    $driver            2>&1 | Out-Null
    git config --local merge.unityyamlmerge.recursive binary             2>&1 | Out-Null
    Write-Ok "已注册（.gitattributes 中的 *.unity / *.prefab / *.asset 将走此工具）"
    Write-Host "      实际写入：$(git config --local --get merge.unityyamlmerge.driver)"
}

# ---------- 5. Git LFS ----------
Write-Step "[5/5] 配置 Git LFS..."
if (-not (Get-Command git-lfs -ErrorAction SilentlyContinue)) {
    Write-Warn2 "未安装 git-lfs。.gitattributes 里的 filter=lfs 规则需要它，"
    Write-Warn2 "否则 git add 会报 \"filter 'lfs' failed\"。安装：winget install GitHub.GitLFS"
} else {
    git lfs install --local 2>&1 | Out-Null
    $trackedInAttrs = (Select-String -Path (Join-Path $repoRoot '.gitattributes') -Pattern 'filter=lfs').Count
    Write-Ok "git-lfs 已启用（$(git lfs version)）"
    Write-Ok ".gitattributes 中已有 $trackedInAttrs 条 LFS 规则，无需重新 track"
}

# ---------- 自检 ----------
Write-Host ""
Write-Step "=== 自检 ==="
Write-Host ""
Push-Location $repoRoot

Write-Host ("Git 身份   : {0} [{1}]" -f (git config user.name), (git config user.email))

$drv = git config --local --get merge.unityyamlmerge.driver
Write-Host ("合并驱动   : {0}" -f $(if ($drv) { '已配置（本仓库）' } else { '未配置' }))

if (Get-Command git-lfs -ErrorAction SilentlyContinue) {
    Write-Host ("Git LFS    : {0}" -f (git lfs version))
} else {
    Write-Host "Git LFS    : 未安装"
}

Write-Host ("远端       : {0}" -f ((git remote -v | Select-Object -First 1) -replace '\s+', ' '))

Write-Host ""
Write-Host "忽略规则检查（用 git check-ignore --no-index 判断，不依赖文件是否真实存在）："
foreach ($d in @('Library/', 'Temp/', 'Logs/', 'UserSettings/', 'Build/', 'obj/', '.codely-cli/', '_Tuanjie/install-1.10.4.json')) {
    git check-ignore --no-index -q $d 2>&1 | Out-Null
    if ($LASTEXITCODE -eq 0) { Write-Ok "$d 已忽略" }
    else { Write-Bad "$d 未被忽略，请检查 .gitignore" }
}
foreach ($d in @('ProjectSettings/', 'Assets/', 'Packages/', 'docs/')) {
    git check-ignore --no-index -q $d 2>&1 | Out-Null
    if ($LASTEXITCODE -eq 0) { Write-Bad "$d 被忽略了！它必须提交" }
    else { Write-Ok "$d 会被提交" }
}

Pop-Location
Write-Host ""
Write-Step "配置完成。"
Write-Host ""
