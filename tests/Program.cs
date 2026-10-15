using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SdeSuporte;

static class Tests
{
    static void Check(bool value,string label){if(!value)throw new Exception("FALHOU: "+label);Console.WriteLine("OK: "+label);}
    static async Task<int> Main(string[] args)
    {
        try
        {
            if(args[0]=="prepare")
            {
                Directory.CreateDirectory(args[1]);
                using var id=new TechnicianIdentity(ECDsa.Create(ECCurve.NamedCurves.nistP256),"Técnico de Teste");
                Json.Write(Path.Combine(args[1],"deployment.json"),Enrollment.Create(id,[],"Teste"));
                Json.Write(Path.Combine(args[1],"tecnico.sdetech"),id.Public);File.WriteAllBytes(Path.Combine(args[1],"private-test.pk8"),id.ExportPrivate());return 0;
            }
            if(args[0]=="protocol") {Protocol();EnrollmentTests();return 0;}
            if(args[0]=="template"){InstallerPackage.CreateTemplate(args[1],args[2],args[3]);return 0;}
            if(args[0]=="package"){using var creator=new TechnicianIdentity(ECDsa.Create(ECCurve.NamedCurves.nistP256),"Preparador");InstallerPackage.Personalize(args[1],args[2],Enrollment.Create(creator,[],"Teste"));return 0;}
            await Live(args[1],args[2]);return 0;
        }catch(Exception e){Console.Error.WriteLine(e);return 1;}
    }
    static void Protocol()
    {
        using var id=new TechnicianIdentity(ECDsa.Create(ECCurve.NamedCurves.nistP256),"Jorge Henrique");
        using var other=new TechnicianIdentity(ECDsa.Create(ECCurve.NamedCurves.nistP256),"Outro");
        using(var persistent=CngKey.Create(CngAlgorithm.ECDsaP256,"SDE-Test-"+Guid.NewGuid().ToString("N"),new CngKeyCreationParameters{ExportPolicy=CngExportPolicies.None}))
        {
            try {
                using var signer=new TechnicianIdentity(new ECDsaCng(persistent),"Tecnico CNG");
                var proof=signer.Sign("GET","/info",[],"","1",new string('A',32),"device");
                Check(Convert.FromBase64String(proof).Length==64,"chave persistente nao exportavel assina requisicao");
            }finally{persistent.Delete();}
        }
        var device=Guid.NewGuid().ToString();var v=new RequestVerifier([id.Public],device);
        byte[] body=Encoding.UTF8.GetBytes("comando");string now=DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        string nonce=Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        string sig=id.Sign("POST","/input",body,"session",now,nonce,device);
        Check(v.Verify(id.Public.Id,device,"POST","/input",body,"session",now,nonce,sig)?.Name=="Jorge Henrique","assinatura autentica e nome aprovado");
        Check(v.Verify(id.Public.Id,device,"POST","/input",body,"session",now,nonce,sig) is null,"replay rejeitado");
        Check(v.Verify(other.Public.Id,device,"POST","/input",body,"session",now,nonce,sig) is null,"tecnico nao autorizado bloqueado");
        nonce=Convert.ToHexString(RandomNumberGenerator.GetBytes(16));sig=id.Sign("POST","/input",body,"session",now,nonce,device);
        Check(v.Verify(id.Public.Id,device,"POST","/input",Encoding.UTF8.GetBytes("alterado"),"session",now,nonce,sig) is null,"corpo alterado bloqueado");
        Check(v.Verify(id.Public.Id,device,"POST","/session",body,"session",now,nonce,sig) is null,"rota alterada bloqueada");
        Check(v.Verify(id.Public.Id,device,"POST","/input",body,"outro-token",now,nonce,sig) is null,"token alterado bloqueado");
        Check(v.Verify(id.Public.Id,Guid.NewGuid().ToString(),"POST","/input",body,"session",now,nonce,sig) is null,"requisicao para outro computador bloqueada");
        string old=(DateTimeOffset.UtcNow.ToUnixTimeSeconds()-120).ToString();sig=id.Sign("POST","/input",body,"session",old,nonce,device);
        Check(v.Verify(id.Public.Id,device,"POST","/input",body,"session",old,nonce,sig) is null,"assinatura expirada bloqueada");
        try{_=new RequestVerifier([],device);throw new Exception("politica vazia aceita");}catch(InvalidDataException){Console.WriteLine("OK: politica vazia bloqueada");}
    }
    static void EnrollmentTests()
    {
        using var admin=new TechnicianIdentity(ECDsa.Create(ECCurve.NamedCurves.nistP256),"Preparador");
        var profile=Enrollment.Create(admin,[],"Setor de Teste");
        var device=new Device(Guid.NewGuid().ToString(),"CLIENTE","Setor de Teste","127.0.0.1",7443,new string('A',64));
        var proof=Enrollment.Seal(device,profile);
        Enrollment.VerifyDevice(proof,[admin.Public.Id],admin.Public.Id);Console.WriteLine("OK: cadastro automatico autorizado pela equipe");
        try{Enrollment.VerifyDevice(proof with{Device=device with{CertificateSha256=new string('B',64)}},[admin.Public.Id],admin.Public.Id);throw new Exception("certificado alterado aceito");}catch(InvalidDataException){Console.WriteLine("OK: certificado de cadastro adulterado rejeitado");}
        try{Enrollment.VerifyDevice(proof,[],admin.Public.Id);throw new Exception("autoridade desconhecida aceita");}catch(InvalidDataException){Console.WriteLine("OK: equipe desconhecida nao cadastra maquinas");}
        try{Enrollment.VerifyDevice(proof,[admin.Public.Id],"desconhecido");throw new Exception("tecnico desconhecido aceito");}catch(InvalidDataException){Console.WriteLine("OK: descoberta respeita lista de tecnicos");}
        var modified=profile.Approval with{Permission=profile.Approval.Permission with{Sector="Outro setor"}};
        try{Enrollment.VerifyPermission(modified);throw new Exception("setor alterado aceito");}catch(InvalidDataException){Console.WriteLine("OK: autorizacao de setor adulterada rejeitada");}
    }
    static async Task Live(string fixture,string hostFile)
    {
        var expected=Json.Read<AgentConfig>(hostFile).Device;expected.Validate();
        var metadata=Json.Read<TechnicianKey>(Path.Combine(fixture,"tecnico.sdetech"));
        using var id=TechnicianIdentity.FromPrivate(File.ReadAllBytes(Path.Combine(fixture,"private-test.pk8")),metadata.Name);
        var discovered=new TaskCompletionSource<Device>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var discovery=new ConsoleDiscovery(id,()=>[id.Public.Id],d=>{if(d.Id==expected.Id)discovered.TrySetResult(d);},17445);
        for(int attempt=0;attempt<20&&!discovered.Task.IsCompleted;attempt++){await discovery.SearchTo(new IPEndPoint(IPAddress.Loopback,7446));await Task.Delay(500);}
        var d=await discovered.Task.WaitAsync(TimeSpan.FromSeconds(5));Check(d.Id==expected.Id,"agente aparece por descoberta sem importar arquivo da maquina");
        using var client=new RemoteClient(d,id);
        bool ready=false;
        for(int i=0;i<20;i++){try{using var response=await client.Request(HttpMethod.Get,"/info");ready=true;break;}catch{await Task.Delay(500);}}
        Check(ready,"agente HTTPS responde sem dominio");
        using(var wrongPin=new RemoteClient(d with{CertificateSha256=new string('0',64)},id))
        {try{using var r=await wrongPin.Request(HttpMethod.Get,"/info");throw new Exception("certificado nao conferido");}catch(HttpRequestException){Console.WriteLine("OK: certificado diferente recusado");}}
        using(var stranger=new TechnicianIdentity(ECDsa.Create(ECCurve.NamedCurves.nistP256),"Nao autorizado"))
        using(var denied=new RemoteClient(d,stranger))
        {try{using var r=await denied.Request(HttpMethod.Get,"/info");throw new Exception("nao autorizado aceito");}catch(HttpRequestException e)when(e.StatusCode==HttpStatusCode.Forbidden){Console.WriteLine("OK: tecnico nao autorizado recusado pelo agente real");}}
        var targetReady=new TaskCompletionSource<(Form,TextBox)>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread=new Thread(()=>{
            ApplicationConfiguration.Initialize();
            var form=new Form{Text="SDE Suporte - alvo do teste",Width=600,Height=300,TopMost=true,StartPosition=FormStartPosition.CenterScreen};
            var input=new TextBox{Dock=DockStyle.Fill,Multiline=true};form.Controls.Add(input);
            var surface=new RemoteScreen{Dock=DockStyle.Bottom,Height=30};form.Controls.Add(surface);
            form.Shown+=(_,_)=>{surface.Focus();Check(surface.Focused,"tela remota pode receber teclado");};
            form.Shown+=(_,_)=>targetReady.SetResult((form,input));Application.Run(form);
        });thread.SetApartmentState(ApartmentState.STA);thread.Start();
        var (form,textbox)=await targetReady.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            await client.Start();Check(client.Session?.TechnicianName==metadata.Name,"sessao identifica tecnico aprovado");
            using(var frame=await client.Request(HttpMethod.Get,"/frame"))
            {var bytes=await frame.Content.ReadAsByteArrayAsync();Check(bytes.Length>1000&&bytes[0]==0xff&&bytes[1]==0xd8,"captura real da tela produz JPEG");}
            form.Invoke(()=>{form.Activate();textbox.Focus();SetForegroundWindow(form.Handle);});await Task.Delay(500);
            IntPtr handle=(IntPtr)form.Invoke(()=>form.Handle);
            Check(GetForegroundWindow()==handle,"alvo exclusivo do teste em primeiro plano");
            using(var command=await client.Request(HttpMethod.Post,"/input",new InputCommand("text",Text:"SDE-TEST-123"))){}
            await Task.Delay(500);string value=(string)textbox.Invoke(()=>textbox.Text);
            Check(value=="SDE-TEST-123","digitacao remota chega ao aplicativo de teste");
            var oldSession=client.Session!;await client.Stop();
            Check(client.Session is null,"encerramento da sessao");
            try{using var r=await client.Request(HttpMethod.Get,"/frame");throw new Exception("frame sem sessao");}catch(HttpRequestException e)when(e.StatusCode==HttpStatusCode.Forbidden){Console.WriteLine("OK: tela bloqueada apos encerrar");}
            using(var tokenClient=new HttpClient(new HttpClientHandler{ServerCertificateCustomValidationCallback=(_,cert,_,_)=>cert is not null&&Convert.ToHexString(SHA256.HashData(cert.RawData))==d.CertificateSha256}))
            {
                string time=DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(),nonce=Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
                using var req=new HttpRequestMessage(HttpMethod.Get,$"https://{d.Host}:{d.Port}/frame");
                req.Headers.Add("X-Device",d.Id);req.Headers.Add("X-Technician",id.Public.Id);req.Headers.Add("X-Time",time);req.Headers.Add("X-Nonce",nonce);req.Headers.Add("X-Session",oldSession.Id);
                req.Headers.Add("X-Signature",id.Sign("GET","/frame",[],oldSession.Id,time,nonce,d.Id));
                using var r=await tokenClient.SendAsync(req);Check(r.StatusCode==HttpStatusCode.Forbidden,"token antigo nao reabre sessao");
            }
        }
        finally{try{await client.Stop();}catch{}form.Invoke(()=>form.Close());thread.Join(5000);}
    }
    [DllImport("user32.dll")]static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")]static extern IntPtr GetForegroundWindow();
}
