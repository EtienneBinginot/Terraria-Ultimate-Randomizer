param([string]$Src = (Split-Path $PSScriptRoot -Parent))
# Type-checks a tModLoader mod source folder with Roslyn, against the installed tModLoader assemblies
$T = if ($env:TML_DIR) { $env:TML_DIR } else { 'D:\Jeux\Jeux Steam\steamapps\common\tModLoader' }
$lib = Join-Path $T 'Libraries'
$roslyn = [AppDomain]::CurrentDomain.GetAssemblies() | Where-Object { $_.GetName().Name -eq 'Microsoft.CodeAnalysis.CSharp' }
if (-not $roslyn) {
  $psRoslyn = Join-Path $PSHOME 'Microsoft.CodeAnalysis.CSharp.dll'
  if (Test-Path $psRoslyn) { Add-Type -Path (Join-Path $PSHOME 'Microsoft.CodeAnalysis.dll'); Add-Type -Path $psRoslyn }
}
$fw = Get-ChildItem (Join-Path $T 'dotnet/shared/Microsoft.NETCore.App') -Directory | Sort-Object Name | Select-Object -Last 1
$refs = @()
$refs += Get-ChildItem $fw.FullName -Filter *.dll
$refs += Get-Item (Join-Path $T 'tModLoader.dll')
$refs += Get-ChildItem $lib -Recurse -Filter *.dll | Where-Object {
  $_.Name -notlike '*.resources.dll' -and $_.FullName -notlike '*Native*' -and $_.FullName -notlike '*codeanalysis*' -and $_.FullName -notlike '*microsoft.build*' -and $_.FullName -notlike '*basic.reference*' -and $_.Name -notlike '*CodeAssist*' -and $_.Name -notlike 'tModPorter*'
}
$metaRefs = New-Object 'System.Collections.Generic.List[Microsoft.CodeAnalysis.MetadataReference]'
$seen = @{}
foreach ($r in $refs) {
  if ($seen[$r.Name]) { continue }
  try {
    [void][Reflection.AssemblyName]::GetAssemblyName($r.FullName)
    $metaRefs.Add([Microsoft.CodeAnalysis.MetadataReference]::CreateFromFile($r.FullName))
    $seen[$r.Name] = 1
  } catch {}
}
$opts = [Microsoft.CodeAnalysis.CSharp.CSharpParseOptions]::Default.WithLanguageVersion([Microsoft.CodeAnalysis.CSharp.LanguageVersion]::Latest)
$trees = New-Object 'System.Collections.Generic.List[Microsoft.CodeAnalysis.SyntaxTree]'
$sep = [IO.Path]::DirectorySeparatorChar
Get-ChildItem $Src -Recurse -Filter *.cs | Where-Object {
  $_.FullName -notlike ('*' + $sep + 'obj' + $sep + '*') -and $_.FullName -notlike ('*' + $sep + 'tools' + $sep + '*') -and $_.FullName -notlike ('*' + $sep + 'bin' + $sep + '*')
} | ForEach-Object {
  $trees.Add([Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText([IO.File]::ReadAllText($_.FullName), $opts, $_.FullName))
}
$copts = New-Object Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions([Microsoft.CodeAnalysis.OutputKind]::DynamicallyLinkedLibrary)
$copts = $copts.WithNullableContextOptions([Microsoft.CodeAnalysis.NullableContextOptions]::Disable).WithAllowUnsafe($true)
$comp = [Microsoft.CodeAnalysis.CSharp.CSharpCompilation]::Create('Check', $trees, $metaRefs, $copts)
$ms = New-Object IO.MemoryStream
$res = $comp.Emit($ms)
$diags = $res.Diagnostics | Where-Object { $_.Severity -ge [Microsoft.CodeAnalysis.DiagnosticSeverity]::Warning -and $_.Id -notin @('CS1701', 'CS1702', 'CS8019') }
foreach ($d in $diags) { $d.ToString() }
"RESULT: success=$($res.Success) errors=$(@($diags | Where-Object Severity -eq 'Error').Count) warnings=$(@($diags | Where-Object Severity -eq 'Warning').Count) files=$($trees.Count) refs=$($metaRefs.Count)"
