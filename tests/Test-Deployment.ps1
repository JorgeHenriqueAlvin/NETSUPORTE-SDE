$ErrorActionPreference='Stop'
$scriptPath=Join-Path $PSScriptRoot '../scripts/Deploy-Agent-SMB.ps1'
$tokens=$null;$errors=$null
$ast=[Management.Automation.Language.Parser]::ParseFile($scriptPath,[ref]$tokens,[ref]$errors)
if($errors.Count){throw ($errors | Out-String)}
# Load only the pure credential formatter, never run a network deployment here.
$formatter=$ast.Find({param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Normalize-UserForNet'},$true)
if($null -eq $formatter){throw 'Credential formatter not found.'}
. ([scriptblock]::Create($formatter.Extent.Text))
$password=ConvertTo-SecureString 'unused-test-password' -AsPlainText -Force
$cases=@(
 @('.\admin','POSTO01','POSTO01\admin'),
 @('.\admin','POSTO02.sde.local','POSTO02\admin'),
 @('.\admin','10.37.2.10','admin'),
 @('SDE\tecnico','POSTO01','SDE\tecnico'),
 @('POSTO01\admin','10.37.2.10','POSTO01\admin'),
 @('tecnico@sde.local','POSTO01','tecnico@sde.local')
)
foreach($case in $cases){
 $credential=New-Object System.Management.Automation.PSCredential($case[0],$password)
 $actual=Normalize-UserForNet $credential $case[1]
 if($actual -cne $case[2]){throw "Unexpected formatting for $($case[0]) on $($case[1]): $actual"}
}
Write-Host "OK: $($cases.Count) credential cases; deployment script parses. No network changes performed."
