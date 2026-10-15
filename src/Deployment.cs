using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;

namespace SdeSuporte;

public record NetworkHost(string Address,string Host,bool WinRmHttps,bool WinRmKerberos,bool Smb,bool AgentPort);
public static class NetworkInventory
{
    public static async Task<bool> Port(IPAddress address,int port,CancellationToken cancellation)
    {
        using var socket=new TcpClient();using var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancellation);timeout.CancelAfter(600);
        try{await socket.ConnectAsync(address,port,timeout.Token);return true;}catch(Exception)when(!cancellation.IsCancellationRequested){return false;}
    }
    public static async Task<NetworkHost?> Probe(IPAddress address,CancellationToken cancellation)
    {
        async Task<bool> PingHost()
        {
            try{using var ping=new Ping();return (await ping.SendPingAsync(address,600).WaitAsync(cancellation)).Status==IPStatus.Success;}
            catch(Exception)when(!cancellation.IsCancellationRequested){return false;}
        }
        var ping=PingHost();var ports=new[]{5986,5985,445,7443}.Select(p=>Port(address,p,cancellation)).ToArray();
        var opened=await Task.WhenAll(ports);if(!await ping&&!opened.Any(x=>x))return null;
        string host=address.ToString();
        try
        {
            var entry=await Dns.GetHostEntryAsync(address).WaitAsync(TimeSpan.FromSeconds(1),cancellation);
            // Only suggest a name that resolves back to the address actually scanned.
            var forward=await Dns.GetHostAddressesAsync(entry.HostName,cancellation).WaitAsync(TimeSpan.FromSeconds(1),cancellation);
            if(forward.Contains(address))host=entry.HostName;
        }catch(Exception)when(!cancellation.IsCancellationRequested){}
        return new(address.ToString(),host,opened[0],opened[1],opened[2],opened[3]);
    }
    public static async Task Scan(IEnumerable<IPv4Range> ranges,Action<NetworkHost> found,IProgress<ScanProgress>? progress,CancellationToken cancellation)
    {
        var addresses=ranges.SelectMany(r=>r.Addresses()).Distinct().ToArray();
        if(addresses.Length==0||addresses.Length>65536)throw new ArgumentException("Selecione de 1 a 65.536 IPs privados.");
        int completed=0,discovered=0;progress?.Report(new(0,addresses.Length,0));
        await Parallel.ForEachAsync(addresses,new ParallelOptions{MaxDegreeOfParallelism=32,CancellationToken=cancellation},async(ip,token)=>{
            var host=await Probe(ip,token);if(host is not null){Interlocked.Increment(ref discovered);found(host);}
            int count=Interlocked.Increment(ref completed);if(count%16==0||count==addresses.Length)progress?.Report(new(count,addresses.Length,Volatile.Read(ref discovered)));
        });
    }
}

public record DeploymentTarget(string Host,string Address);
public record DeploymentResult(string Host,string Address,string State,string Detail);

