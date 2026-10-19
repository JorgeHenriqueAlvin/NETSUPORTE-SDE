using System.Collections.Concurrent;
using System.Text.Json;

namespace SdeSuporte;

sealed class FindLinuxAgentsDialog:Form
{
    readonly ListBox results=new(){Dock=DockStyle.Fill,HorizontalScrollbar=true};
    readonly Label status=new(){Dock=DockStyle.Bottom,Height=75,Padding=new Padding(8)};
    readonly Button search=new(){Text="Buscar novamente",AutoSize=true};
    readonly Button cancelSearch=new(){Text="Parar busca",AutoSize=true,Enabled=false};
    readonly Button add=new(){Text="Adicionar selecionado",AutoSize=true,Enabled=false};
    readonly string[] roots;
    readonly TechnicianIdentity identity;
    readonly CancellationToken lifetime;
    CancellationTokenSource? scanning;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Device? SelectedDevice {get;private set;}
    sealed record Match(Device Device,bool Available)
    {
        public override string ToString()=>$"{Device.Name} — {Device.Host} — {(Available?"Desktop disponível":"Desktop indisponível")}";
    }
    public FindLinuxAgentsDialog(string[] roots,TechnicianIdentity identity,CancellationToken lifetime)
    {
        this.roots=roots;this.identity=identity;this.lifetime=lifetime;
        Text="Buscar Raspberry Pi / Linux";ClientSize=new(760,450);MinimumSize=new(600,380);StartPosition=FormStartPosition.CenterParent;
        Font=new Font("Segoe UI",10);Padding=new Padding(12);
        var heading=new Label{Dock=DockStyle.Top,Height=80,Text="Encontre agentes Raspberry Pi / Linux sem informar o IP.\nBusca nas redes locais (até /22 por interface). O agente deve estar iniciado e pertencer à equipe."};
        var buttons=new FlowLayoutPanel{Dock=DockStyle.Bottom,Height=48,AutoScroll=true};
        var close=new Button{Text="Fechar",AutoSize=true,DialogResult=DialogResult.Cancel};
        buttons.Controls.AddRange([search,cancelSearch,add,close]);
        Controls.Add(results);Controls.Add(status);Controls.Add(buttons);Controls.Add(heading);Premium.Apply(this);CancelButton=close;
        results.SelectedIndexChanged+=(_,_)=>add.Enabled=scanning is null&&results.SelectedItem is Match;
        search.Click+=async(_,_)=>await Search();Shown+=async(_,_)=>await Search();
        cancelSearch.Click+=(_,_)=>scanning?.Cancel();
        add.Click+=(_,_)=>{if(scanning is null&&results.SelectedItem is Match match){SelectedDevice=match.Device;DialogResult=DialogResult.OK;Close();}};
        FormClosing+=(_,_)=>scanning?.Cancel();
    }
    async Task Search()
    {
        if(scanning is not null)return;
        using var stop=CancellationTokenSource.CreateLinkedTokenSource(lifetime);scanning=stop;
        var token=stop.Token;search.Enabled=false;cancelSearch.Enabled=true;add.Enabled=false;results.Items.Clear();
        bool Active()=>!IsDisposed&&!Disposing&&!token.IsCancellationRequested;
        try
        {
            var ranges=IPv4Range.LocalCidrs(true).Select(s=>IPv4Range.Parse(s)).ToArray();
            if(ranges.Length==0){status.Text="Nenhuma rede privada ativa encontrada. Confira a conexão de rede do Técnico.";return;}
            var candidates=new ConcurrentDictionary<string,Device>();
            var progress=new Progress<ScanProgress>(p=>{if(Active())status.Text=$"Procurando: {p.Checked}/{p.Total} IPs • {p.Found} agente(s) da equipe. Redes: {string.Join(", ",ranges.Select(r=>r.Label))}";});
            await NetworkScanner.Scan(ranges,roots,identity.Public.Id,d=>candidates.TryAdd(d.Id,d),progress,token);
            if(!Active())return;
            status.Text="Identificando os agentes Linux e verificando a autorização...";
            using var limit=new SemaphoreSlim(8,8);
            await Task.WhenAll(candidates.Values.Select(async device=>{
                await limit.WaitAsync(token);
                try
                {
                    using var client=new RemoteClient(device,identity,TimeSpan.FromSeconds(5));
                    using var response=await client.Request(HttpMethod.Get,"/info").WaitAsync(token);
                    using var info=JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
                    var root=info.RootElement;
                    if(!root.TryGetProperty("id",out var id)||id.GetString()!=device.Id)return;
                    // Existing Linux agents identify their OS in the version string.
                    if(!root.TryGetProperty("version",out var version)||version.ValueKind!=JsonValueKind.String||!version.GetString()!.Contains("linux",StringComparison.OrdinalIgnoreCase))return;
                    bool available=root.TryGetProperty("available",out var ready)&&ready.ValueKind==JsonValueKind.True;
                    if(Active())results.Items.Add(new Match(device,available));
                }
                catch(OperationCanceledException)when(token.IsCancellationRequested){throw;}
                catch(Exception e)when(e is HttpRequestException or JsonException or OperationCanceledException){/* Unavailable agents are not offered for connection. */}
                finally{limit.Release();}
            }));
            if(Active())status.Text=results.Items.Count>0?$"{results.Items.Count} agente(s) Linux encontrado(s). Selecione um para adicionar. A conexão solicitará autorização no cliente.":"Nenhum agente Linux acessível encontrado. Confira se o Raspberry está ligado, com o agente iniciado e na mesma rede. Outras VLANs ou redes maiores podem exigir Varredura de IP.";
        }
        catch(OperationCanceledException){if(!IsDisposed)status.Text=$"Busca interrompida. {results.Items.Count} agente(s) encontrado(s).";}
        catch(Exception){if(!IsDisposed)status.Text="Não foi possível concluir a busca. Confira a rede e tente novamente.";}
        finally
        {
            scanning=null;
            if(!IsDisposed){search.Enabled=true;cancelSearch.Enabled=false;add.Enabled=results.SelectedItem is Match;}
        }
    }
}
