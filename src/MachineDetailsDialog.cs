using System.Text.Json;

namespace SdeSuporte;

sealed class MachineDetailsDialog:Form
{
    readonly TextBox report=new(){Dock=DockStyle.Fill,Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Both,WordWrap=false};
    readonly Button refresh=new(){Text="Atualizar informações",AutoSize=true};
    readonly CancellationTokenSource stop;
    public MachineDetailsDialog(Device device,TechnicianIdentity identity,CancellationToken lifetime)
    {
        stop=CancellationTokenSource.CreateLinkedTokenSource(lifetime);
        Text=$"Configurações — {device.Name}";ClientSize=new(820,560);MinimumSize=new(620,380);StartPosition=FormStartPosition.CenterParent;
        Font=new Font("Segoe UI",10);Padding=new Padding(14);
        var heading=new Label{Dock=DockStyle.Top,Height=65,Text=$"{device.Name}\nIP da conexão: {device.Host} • Setor: {device.Sector}"};
        var buttons=new FlowLayoutPanel{Dock=DockStyle.Bottom,Height=48};
        var close=new Button{Text="Fechar",AutoSize=true,DialogResult=DialogResult.Cancel};buttons.Controls.AddRange([refresh,close]);
        Controls.Add(report);Controls.Add(buttons);Controls.Add(heading);Premium.Apply(this);CancelButton=close;
        async Task LoadDetails()
        {
            refresh.Enabled=false;report.Text="Consultando configurações do agente...";
            var token=stop.Token;
            try
            {
                using var remote=new RemoteClient(device,identity,TimeSpan.FromSeconds(10));
                using var infoResponse=await remote.Request(HttpMethod.Get,"/info").WaitAsync(token);
                using var info=JsonDocument.Parse(await infoResponse.Content.ReadAsStringAsync(token));
                if(!info.RootElement.TryGetProperty("id",out var id)||id.GetString()!=device.Id)throw new InvalidDataException("Identificação divergente.");
                if(!info.RootElement.TryGetProperty("capabilities",out var caps)||!caps.EnumerateArray().Any(c=>c.GetString()=="inventory"))
                {
                    if(!IsDisposed)report.Text=$"Nome cadastrado: {device.Name}\r\nIP: {device.Host}\r\n\r\nEste agente ainda não fornece as configurações. Atualize o Cliente Windows para 0.12.8 ou instale a nova versão do agente Linux.\r\nA atualização apenas do Técnico não habilita o inventário nos agentes antigos.";
                    return;
                }
                using var response=await remote.Request(HttpMethod.Get,"/inventory").WaitAsync(token);
                var fields=JsonSerializer.Deserialize<Dictionary<string,string>>(await response.Content.ReadAsStringAsync(token),Json.Options)??throw new InvalidDataException("Inventário vazio.");
                if(!IsDisposed)report.Text="Origem: agente autenticado; conexão com certificado fixado ao cadastro.\r\n\r\n"+string.Join("\r\n\r\n",fields.Select(p=>$"{p.Key}: {p.Value}"))+"\r\n\r\nCampos indisponíveis neste sistema podem ser omitidos. RAM e espaço livre refletem o momento da consulta.";
            }
            catch(OperationCanceledException){if(!IsDisposed&&!token.IsCancellationRequested)report.Text="A consulta excedeu o tempo de espera. Tente novamente.";}
            catch(Exception e){if(!IsDisposed)report.Text="Não foi possível consultar as configurações: "+e.Message;}
            finally{if(!IsDisposed)refresh.Enabled=true;}
        }
        Shown+=async(_,_)=>await LoadDetails();refresh.Click+=async(_,_)=>await LoadDetails();
        FormClosing+=(_,_)=>stop.Cancel();
        // Keep the source alive until any pending read has observed cancellation.
        FormClosed+=(_,_)=>stop.Dispose();
    }
}
