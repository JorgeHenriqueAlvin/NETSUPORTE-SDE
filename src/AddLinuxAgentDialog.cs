namespace SdeSuporte;

sealed class AddLinuxAgentDialog:Form
{
    readonly TextBox address=new(){Dock=DockStyle.Top,PlaceholderText="Exemplo: 10.37.3.163",MaxLength=64};
    readonly TextBox report=new(){Dock=DockStyle.Fill,Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical,Text="Informe o IP e clique em Testar conexão. O teste identifica o nome e verifica a equipe antes de permitir salvar."};
    readonly Button test=new(){Text="Testar conexão",AutoSize=true};
    readonly Button save=new(){Text="Adicionar à lista",AutoSize=true,Enabled=false};
    readonly CancellationTokenSource stop;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Device? SelectedDevice {get;private set;}
    public AddLinuxAgentDialog(string[] roots,TechnicianIdentity identity,CancellationToken lifetime)
    {
        stop=CancellationTokenSource.CreateLinkedTokenSource(lifetime);
        Text="Adicionar Raspberry/Linux pelo IP";ClientSize=new(650,390);MinimumSize=new(560,360);StartPosition=FormStartPosition.CenterParent;
        Font=new Font("Segoe UI",10);Padding=new Padding(16);MinimizeBox=false;MaximizeBox=false;
        var heading=new Label{Dock=DockStyle.Top,Height=70,Text="Adicionar agente Raspberry / Linux\nUse o IP exibido por hostname -I no Raspberry.",Font=new Font("Segoe UI",11)};
        var hint=new Label{Dock=DockStyle.Bottom,Height=55,Text="O agente deve estar iniciado e acessível pela rede. Esta opção não contorna bloqueios de Wi-Fi, roteamento ou firewall.",Padding=new Padding(0,8,0,0)};
        var buttons=new FlowLayoutPanel{Dock=DockStyle.Bottom,Height=46,FlowDirection=FlowDirection.RightToLeft};
        var cancel=new Button{Text="Fechar",DialogResult=DialogResult.Cancel,AutoSize=true};buttons.Controls.AddRange([cancel,save,test]);
        var gap=new Panel{Dock=DockStyle.Top,Height=12};
        Controls.Add(report);Controls.Add(hint);Controls.Add(buttons);Controls.Add(gap);Controls.Add(address);Controls.Add(heading);
        AcceptButton=test;CancelButton=cancel;Premium.Apply(this);
        address.TextChanged+=(_,_)=>{SelectedDevice=null;save.Enabled=false;report.Text="IP alterado. Clique em Testar conexão para verificar este endereço.";};
        test.Click+=async(_,_)=>{
            test.Enabled=false;address.ReadOnly=true;save.Enabled=false;SelectedDevice=null;report.Text="Testando porta, identidade da equipe e autorização do Técnico...";
            try{
                var result=await AgentConnection.Test(address.Text.Trim(),roots,identity,stop.Token);
                if(IsDisposed||stop.IsCancellationRequested)return;
                report.Text=result.Message;SelectedDevice=result.CanSave?result.Device:null;save.Enabled=result.CanSave;
            }catch(OperationCanceledException){}catch(Exception){if(!IsDisposed&&!stop.IsCancellationRequested)report.Text="Não foi possível concluir o teste. Confira o IP e tente novamente.";}
            finally{if(!IsDisposed&&!stop.IsCancellationRequested){test.Enabled=true;address.ReadOnly=false;}}
        };
        save.Click+=(_,_)=>{if(SelectedDevice is not null){DialogResult=DialogResult.OK;Close();}};
        FormClosing+=(_,_)=>stop.Cancel();FormClosed+=(_,_)=>stop.Dispose();
    }
}
