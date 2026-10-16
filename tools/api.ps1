param([string[]]$Types, [string]$Filter = '')
# Lists members of tModLoader types (signatures), e.g. -Types Terraria.NPC -Filter downed
$T = if ($env:TML_DIR) { $env:TML_DIR } else { 'D:\Jeux\Jeux Steam\steamapps\common\tModLoader' }
$psRoslyn = Join-Path $PSHOME 'Microsoft.CodeAnalysis.CSharp.dll'
Add-Type -Path (Join-Path $PSHOME 'Microsoft.CodeAnalysis.dll'); Add-Type -Path $psRoslyn
$fw = Get-ChildItem (Join-Path $T 'dotnet/shared/Microsoft.NETCore.App') -Directory | Sort-Object Name | Select-Object -Last 1
$refs = New-Object 'System.Collections.Generic.List[Microsoft.CodeAnalysis.MetadataReference]'
foreach ($f in @(Get-ChildItem $fw.FullName -Filter *.dll) + @(Get-Item (Join-Path $T 'tModLoader.dll')) + @(Get-ChildItem (Join-Path $T 'Libraries') -Recurse -Filter *.dll | Where-Object { $_.Name -in @('FNA.dll', 'ReLogic.dll', 'MonoMod.RuntimeDetour.dll', 'MonoMod.Utils.dll', 'Mono.Cecil.dll', 'TerrariaHooks.dll') })) {
  try { [void][Reflection.AssemblyName]::GetAssemblyName($f.FullName); $refs.Add([Microsoft.CodeAnalysis.MetadataReference]::CreateFromFile($f.FullName)) } catch {}
}
$comp = [Microsoft.CodeAnalysis.CSharp.CSharpCompilation]::Create('Probe', $null, $refs)
$fmt = [Microsoft.CodeAnalysis.SymbolDisplayFormat]::CSharpShortErrorMessageFormat
foreach ($tn in ($Types -split ",")) {
  $t = $comp.GetTypeByMetadataName($tn)
  if (-not $t) { "## $tn : NOT FOUND"; continue }
  "## $tn ($($t.TypeKind)) base=$($t.BaseType)"
  $gm = [Microsoft.CodeAnalysis.INamespaceOrTypeSymbol].GetMethod('GetMembers', [Type[]]@())
  foreach ($m in $gm.Invoke($t, @())) {
    if ($m.DeclaredAccessibility -ne 'Public' -and $m.DeclaredAccessibility -ne 'Protected' -and $m.DeclaredAccessibility -ne 'ProtectedOrInternal') { continue }
    $s = [Microsoft.CodeAnalysis.ISymbol].GetMethod('ToDisplayString').Invoke($m, @($fmt))
    if ($m.Kind -eq 'Field') { $s = "$([Microsoft.CodeAnalysis.ISymbol].GetMethod('ToDisplayString').Invoke($m.Type, @($fmt))) $s" + $(if ($m.IsStatic) { ' [static]' } else { '' }) }
    if ($m.Kind -eq 'Property') { $s = "$([Microsoft.CodeAnalysis.ISymbol].GetMethod('ToDisplayString').Invoke($m.Type, @($fmt))) $s" + $(if ($m.IsStatic) { ' [static]' } else { '' }) }
    if ($m.Kind -eq 'Method') { $s = "$([Microsoft.CodeAnalysis.ISymbol].GetMethod('ToDisplayString').Invoke($m.ReturnType, @($fmt))) $s" + $(if ($m.IsStatic) { ' [static]' } elseif ($m.IsVirtual -or $m.IsAbstract) { ' [virtual]' } else { '' }) }
    if ($Filter -eq '' -or $s -match $Filter) { "  $s" }
  }
}