sealed class DeploymentPanel:UserControl
{
    readonly DataGridView grid=new(){Dock=DockStyle.Fill,AllowUserToAddRows=false,AllowUserToDeleteRows=false,RowHeadersVisible=false,AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill,SelectionMode=DataGridViewSelectionMode.FullRowSelect};
    readonly TextBox networks=new(){Width=300};
    readonly ComboBox mode=new(){Width=220,DropDownStyle=ComboBoxStyle.DropDownList};
    readonly Label status=new(){Dock=DockStyle.Bottom,Height=32,Text="Abra esta aba para procurar máquinas, mesmo sem o Cliente instalado."};
    readonly Button install=new(){Text="Instalar Cliente nas selecionadas",AutoSize=true};
    CancellationTokenSource? scan;
    bool started,deploying;
    public DeploymentPanel()
    {
        Dock=DockStyle.Fill;Padding=new Padding(8);
        var help=new Label{Dock=DockStyle.Top,Height=100,Text="LOCALIZAR MÁQUINAS E IMPLANTAR O CLIENTE\nA busca usa ping e portas do Windows, sem exigir agente. Portas abertas não comprovam que a máquina é Windows.\nPara instalar: selecione os destinos e autentique com uma conta de administrador. Exige WinRM configurado.\nHTTPS funciona sem domínio, com certificado válido. Kerberos usa nomes e conta do domínio. A senha não é gravada.\nA busca automática pesquisa até /22 por interface; edite a faixa para pesquisar outras redes internas."};
        var bar=new FlowLayoutPanel{Dock=DockStyle.Top,Height=85,AutoScroll=true};
        networks.Text=string.Join("; ",IPv4Range.LocalCidrs(true));
        var find=new Button{Text="Localizar máquinas",AutoSize=true};find.Click+=async(_,_)=>await Scan();
        var stop=new Button{Text="Parar busca",AutoSize=true};stop.Click+=(_,_)=>scan?.Cancel();
        var select=new Button{Text="Selecionar com WinRM",AutoSize=true};select.Click+=(_,_)=>{foreach(DataGridViewRow row in grid.Rows)row.Cells[0].Value=mode.SelectedIndex==0?((NetworkHost)row.Tag!).WinRmHttps:((NetworkHost)row.Tag!).WinRmKerberos;};
        mode.Items.AddRange(["HTTPS (sem exigir domínio)","Domínio (Kerberos)"]);mode.SelectedIndex=0;
        bar.Controls.AddRange([networks,find,stop,mode,select,install]);
        grid.Columns.Add(new DataGridViewCheckBoxColumn{Name="Selecionar",FillWeight=35});
        grid.Columns.Add(new DataGridViewTextBoxColumn{Name="IP",ReadOnly=true,FillWeight=65});
        grid.Columns.Add(new DataGridViewTextBoxColumn{Name="Nome para autenticar",FillWeight=110});
        grid.Columns.Add(new DataGridViewTextBoxColumn{Name="Gerenciamento detectado",ReadOnly=true,FillWeight=120});
        grid.Columns.Add(new DataGridViewTextBoxColumn{Name="Resultado",ReadOnly=true,FillWeight=160});
        grid.CurrentCellDirtyStateChanged+=(_,_)=>{if(grid.IsCurrentCellDirty)grid.CommitEdit(DataGridViewDataErrorContexts.Commit);};
        install.Click+=async(_,_)=>await Deploy();
        Controls.Add(grid);Controls.Add(bar);Controls.Add(help);Controls.Add(status);
        Enter+=async(_,_)=>{if(!started){started=true;await Scan();}};
        Disposed+=(_,_)=>scan?.Cancel();
    }
    async Task Scan()
    {
        if(scan is not null||deploying)return;
        try
        {
            var ranges=networks.Text.Split(';',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries).Select(s=>IPv4Range.Parse(s)).ToArray();
            if(ranges.Sum(r=>r.Count)>65536)throw new ArgumentException("Pesquise até 65.536 IPs por vez.");
            using var cancellation=new CancellationTokenSource();scan=cancellation;grid.Rows.Clear();install.Enabled=false;
            var progress=new Progress<ScanProgress>(p=>{if(!IsDisposed)status.Text=$"Localizando: {p.Checked}/{p.Total} IPs • {p.Found} máquina(s) encontrada(s)";});
            await NetworkInventory.Scan(ranges,host=>{
                if(IsDisposed)return;
                try{BeginInvoke(new Action(()=>{if(IsDisposed)return;int row=grid.Rows.Add(false,host.Address,host.Host,
                    host.WinRmHttps?"WinRM HTTPS (5986)":host.WinRmKerberos?"WinRM domínio (5985)":host.Smb?"SMB (445); configure WinRM":"Respondeu; WinRM não detectado",
                    host.AgentPort?"Porta 7443 aberta; confirme na lista de agentes":"Cliente não confirmado");grid.Rows[row].Tag=host;}));}catch(InvalidOperationException){}
            },progress,cancellation.Token);
            if(!IsDisposed)status.Text=$"Busca concluída: {grid.Rows.Count} máquina(s). Confira os nomes e selecione os destinos.";
        }catch(OperationCanceledException){if(!IsDisposed)status.Text="Busca interrompida.";}
        catch(Exception e){if(!IsDisposed)MessageBox.Show(this,e.Message,"Localizar máquinas");}
        finally{scan=null;if(!IsDisposed)install.Enabled=true;}
    }
    async Task Deploy()
    {
        if(deploying||scan is not null)return;
        try
        {
            grid.EndEdit();var rows=grid.Rows.Cast<DataGridViewRow>().Where(r=>r.Cells[0].Value is true).ToArray();
            if(rows.Length==0||rows.Length>512)throw new ArgumentException("Selecione entre 1 e 512 máquinas.");
            var targets=rows.Select(r=>new DeploymentTarget(Convert.ToString(r.Cells[2].Value)?.Trim()??"",Convert.ToString(r.Cells[1].Value)??"")).ToArray();
            foreach(var target in targets)
            {
                if(Uri.CheckHostName(target.Host)==UriHostNameType.Unknown||target.Host.Contains(':'))throw new ArgumentException("Informe um nome de computador ou IPv4 válido para autenticar.");
                IPv4Range.Parse(target.Address);
                if(mode.SelectedIndex==1&&IPAddress.TryParse(target.Host,out _))throw new ArgumentException("Kerberos exige nome DNS do computador. Edite a coluna Nome para autenticar, ou use HTTPS.");
            }
            string root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,".."));
            string installer=Path.Combine(root,"Packages","SDE-Suporte-Cliente-Instalar.exe"),script=Path.Combine(root,"scripts","Deploy-Agent.ps1");
            string expected=File.ReadAllText(installer+".sha256").Trim();
            if(expected.Length!=64||!expected.All(Uri.IsHexDigit)||Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(installer)))!=expected.ToUpperInvariant())throw new InvalidDataException("O pacote Cliente não passou na verificação. Reinstale o Técnico atualizado.");
            string names=string.Join("\n",targets.Take(15).Select(t=>$"{t.Host} ({t.Address})"));
            if(MessageBox.Show(this,$"Instalar/atualizar o Cliente em {targets.Length} máquina(s)?\n\n{names}\n\nSerá solicitada uma conta de administrador. A conexão precisa estar autorizada no WinRM. Máquinas sem usuário conectado ficarão disponíveis no próximo login.","Confirmar implantação",MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes)return;
            deploying=true;install.Enabled=false;grid.Enabled=false;mode.Enabled=false;
            string folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"SdeSuporte","deployments",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
            string manifest=Path.Combine(folder,"request.json"),result=Path.Combine(folder,"results.json");
            Json.Write(manifest,new{targets,mode=mode.SelectedIndex==0?"Https":"Kerberos",installer,sha256=expected,resultPath=result});
            var start=new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"WindowsPowerShell","v1.0","powershell.exe")){UseShellExecute=true};
            foreach(string argument in new[]{"-NoProfile","-ExecutionPolicy","Bypass","-File",script,"-ManifestPath",manifest})start.ArgumentList.Add(argument);
            status.Text="Implantação em andamento. Autentique na janela do PowerShell; não feche essa janela.";
            using var process=Process.Start(start)??throw new IOException("Não foi possível abrir a implantação.");await process.WaitForExitAsync();
            if(IsDisposed)return;
            if(!File.Exists(result))throw new IOException("A implantação não gerou resultado. Verifique a janela do PowerShell e as permissões de administrador.");
            var results=Json.Read<DeploymentResult[]>(result);int successful=0;
            foreach(var row in rows)
            {
                string address=Convert.ToString(row.Cells[1].Value)??"";var item=results.FirstOrDefault(r=>r.Address==address);
                string label=item?.State switch{"InstalledAgentStarted"=>"Instalado; agente iniciado","InstalledWaitingForLogon"=>"Instalado; aguardando login","Failed"=>"Falha",_=>"Não executado"};
                row.Cells[4].Value=item is null?label:label+": "+item.Detail;
                if(item?.State.StartsWith("Installed",StringComparison.Ordinal)==true)successful++;
            }
            status.Text=$"Implantação concluída: {successful}/{targets.Length} instaladas. Atualize a lista de agentes. Relatório: {result}";
        }catch(Exception e){if(!IsDisposed)MessageBox.Show(this,e.Message,"Instalar Cliente");}
        finally{deploying=false;if(!IsDisposed){install.Enabled=true;grid.Enabled=true;mode.Enabled=true;}}
    }
}
