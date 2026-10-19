using System.Diagnostics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using SdeSuporte;

if(!OperatingSystem.IsLinux())throw new PlatformNotSupportedException();
var directory=Path.Combine(Path.GetTempPath(),"sde-linux-test-"+Guid.NewGuid());Directory.CreateDirectory(directory);
var state=Path.Combine(directory,"SdeSuporte");Directory.CreateDirectory(state);
var bin=Path.Combine(directory,"bin");Directory.CreateDirectory(bin);
var python=Path.GetFullPath("tests-linux/fake-desktop.py");
File.WriteAllText(Path.Combine(bin,"python3"),"#!/bin/sh\nexec /usr/bin/python3 '"+python+"' \"$@\"\n");
File.WriteAllText(Path.Combine(bin,"loginctl"),"#!/bin/sh\nprintf 'Active=yes\\nLockedHint=no\\n'\n");
foreach(var file in Directory.GetFiles(bin))File.SetUnixFileMode(file,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute);
using var issuer=new TechnicianIdentity(ECDsa.Create(ECCurve.NamedCurves.nistP256),"Test authority");
using var tech=new TechnicianIdentity(ECDsa.Create(ECCurve.NamedCurves.nistP256),"Test technician");
tech.Credential=TeamAccess.Issue(issuer,tech.Public);
var profile=Enrollment.Create(issuer,[],"Test",true);
using var key=RSA.Create(2048);
var request=new CertificateRequest("CN=Test",key,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1);
using var cert=request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1),DateTimeOffset.UtcNow.AddDays(1));
File.WriteAllBytes(Path.Combine(state,"agent.pfx"),cert.Export(X509ContentType.Pfx));
var device=new Device(Guid.NewGuid().ToString(),"Test Pi","Test","127.0.0.1",17443,Convert.ToHexString(SHA256.HashData(cert.RawData)));
// Match host selection so startup does not re-enroll using the production delegation.
var host=DiscoveryNetwork.Addresses().FirstOrDefault(x=>System.Net.IPAddress.TryParse(x,out _))??System.Net.Dns.GetHostName();device=device with {Host=host};
Json.Write(Path.Combine(state,"agent.json"),new AgentConfig(device,[issuer.Public],true,Enrollment.Seal(device,profile)));
var start=new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false,RedirectStandardError=true,RedirectStandardOutput=true};
// When launched via dotnet run, Environment.ProcessPath is the test apphost.
start.FileName=Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")??"dotnet";
start.ArgumentList.Add(Path.GetFullPath("linux/bin/Release/net10.0/SDE-Suporte-Cliente-Linux.dll"));
start.Environment["XDG_DATA_HOME"]=directory;start.Environment["XDG_SESSION_TYPE"]="x11";start.Environment["DISPLAY"]=":99";start.Environment["XDG_SESSION_ID"]="test";
start.Environment["PATH"]=bin+":"+Environment.GetEnvironmentVariable("PATH");
using var agent=Process.Start(start)!;var stdout=agent.StandardOutput.ReadToEndAsync();var stderr=agent.StandardError.ReadToEndAsync();
try{
    // Manual add validates a single private IP, never a range or arbitrary URL.
    foreach(var invalid in new[]{"https://10.37.3.163", "10.37.3.0/24", "10.37.3.1-10.37.3.2", "8.8.8.8", "127.0.0.1"}){
        try{AgentConnection.ParseAddress(invalid);throw new Exception("Invalid manual address accepted: "+invalid);}catch(ArgumentException){}
    }
    var message=new DiscoveryMessage("SDE-DISCOVERY-1",Enrollment.Seal(device,profile));
    if(AgentConnection.ValidateDiscovery(message,device.CertificateSha256,device.Port,[issuer.Public.Id],tech.Public.Id).Name!="Test Pi")throw new Exception("Manual discovery lost device name.");
    try{AgentConnection.ValidateDiscovery(message,new string('0',64),device.Port,[issuer.Public.Id],tech.Public.Id);throw new Exception("Wrong certificate accepted.");}catch(System.Security.Authentication.AuthenticationException){}
    try{AgentConnection.ValidateDiscovery(message,device.CertificateSha256,device.Port,["untrusted"],tech.Public.Id);throw new Exception("Wrong team accepted.");}catch(InvalidDataException){}
    var renamed=device with{Name="Altered"};
    try{AgentConnection.ValidateDiscovery(message with{Enrollment=message.Enrollment with{Device=renamed}},device.CertificateSha256,device.Port,[issuer.Public.Id],tech.Public.Id);throw new Exception("Altered device accepted.");}catch(InvalidDataException){}
    using var cancelled=new CancellationTokenSource();cancelled.Cancel();
    try{await AgentConnection.Test("10.37.3.163",[issuer.Public.Id],tech,cancelled.Token);throw new Exception("Cancellation ignored.");}catch(OperationCanceledException){}
    using var client=new RemoteClient(device with {Host="127.0.0.1"},tech,TimeSpan.FromSeconds(5));
    bool ready=false;Exception? lastError=null;
    for(int i=0;i<40;i++){
        try{using var info=await client.Request(HttpMethod.Get,"/info");ready=true;break;}catch(Exception e){lastError=e;if(agent.HasExited)throw new Exception("Agent stopped: "+await stderr);await Task.Delay(250);}
    }
    if(!ready)throw new Exception("Server unavailable.",lastError);
    using var stranger=new TechnicianIdentity(ECDsa.Create(ECCurve.NamedCurves.nistP256),"Unknown");
    using var forbidden=new RemoteClient(device with {Host="127.0.0.1"},stranger);
    using(var inventory=await client.Request(HttpMethod.Get,"/inventory"))
    {
        var fields=System.Text.Json.JsonSerializer.Deserialize<Dictionary<string,string>>(await inventory.Content.ReadAsStringAsync(),Json.Options)!;
        if(fields["Nome da máquina / host"]!=Environment.MachineName||!fields.ContainsKey("RAM total")||!fields.ContainsKey("Sistema operacional"))throw new Exception("Linux inventory missing real host, OS or RAM.");
    }
    try{using var deniedInventory=await forbidden.Request(HttpMethod.Get,"/inventory");throw new Exception("Unauthorized inventory accepted.");}catch(HttpRequestException e)when(e.StatusCode==System.Net.HttpStatusCode.Forbidden){}
    try{using var info=await forbidden.Request(HttpMethod.Get,"/info");throw new Exception("Unauthorized identity accepted.");}catch(HttpRequestException e)when(e.StatusCode==System.Net.HttpStatusCode.Forbidden){}
    if(System.Net.IPAddress.TryParse(device.Host,out var reachable)){
        var manual=await AgentConnection.Test(device.Host,[issuer.Public.Id],tech,port:device.Port);
        if(!manual.CanSave||manual.Device?.Name!="Test Pi"||manual.Device.Id!=device.Id)throw new Exception("Manual add failed: "+manual.Message);
        var denied=await AgentConnection.Test(device.Host,[issuer.Public.Id],stranger,port:device.Port);
        if(denied.CanSave||denied.State!=AgentConnectionState.Unauthorized)throw new Exception("Manual add accepted an unauthorized technician.");
        var wrongTeam=await AgentConnection.Test(device.Host,["untrusted"],tech,port:device.Port);
        if(wrongTeam.CanSave||wrongTeam.State!=AgentConnectionState.UntrustedAgent)throw new Exception("Manual add accepted another team.");
        Console.WriteLine("PASS: manual IP diagnostic, verified machine name and rejection of wrong team / unauthorized technician.");
    }
    await client.Start();
    using(var sent=await client.Request(HttpMethod.Post,"/chat",new{text="hello"})){}
    using(var reply=await client.Request(HttpMethod.Get,"/chat?after=0")){
        using var json=JsonDocument.Parse(await reply.Content.ReadAsStringAsync());
        var first=json.RootElement.GetProperty("messages")[0];
        if(first.GetProperty("sequence").GetInt64()!=1||first.GetProperty("text").GetString()!="hello")throw new Exception("Chat protocol mismatch.");
    }
    using(var frame=await client.Request(HttpMethod.Get,"/frame?monitor=primary"))if(await frame.Content.ReadAsStringAsync()!="frame-test")throw new Exception("Frame mismatch.");
    using(var input=await client.Request(HttpMethod.Post,"/input",new InputCommand("move",10,20))){}
    await client.Stop();
    try{using var frame=await client.Request(HttpMethod.Get,"/frame");throw new Exception("Ended session accepted.");}catch(HttpRequestException e)when(e.StatusCode==System.Net.HttpStatusCode.Forbidden){}
    // The same signed request cannot be replayed.
    var verify=new RequestVerifier([issuer.Public],device.Id,issuer.Public);
    var stamp=DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();var nonce=Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
    var signature=tech.Sign("GET","/info",[],"",stamp,nonce,device.Id);
    var encoded=TeamAccess.Encode(tech.Credential!);
    if(verify.Verify(tech.Public.Id,device.Id,"GET","/info",[],"",stamp,nonce,signature,encoded) is null || verify.Verify(tech.Public.Id,device.Id,"GET","/info",[],"",stamp,nonce,signature,encoded) is not null)throw new Exception("Replay protection failed.");
    File.WriteAllText(Path.Combine(bin,"loginctl"),"#!/bin/sh\nprintf 'Active=yes\\nLockedHint=yes\\n'\n");
    try{await client.Start();throw new Exception("Locked desktop accepted.");}catch(HttpRequestException e)when(e.StatusCode==System.Net.HttpStatusCode.Conflict){}
    Console.WriteLine("PASS: TLS pinning, team credential, rejection of unknown identity, consent flow, session, chat page, frame, input and closed-session rejection (mock desktop).");
}finally{if(!agent.HasExited){agent.Kill(true);await agent.WaitForExitAsync();}Directory.Delete(directory,true);}
