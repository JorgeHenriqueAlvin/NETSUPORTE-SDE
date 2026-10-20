using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SdeSuporte;

sealed class OperationsPanel:UserControl
{
    readonly DataGridView grid=new(){Dock=DockStyle.Fill,ReadOnly=true,AllowUserToAddRows=false,AllowUserToDeleteRows=false,AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill,SelectionMode=DataGridViewSelectionMode.FullRowSelect,MultiSelect=false};
    readonly Label status=new(){Dock=DockStyle.Bottom,Height=48};
    readonly System.Windows.Forms.Timer timer=new(){Interval=60000};
    readonly CancellationTokenSource stop;
    readonly TechnicianIdentity identity;
    readonly Func<Device[]> devices;
    readonly Func<Device,bool> online;
    readonly Func<string[]> roots;
    readonly string folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"SdeSuporte","operations");
    readonly Dictionary<string,HashSet<string>> seen=new();
    bool busy;
    public OperationsPanel(TechnicianIdentity identity,Func<Device[]> devices,Func<Device,bool> online,Func<string[]> roots,CancellationToken lifetime)
    {
        this.identity=identity;this.devices=devices;this.online=online;this.roots=roots;
        stop=CancellationTokenSource.CreateLinkedTokenSource(lifetime);
        Directory.CreateDirectory(folder);
        var bar=new FlowLayoutPanel{Dock=DockStyle.Top,Height=80,AutoScroll=true};
        var refresh=new Button{Text="Atualizar estados e logs",AutoSize=true};
        var diagnose=new Button{Text="Diagnosticar selecionada",AutoSize=true};
        var logs=new Button{Text="Ver eventos recebidos",AutoSize=true};
        var auto=new CheckBox{Text="Coletar a cada minuto",Checked=true,AutoSize=true};
        bar.Controls.AddRange([refresh,diagnose,logs,auto]);
        grid.Columns.Add("machine","Computador");grid.Columns.Add("ip","IP");grid.Columns.Add("version","Agente");
        grid.Columns.Add("update","Atualização");grid.Columns.Add("session","Atendimento");grid.Columns.Add("event","Último evento / alerta");grid.Columns.Add("at","Consulta");
        Controls.Add(grid);Controls.Add(bar);Controls.Add(status);
        status.Text="Logs de suporte recebidos dos agentes acessíveis. Não registra tudo que o funcionário faz. Agentes antigos precisam ser atualizados.";
        refresh.Click+=async(_,_)=>await RefreshAll();
        timer.Tick+=async(_,_)=>{if(auto.Checked&&Visible)await RefreshAll();};timer.Start();
        VisibleChanged+=async(_,_)=>{if(Visible)await RefreshAll();};
        diagnose.Click+=async(_,_)=>{
            if(grid.CurrentRow?.Tag is not Device d)return;
            try{
                var result=await AgentConnection.Test(d.Host,roots(),identity,stop.Token,d.Port);
                if(!IsDisposed)MessageBox.Show(this,result.Message,"Diagnóstico: "+result.State);
            }catch(OperationCanceledException){}
            catch(Exception e){if(!IsDisposed)MessageBox.Show(this,e.Message,"Diagnóstico");}
        };
        logs.Click+=(_,_)=>{
            if(grid.CurrentRow?.Tag is not Device d)return;
            var entries=OperationalData.Tail(EventPath(d),200);
            using var dialog=new Form{Text="Eventos recebidos — "+d.Name,Width=960,Height=600,StartPosition=FormStartPosition.CenterParent};
            dialog.Controls.Add(new TextBox{Dock=DockStyle.Fill,ReadOnly=true,Multiline=true,ScrollBars=ScrollBars.Both,WordWrap=false,Text=string.Join(Environment.NewLine,entries.Select(x=>x.GetRawText()))});
            dialog.ShowDialog(this);
        };
    }
    string EventPath(Device d)=>Path.Combine(folder,Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(d.Id)))+".jsonl");
    static string Field(JsonElement data,string key)=>data.TryGetProperty(key,out var value)?value.ToString():"";
    static string UpdateLabel(string value)=>value switch{
        "Current"=>"Atualizado","Downloading"=>"Baixando","Prepared"=>"Preparado","WaitingForIdle"=>"Aguardando ociosidade",
        "Deferred"=>"Adiado: aplicativo em uso","Installing"=>"Instalando","Installed"=>"Instalação confirmada",
        "Failed"=>"Falha na atualização",_=>"Sem informação"};
    async Task RefreshAll()
    {
        if(busy||IsDisposed||stop.IsCancellationRequested)return;
        busy=true;
        try
        {
            var targets=devices();var token=stop.Token;
            // UI rows are created on the UI thread. Parallel continuations return
            // to that context; neither WinForms controls nor key signing run on workers.
            var rows=new Dictionary<string,DataGridViewRow>();
            foreach(DataGridViewRow row in grid.Rows)if(row.Tag is Device d)rows[d.Id]=row;
            foreach(var d in targets)
            {
                if(!rows.TryGetValue(d.Id,out var row)){int i=grid.Rows.Add(d.Name,d.Host,"","","","","");row=grid.Rows[i];rows[d.Id]=row;}
                row.Tag=d;row.Cells[0].Value=d.Name;row.Cells[1].Value=d.Host;
                if(!online(d)){row.Cells[5].Value="Sem comunicação recente; use Diagnosticar";continue;}
            }
            using var gate=new SemaphoreSlim(8);
            int ok=0,failed=0;
            await Task.WhenAll(targets.Where(online).Select(async d=>{
                await gate.WaitAsync(token);
                try
                {
                    using var remote=new RemoteClient(d,identity,TimeSpan.FromSeconds(5));
                    using var infoResponse=await remote.Request(HttpMethod.Get,"/info").WaitAsync(token);
                    using var info=JsonDocument.Parse(await infoResponse.Content.ReadAsStringAsync(token));
                    if(Field(info.RootElement,"id")!=d.Id)throw new InvalidDataException("IP associado a outra identidade.");
                    var row=rows[d.Id];if(IsDisposed||token.IsCancellationRequested)return;
                    row.Cells[2].Value=Field(info.RootElement,"version");
                    bool supported=info.RootElement.TryGetProperty("capabilities",out var caps)&&caps.EnumerateArray().Any(x=>x.GetString()=="operations");
                    if(!supported){row.Cells[5].Value="Atualize o Cliente para receber estado e logs.";return;}
                    using var healthResponse=await remote.Request(HttpMethod.Get,"/health").WaitAsync(token);
                    using var health=JsonDocument.Parse(await healthResponse.Content.ReadAsStringAsync(token));
                    using var auditResponse=await remote.Request(HttpMethod.Get,"/audit").WaitAsync(token);
                    var events=JsonSerializer.Deserialize<JsonElement[]>(await auditResponse.Content.ReadAsStringAsync(token))??[];
                    if(IsDisposed||token.IsCancellationRequested)return;
                    row.Cells[4].Value=health.RootElement.GetProperty("activeSession").GetBoolean()?"Em atendimento":"Livre";
                    if(health.RootElement.TryGetProperty("update",out var update)&&update.ValueKind==JsonValueKind.Object)
                    {
                        row.Cells[3].Value=UpdateLabel(Field(update,"state"))+" • "+Field(update,"targetVersion");
                        row.Cells[3].ToolTipText=Field(update,"detail")+" | Registro: "+Field(update,"at");
                        row.Cells[3].Style.ForeColor=Field(update,"state")=="Failed"?Color.Firebrick:Color.Black;
                    }
                    else row.Cells[3].Value="Estado ainda não recebido";
                    var path=EventPath(d);
                    if(!seen.TryGetValue(d.Id,out var ids)){ids=OperationalData.Tail(path,1000).Where(x=>x.TryGetProperty("eventId",out _)).Select(x=>x.GetProperty("eventId").GetString()!).ToHashSet();seen[d.Id]=ids;}
                    foreach(var entry in events.TakeLast(200))
                    {
                        string eventId=OperationalData.EventId(entry);if(!ids.Add(eventId))continue;
                        OperationalData.Write(path,new{eventId,receivedAt=DateTimeOffset.UtcNow,deviceId=d.Id,hostname=d.Name,eventData=entry});
                    }
                    // Rebuild from retained entries instead of dropping the dedup set
                    // arbitrarily and duplicating the agent's entire tail on every poll.
                    if(ids.Count>2000)seen[d.Id]=OperationalData.Tail(path,1000).Select(x=>x.GetProperty("eventId").GetString()!).ToHashSet();
                    if(events.LastOrDefault() is var last&&last.ValueKind==JsonValueKind.Object)row.Cells[5].Value=Field(last,"action")+" • "+Field(last,"technician");
                    row.Cells[6].Value=DateTimeOffset.Now.ToString("g");ok++;
                }
                catch(OperationCanceledException)when(token.IsCancellationRequested){}
                catch(Exception e){failed++;if(!IsDisposed&&!token.IsCancellationRequested){rows[d.Id].Cells[5].Value="Consulta falhou: "+e.Message;rows[d.Id].Cells[6].Value=DateTimeOffset.Now.ToString("g");}}
                finally{gate.Release();}
            }));
            if(!IsDisposed)status.Text=$"Consultas confirmadas: {ok} • Falhas: {failed} • Máquinas salvas: {targets.Length}. Logs locais recebidos; até 200 eventos recentes por consulta. Registros anteriores podem não ter sido coletados.";
        }
        catch(OperationCanceledException){}
        catch(Exception e){if(!IsDisposed)status.Text="Falha na coleta: "+e.Message;}
        finally{busy=false;}
    }
    protected override void Dispose(bool disposing)
    {
        if(disposing){stop.Cancel();timer.Dispose();}
        base.Dispose(disposing);
    }
}
