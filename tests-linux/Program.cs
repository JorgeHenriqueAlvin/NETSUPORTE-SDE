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
    using var client=new RemoteClient(device with {Host="127.0.0.1"},tech,TimeSpan.FromSeconds(5));
    bool ready=false;Exception? lastError=null;
    for(int i=0;i<40;i++){
        try{using var info=await client.Request(HttpMethod.Get,"/info");ready=true;break;}catch(Exception e){lastError=e;if(agent.HasExited)throw new Exception("Agent stopped: "+await stderr);await Task.Delay(250);}
    }
    if(!ready)throw new Exception("Server unavailable.",lastError);
    using var stranger=new TechnicianIdentity(ECDsa.Create(ECCurve.NamedCurves.nistP256),"Unknown");
    using var forbidden=new RemoteClient(device with {Host="127.0.0.1"},stranger);
    try{using var info=await forbidden.Request(HttpMethod.Get,"/info");throw new Exception("Unauthorized identity accepted.");}catch(HttpRequestException e)when(e.StatusCode==System.Net.HttpStatusCode.Forbidden){}
    await client.Start();
    using(var sent=await client.Request(HttpMethod.Post,"/chat",new{text="hello"})){}
    using(var reply=await client.Request(HttpMethod.Get,"/chat?after=0")){
        using var json=JsonDocument.Parse(await reply.Content.ReadAsStringAsync());
        var first=json.RootElement.GetProperty("messages")[0];
        if(first.GetProperty("sequence").GetInt64()!=1||first.GetProperty("text").GetString()!="hello")throw new Exception("Chat protocol mismatch.");
    }
    using(var frame=await client.Request(HttpMethod.Get,"/frame?monitor=all"))if(await frame.Content.ReadAsStringAsync()!="frame-test")throw new Exception("Frame mismatch.");
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
