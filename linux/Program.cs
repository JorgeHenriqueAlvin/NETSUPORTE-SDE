using System.Diagnostics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using SdeSuporte;

if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("Este Cliente requer Linux.");
if (Environment.GetEnvironmentVariable("XDG_SESSION_TYPE") != "x11" || string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY")))
    throw new InvalidOperationException("Entre em uma sessão X11. Wayland ainda não é suportado.");
var state = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SdeSuporte");
Directory.CreateDirectory(state); File.SetUnixFileMode(state, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
var configPath = Path.Combine(state, "agent.json");
var certPath = Path.Combine(state, "agent.pfx");
var profile = TeamAccess.Deployment;
AgentConfig config;
if (!File.Exists(configPath))
{
    using var key = RSA.Create(3072);
    var request = new CertificateRequest("CN=SDE-Raspberry", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
    request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false,false,0,false));
    request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,true));
    using var issued = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1),DateTimeOffset.UtcNow.AddYears(3));
    File.WriteAllBytes(certPath,issued.Export(X509ContentType.Pfx));
    File.SetUnixFileMode(certPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    var host=DiscoveryNetwork.Addresses().FirstOrDefault(x=>System.Net.IPAddress.TryParse(x,out _)) ?? throw new InvalidOperationException("Conecte o Raspberry à rede antes de iniciar.");
    var device=new Device(Guid.NewGuid().ToString(),Environment.MachineName,profile.Approval.Permission.Sector,host,7443,Convert.ToHexString(SHA256.HashData(issued.RawData)));
    config=new(device,profile.Approval.Permission.Technicians,true,Enrollment.Seal(device,profile));
    Json.Write(configPath,config);
}
else config=Json.Read<AgentConfig>(configPath);
config.Device.Validate();
using var certificate=X509CertificateLoader.LoadPkcs12FromFile(certPath,null);
if(Convert.ToHexString(SHA256.HashData(certificate.RawData))!=config.Device.CertificateSha256) throw new InvalidDataException("Certificado diverge do cadastro.");
// DHCP may change the address without changing the persistent identity or certificate.
var address=DiscoveryNetwork.Addresses().FirstOrDefault(x=>System.Net.IPAddress.TryParse(x,out _));
if(address is not null && address!=config.Device.Host)
{
    var device=config.Device with {Host=address};
    config=config with {Device=device,Enrollment=Enrollment.Seal(device,profile)}; Json.Write(configPath,config);
}
if(args.Contains("--check"))
{
    using var check=new Desktop();
    var screens=await check.Call("monitors");
    Console.WriteLine(Encoding.UTF8.GetString(screens)); return;
}
var builder=WebApplication.CreateSlimBuilder(args);
builder.WebHost.ConfigureKestrel(k=>{
    k.Limits.MaxRequestBodySize=4*1024*1024; k.Limits.MaxConcurrentConnections=32;
    k.Limits.RequestHeadersTimeout=TimeSpan.FromSeconds(5);
    k.ListenAnyIP(config.Device.Port,l=>{l.Protocols=HttpProtocols.Http1;l.UseHttps(certificate);});
});
var app=builder.Build();
using var desktop=new Desktop();
using var discovery=new AgentDiscovery(config);
var verifier=new RequestVerifier(config.Technicians,config.Device.Id,config.Enrollment!.Approval.Permission.TeamWide?config.Enrollment.Approval.Permission.Issuer:null);
using var gate=new SemaphoreSlim(1);
RemoteSession? session=null;
DateTimeOffset activity=DateTimeOffset.UtcNow;
DateTimeOffset idleSince=DateTimeOffset.UtcNow;
var history=new List<ChatMessage>(); long sequence=0;
var held=new Dictionary<string,InputCommand>();
var notices=new Dictionary<string,DateTimeOffset>();
var lastNotice=new Dictionary<string,DateTimeOffset>();
void Audit(string action,string technician,string detail="")=>OperationalData.Write(Path.Combine(state,"audit.jsonl"),new {id=Guid.NewGuid().ToString("N"),at=DateTimeOffset.UtcNow,action,technician,machine=config.Device.Name,deviceId=config.Device.Id,detail});
ChatMessage Message(string name,string text)
{
    if(string.IsNullOrWhiteSpace(text)||text.Length>2000) throw new ArgumentException("Mensagem inválida.");
    var m=new ChatMessage(++sequence,name,text.Trim(),DateTimeOffset.UtcNow);history.Add(m);if(history.Count>200)history.RemoveAt(0);return m;
}
async Task End(string reason)
{
    var previous=session;session=null;if(previous is not null)idleSince=DateTimeOffset.UtcNow;
    foreach(var input in held.Values)try{await desktop.Call("input",input:input with {Kind=input.Kind=="down"?"up":"keyUp",Layout=""});}catch{}
    held.Clear(); history.Clear();
    if(previous is not null){Audit(reason,previous.TechnicianName);desktop.Send(new{kind="session",name=""});}
}
async Task<bool> Available()
{
    // Fail closed if logind cannot confirm that this is an active, unlocked desktop.
    var sid=Environment.GetEnvironmentVariable("XDG_SESSION_ID");
    if(string.IsNullOrEmpty(sid)||!desktop.Alive)return false;
    try{
        var data=Encoding.UTF8.GetString(await Desktop.Execute("loginctl",["show-session",sid,"-p","Active","-p","LockedHint"]));
        return data.Split('\n').Contains("Active=yes")&&data.Split('\n').Contains("LockedHint=no");
    }catch{return false;}
}
desktop.EmployeeText=async text=>{
    await gate.WaitAsync();try{if(session is not null&&await Available()){
        var m=Message(Environment.UserName,text);Audit("chat_employee",session.TechnicianName);desktop.Send(new{kind="message",name=m.Sender,text=m.Text});
    }}catch{}finally{gate.Release();}
};
using var shutdown=new CancellationTokenSource();
var watch=Task.Run(async()=>{
    using var timer=new PeriodicTimer(TimeSpan.FromSeconds(1));
    try{while(await timer.WaitForNextTickAsync(shutdown.Token)){
        if(!await gate.WaitAsync(0))continue;
        try{if(session is not null&&(session.Expires<=DateTimeOffset.UtcNow||DateTimeOffset.UtcNow-activity>TimeSpan.FromSeconds(20)||!await Available()))await End("expired_or_desktop_unavailable");
        if(session is null&&DateTimeOffset.UtcNow-idleSince>TimeSpan.FromSeconds(60)&&File.Exists(Path.Combine(state,"UpdateCache","pending.json"))){Environment.ExitCode=75;app.Lifetime.StopApplication();break;}
        }finally{gate.Release();}
    }}catch(OperationCanceledException){}
});
app.Run(async context=>{
    context.Response.Headers.CacheControl="no-store";
    if(!await gate.WaitAsync(0)){context.Response.StatusCode=429;return;}
    string user="não autenticado";
    try{
        var req=context.Request;string path=req.Path.Value??"/";
        async Task Reply(int status,object payload){context.Response.StatusCode=status;await context.Response.WriteAsJsonAsync(payload,Json.Options);}
        if(req.Method=="GET"&&path=="/discover"&&!req.QueryString.HasValue){await Reply(200,new DiscoveryMessage("SDE-DISCOVERY-1",config.Enrollment!));return;}
        int limit=path=="/announcement"?4*1024*1024:path=="/chat"?16384:8192;
        if(req.ContentLength is null && (req.Method=="POST"||req.Headers.ContainsKey("Transfer-Encoding")) || req.ContentLength>limit){await Reply(413,new{error="Tamanho inválido."});return;}
        byte[] body=new byte[(int)(req.ContentLength??0)];
        using(var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(5)))await req.Body.ReadExactlyAsync(body,timeout.Token);
        string H(string name)=>req.Headers[name].ToString();
        var tech=verifier.Verify(H("X-Technician"),H("X-Device"),req.Method,path+req.QueryString,body,H("X-Session"),H("X-Time"),H("X-Nonce"),H("X-Signature"),H("X-Team-Credential"));
        if(tech is null){await Reply(403,new{error="Técnico não autorizado."});return;}user=tech.Name;
        if(path=="/info"&&req.Method=="GET"){await Reply(200,new{config.Device.Id,config.Device.Name,config.Device.Sector,hostname=Environment.MachineName,description=MachineInventory.Description(),os=MachineInventory.OperatingSystemName(),osVersion=Environment.OSVersion.VersionString,version=typeof(MachineInventory).Assembly.GetName().Version?.ToString(3),capabilities=new[]{"chat","announcements","announcement-images","inventory","operations"},available=await Available()});return;}
        if(path=="/health"&&req.Method=="GET"){await Reply(200,new{deviceId=config.Device.Id,hostname=Environment.MachineName,version=typeof(MachineInventory).Assembly.GetName().Version?.ToString(3),activeSession=session is not null,desktopAvailable=await Available(),at=DateTimeOffset.UtcNow,update=OperationalData.UpdateStatus(state)});return;}
        if(path=="/audit"&&req.Method=="GET"){await Reply(200,OperationalData.Tail(Path.Combine(state,"audit.jsonl")));return;}
        if(path=="/inventory"&&req.Method=="GET"){var inventory=MachineInventory.Collect();Audit("inventory_read",user);await Reply(200,inventory);return;}
        if(path=="/announcement"&&req.Method=="POST"){
            var notice=JsonSerializer.Deserialize<Notice>(body,Json.Options)??throw new ArgumentException("Aviso inválido.");
            if(!Guid.TryParseExact(notice.Id,"D",out _)||notice.Text.Length>2000||string.IsNullOrWhiteSpace(notice.Text)&&notice.ImageBase64 is null)throw new ArgumentException("Aviso inválido.");
            if(notice.ImageBase64 is not null)await desktop.Call("validate-image",input:new{imageBase64=notice.ImageBase64});
            string id=tech.Id+notice.Id;var now=DateTimeOffset.UtcNow;
            foreach(var expired in notices.Where(x=>now-x.Value>TimeSpan.FromHours(1)).Select(x=>x.Key).ToArray())notices.Remove(expired);
            if(notices.ContainsKey(id)){await Reply(200,new{received=true,duplicate=true});return;}
            if(lastNotice.TryGetValue(tech.Id,out var last)&&now-last<TimeSpan.FromSeconds(3)){await Reply(429,new{error="Aguarde três segundos."});return;}
            if(notices.Count>=512)notices.Remove(notices.MinBy(x=>x.Value).Key);
            desktop.Send(new{kind="notice",name=user,text=notice.Text,imageBase64=notice.ImageBase64});notices[id]=now;lastNotice[tech.Id]=now;Audit("announcement_received",user,notice.Id);
            await Reply(200,new{received=true,duplicate=false});return;
        }
        if(path=="/session"&&req.Method=="POST"){
            if(session is not null){await Reply(409,new{error="Já existe atendimento."});return;}
            if(!await Available()){await Reply(409,new{error="Área de trabalho bloqueada ou indisponível."});return;}
            Audit("access_requested",user);
            if(Encoding.UTF8.GetString(await desktop.Call("consent",user)).Trim()!="yes"){
                Audit("consent_declined_or_timeout",user);await Reply(403,new{error="Acesso não autorizado pelo funcionário."});return;
            }
            if(!await Available()){await Reply(409,new{error="Área de trabalho indisponível."});return;}
            session=new(Convert.ToHexString(RandomNumberGenerator.GetBytes(32)),tech.Id,user,DateTimeOffset.UtcNow,DateTimeOffset.UtcNow.AddHours(1));
            activity=DateTimeOffset.UtcNow;desktop.Send(new{kind="session",name=user});Audit("started",user);await Reply(200,session);return;
        }
        if(session is null||session.TechnicianId!=tech.Id||session.Id!=H("X-Session")||session.Expires<=DateTimeOffset.UtcNow){await Reply(403,new{error="Sessão encerrada."});return;}
        if(!await Available()){await End("desktop_unavailable");await Reply(409,new{error="Área de trabalho indisponível."});return;}
        activity=DateTimeOffset.UtcNow;
        if(path=="/session/end"&&req.Method=="POST"){await End("technician_stop");await Reply(200,new{ok=true});return;}
        if(path=="/chat"&&req.Method=="GET"){
            if(!long.TryParse(req.Query["after"].FirstOrDefault()??"0",out var after)||after<0)throw new ArgumentException("Cursor inválido.");
            await Reply(200,new {messages=history.Where(x=>x.Sequence>after).ToArray()});return;
        }
        if(path=="/chat"&&req.Method=="POST"){
            var value=JsonSerializer.Deserialize<TextMessage>(body,Json.Options)??throw new ArgumentException("Mensagem inválida.");
            var m=Message(user,value.Text);desktop.Send(new{kind="message",name=user,text=m.Text});Audit("chat_technician",user);await Reply(200,m);return;
        }
        if(path=="/frame"&&req.Method=="GET"){
            var screens=JsonSerializer.Deserialize<RemoteMonitor[]>(await desktop.Call("monitors"),Json.Options)!;
            var monitor=req.Query["monitor"].FirstOrDefault()??"all";
            var selected=monitor=="all"?null:monitor=="primary"?screens.FirstOrDefault(m=>m.Primary)??screens.First():screens.FirstOrDefault(m=>m.Id==monitor)??throw new ArgumentException("Monitor inexistente.");
            monitor=selected?.Id??"all";
            // Python layout bytes are the source of truth for stale-layout input checks.
            context.Response.Headers["X-SDE-Monitors"]=Encoding.UTF8.GetString(await desktop.Call("layout")).Trim();
            context.Response.Headers["X-SDE-Monitor"]=Uri.EscapeDataString(monitor);
            context.Response.Headers["X-SDE-Offset-X"]=((selected?.X??screens.Min(m=>m.X))-screens.Min(m=>m.X)).ToString();
            context.Response.Headers["X-SDE-Offset-Y"]=((selected?.Y??screens.Min(m=>m.Y))-screens.Min(m=>m.Y)).ToString();
            byte[] frame=await desktop.Call("frame",monitor);context.Response.ContentType="image/jpeg";await context.Response.Body.WriteAsync(frame);return;
        }
        if(path=="/input"&&req.Method=="POST"){
            var input=JsonSerializer.Deserialize<InputCommand>(body,Json.Options)??throw new ArgumentException("Entrada inválida.");
            await desktop.Call("input",input:input);
            string key=(input.Kind is "down" or "up"?"mouse":"key")+input.Key;
            if(input.Kind is "down" or "keyDown")held[key]=input;
            if(input.Kind is "up" or "keyUp")held.Remove(key);
            await Reply(200,new{ok=true});return;
        }
        await Reply(404,new{error="Operação inexistente."});
    }catch(Exception e){Audit("error",user,e.GetType().Name);if(!context.Response.HasStarted){context.Response.StatusCode=e is ArgumentException?400:500;await context.Response.WriteAsJsonAsync(new{error="Não foi possível concluir a operação. Consulte o log local."});}}
    finally{gate.Release();}
});
try{await app.RunAsync();}finally{shutdown.Cancel();await watch;await End("agent_closed");}

