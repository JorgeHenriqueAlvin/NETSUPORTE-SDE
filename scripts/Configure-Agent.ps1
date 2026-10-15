#Requires -RunAsAdministrator
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
[System.Windows.Forms.Application]::EnableVisualStyles()
$form=New-Object System.Windows.Forms.Form
$form.Text='SDE Suporte - configurar cliente na rede local'
$form.Size=New-Object System.Drawing.Size(660,440)
$form.StartPosition='CenterScreen'
$form.FormBorderStyle='FixedDialog'
$form.MaximizeBox=$false
function LabelAt([string]$Text,[int]$Y) {
 $label=New-Object System.Windows.Forms.Label
 $label.Text=$Text; $label.Location=New-Object System.Drawing.Point(20,$Y); $label.Size=New-Object System.Drawing.Size(600,25); $form.Controls.Add($label)
}
LabelAt 'Nao precisa de dominio. O tecnico deve exportar sua identificacao no console.' 15
LabelAt 'Setor deste computador:' 55
$sector=New-Object System.Windows.Forms.TextBox; $sector.Location=New-Object System.Drawing.Point(20,82);$sector.Width=600;$form.Controls.Add($sector)
LabelAt 'Identificacoes dos tecnicos autorizados (.sdetech):' 118
$files=New-Object System.Windows.Forms.TextBox;$files.Location=New-Object System.Drawing.Point(20,145);$files.Width=470;$files.ReadOnly=$true;$form.Controls.Add($files)
$select=New-Object System.Windows.Forms.Button;$select.Text='Selecionar';$select.Location=New-Object System.Drawing.Point(500,143);$select.Width=120;$form.Controls.Add($select)
$script:technicianFiles=@()
$select.Add_Click({$d=New-Object System.Windows.Forms.OpenFileDialog;$d.Filter='Identificacao do tecnico (*.sdetech)|*.sdetech';$d.Multiselect=$true;if($d.ShowDialog() -eq 'OK'){$script:technicianFiles=@($d.FileNames);$files.Text=($d.FileNames -join '; ')};$d.Dispose()})
LabelAt 'Redes dos tecnicos: LocalSubnet ou sub-redes separadas por virgula' 185
$networks=New-Object System.Windows.Forms.TextBox;$networks.Location=New-Object System.Drawing.Point(20,212);$networks.Width=600;$networks.Text='LocalSubnet';$form.Controls.Add($networks)
$consent=New-Object System.Windows.Forms.CheckBox;$consent.Text='Pedir confirmacao do funcionario a cada atendimento';$consent.Location=New-Object System.Drawing.Point(20,250);$consent.Width=600;$form.Controls.Add($consent)
$configure=New-Object System.Windows.Forms.Button;$configure.Text='Configurar e exportar computador';$configure.Location=New-Object System.Drawing.Point(20,300);$configure.Size=New-Object System.Drawing.Size(600,45);$form.Controls.Add($configure)
$configure.Add_Click({
 try {
  if (-not $sector.Text.Trim() -or $script:technicianFiles.Count -eq 0) { throw 'Informe setor e selecione pelo menos uma identificacao de tecnico.' }
  $save=New-Object System.Windows.Forms.SaveFileDialog;$save.Filter='Computador SDE (*.sdehost)|*.sdehost';$save.FileName="$env:COMPUTERNAME.sdehost"
  if ($save.ShowDialog() -ne 'OK') { $save.Dispose();return }
  $export=$save.FileName;$save.Dispose();$configure.Enabled=$false
  & "$PSScriptRoot\Install-Agent.ps1" -Sector $sector.Text.Trim() -TechnicianFiles $script:technicianFiles -ExportPath $export -TechnicianNetworks @($networks.Text.Split(',') | ForEach-Object { $_.Trim() } | Where-Object { $_ }) -RequireConsent $consent.Checked
  [System.Windows.Forms.MessageBox]::Show("Configurado. Envie o arquivo .sdehost ao tecnico para importar no console.`n`nSaia e entre no Windows para iniciar o agente sem elevacao.",'SDE Suporte') | Out-Null
  $form.Close()
 } catch { [System.Windows.Forms.MessageBox]::Show($_.Exception.Message,'Configuracao nao concluida') | Out-Null; $configure.Enabled=$true }
})
[void]$form.ShowDialog();$form.Dispose()
