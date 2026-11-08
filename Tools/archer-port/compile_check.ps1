param(
    [string]$Project = "D:\zuoye\tuanjie\TuanjieRoguelike2D-2",
    [string]$Csproj = "Assembly-CSharp.csproj",
    [string]$OutDll = "",
    [string]$ExtraRef = "",
    [string]$SkipRefName = ""
)

$ErrorActionPreference = "Stop"
Set-Location $Project

$csc = "D:\zuoye\tuanjie\2022.3.62t16\Editor\Data\MonoBleedingEdge\lib\mono\msbuild\Current\bin\Roslyn\csc.exe"
if (-not (Test-Path $csc)) { Write-Output "csc not found: $csc"; exit 2 }

[xml]$xml = Get-Content $Csproj
$ns = New-Object System.Xml.XmlNamespaceManager($xml.NameTable)
$ns.AddNamespace("m", "http://schemas.microsoft.com/developer/msbuild/2003")

$defines = ""
foreach ($g in $xml.SelectNodes("//m:PropertyGroup", $ns)) {
    $d = $g.SelectSingleNode("m:DefineConstants", $ns)
    if ($d -and $d.InnerText.Trim().Length -gt 0) { $defines = $d.InnerText.Trim(); break }
}

if (-not $OutDll) { $OutDll = Join-Path $Project "Temp\_check\$([System.IO.Path]::GetFileNameWithoutExtension($Csproj)).dll" }
New-Item -ItemType Directory -Force -Path (Split-Path $OutDll) | Out-Null

$refs = New-Object System.Collections.Generic.List[string]
foreach ($r in $xml.SelectNodes("//m:Reference", $ns)) {
    $name = $r.Include
    if ($SkipRefName -and $name -eq $SkipRefName) { continue }
    $hint = $r.SelectSingleNode("m:HintPath", $ns)
    if ($hint) {
        $p = $hint.InnerText.Trim()
        if (-not [System.IO.Path]::IsPathRooted($p)) { $p = Join-Path $Project $p }
        if (Test-Path $p) { $refs.Add($p) }
    }
}
if ($ExtraRef) { foreach ($e in $ExtraRef.Split(';')) { if ($e.Trim()) { $refs.Add($e.Trim()) } } }

$sources = New-Object System.Collections.Generic.List[string]
$seen = New-Object System.Collections.Generic.HashSet[string]
foreach ($c in $xml.SelectNodes("//m:Compile", $ns)) {
    $p = $c.Include
    if (-not $p) { continue }
    if (-not [System.IO.Path]::IsPathRooted($p)) { $p = Join-Path $Project $p }
    if (-not (Test-Path $p)) { Write-Output "MISSING SOURCE: $p" }
    if ($seen.Add([System.IO.Path]::GetFullPath($p))) { $sources.Add($p) }
}

# 新加的脚本还没被 Unity 写进 csproj（编辑器没跑过），这里自动补全，免得检查漏文件
$scriptRoot = Join-Path $Project "Assets"
$isEditorProject = $Csproj -like "*Editor*"
foreach ($f in Get-ChildItem $scriptRoot -Recurse -Filter *.cs) {
    $full = [System.IO.Path]::GetFullPath($f.FullName)
    if (-not $seen.Contains($full)) {
        $isEditorFile = $full -match "\\Editor\\" -or $full -match "\\Editor$"
        if ($isEditorProject -eq $isEditorFile) {
            if ($seen.Add($full)) { $sources.Add($full); Write-Output "ADDED SOURCE: $($f.FullName)" }
        }
    }
}

$rsp = Join-Path (Split-Path $OutDll) "csc.rsp"
$lines = New-Object System.Collections.Generic.List[string]
$lines.Add("-target:library")
$lines.Add("-langversion:9.0")
$lines.Add("-nostdlib+")
$lines.Add("-noconfig")
$lines.Add("-nowarn:0169,0414,0649,0162")
$lines.Add("-out:`"$OutDll`"")
if ($defines) { $lines.Add("-define:$defines") }
foreach ($r in $refs) { $lines.Add("-r:`"$r`"") }
foreach ($s in $sources) { $lines.Add("`"$s`"") }
Set-Content -Path $rsp -Value $lines -Encoding UTF8

Write-Output "sources=$($sources.Count) refs=$($refs.Count)"
$mono = "D:\zuoye\tuanjie\2022.3.62t16\Editor\Data\MonoBleedingEdge\bin\mono.exe"
& $mono $csc "@$rsp" 2>&1 | Out-String
Write-Output "CSC_EXIT=$LASTEXITCODE"
Write-Output "OUT_EXISTS=$(Test-Path $OutDll)"