record ChatMessage(long Sequence,string Sender,string Text,DateTimeOffset At);
record TextMessage(string Text);
record Notice(string Id,string Text,string? ImageBase64=null);
sealed class Desktop:IDisposable
{
    readonly Process ui;
    public Func<string,Task>? EmployeeText;
    public bool Alive=>!ui.HasExited;
    public Desktop(){ui=Start("python3",[Path.Combine(AppContext.BaseDirectory,"desktop.py"),"ui"]);_=Read();}
    async Task Read(){while(await ui.StandardOutput.ReadLineAsync() is {} line)try{var text=JsonSerializer.Deserialize<TextMessage>(line,Json.Options);if(text is not null&&EmployeeText is not null)await EmployeeText(text.Text);}catch{}}
    public void Send(object value){if(!Alive)throw new IOException("Interface encerrada.");ui.StandardInput.WriteLine(JsonSerializer.Serialize(value));ui.StandardInput.Flush();}
    static Process Start(string file,IEnumerable<string> args){var start=new ProcessStartInfo(file){UseShellExecute=false,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true};foreach(var arg in args)start.ArgumentList.Add(arg);return Process.Start(start)??throw new IOException("Falha ao iniciar componente.");}
    public static async Task<byte[]> Execute(string file,IEnumerable<string> args,object? input=null,int seconds=8){
        using var p=Start(file,args);using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(seconds));
        var errors=p.StandardError.ReadToEndAsync();using var output=new MemoryStream();var copy=p.StandardOutput.BaseStream.CopyToAsync(output,timeout.Token);
        try{if(input is not null)await p.StandardInput.WriteAsync(JsonSerializer.Serialize(input,Json.Options));p.StandardInput.Close();await p.WaitForExitAsync(timeout.Token);await copy;string error=await errors;if(p.ExitCode!=0)throw new ArgumentException("Componente desktop recusou a operação: "+error);return output.ToArray();}
        finally{if(!p.HasExited)p.Kill(true);}
    }
    public Task<byte[]> Call(string command,string? argument=null,object? input=null)=>Execute("python3",new[]{Path.Combine(AppContext.BaseDirectory,"desktop.py"),command}.Concat(argument is null?[]:new[]{argument}),input,command=="consent"?35:8);
    public void Dispose(){if(!ui.HasExited){try{Send(new{kind="quit"});if(!ui.WaitForExit(1500))ui.Kill(true);}catch{if(!ui.HasExited)ui.Kill(true);}}ui.Dispose();}
}
