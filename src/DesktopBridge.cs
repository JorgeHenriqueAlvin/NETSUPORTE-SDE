using System.ComponentModel;
using System.Diagnostics;
using System.Drawing.Imaging;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

namespace SdeSuporte;

// The privileged side accepts the original, technician-signed HTTP operation,
// never an unsigned mouse command or an arbitrary executable/path from the user.
sealed record DesktopProof(string Method,string Path,byte[] Body,string Technician,string Device,
    string Session,string Time,string Nonce,string Signature,string Credential)
{
    public static DesktopProof From(System.Net.HttpListenerRequest r,byte[] body)
    {
        string H(string key)=>r.Headers[key]??"";
        return new(r.HttpMethod,r.RawUrl??"/",body,H("X-Technician"),H("X-Device"),H("X-Session"),
            H("X-Time"),H("X-Nonce"),H("X-Signature"),H("X-Team-Credential"));
    }
    public TechnicianKey? Verify(RequestVerifier verifier)=>verifier.Verify(Technician,Device,Method,Path,Body,Session,Time,Nonce,Signature,Credential);
}
sealed record DesktopPacket(DesktopProof Proof,string SessionId="");
sealed record DesktopReply(string Error="",byte[]? Image=null,string Layout="",string Monitor="all",int OffsetX=0,int OffsetY=0,string WorkerPipe="",int WorkerPid=0,int Status=500);

static class DesktopWire
{
    public const string ServiceName="SDE-Suporte-Desktop";
    public const string BrokerPipe="SDE-Suporte-Desktop-v1";
    public const int MaxPacket=16*1024,MaxReply=40*1024*1024;
    public static string InstalledAgent=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"SDE-Suporte","Agent","SDE-Suporte-Cliente.exe");
    public static string InstalledHelper=>Path.Combine(Path.GetDirectoryName(InstalledAgent)!,"SDE-Suporte-Desktop.exe");
    public static bool TrustedServiceLocation=>string.Equals(Environment.ProcessPath,InstalledHelper,StringComparison.OrdinalIgnoreCase);
    public static bool TrustedLocation=>string.Equals(Environment.ProcessPath,InstalledAgent,StringComparison.OrdinalIgnoreCase);
    public static void Write<T>(Stream stream,T value,int limit)
    {
        var data=JsonSerializer.SerializeToUtf8Bytes(value,Json.Options);
        if(data.Length>limit)throw new InvalidDataException("Mensagem do serviço excede o limite.");
        using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(5));
        stream.WriteAsync(BitConverter.GetBytes(data.Length),deadline.Token).AsTask().GetAwaiter().GetResult();
        stream.WriteAsync(data,deadline.Token).AsTask().GetAwaiter().GetResult();
        stream.FlushAsync(deadline.Token).GetAwaiter().GetResult();
    }
    public static T Read<T>(Stream stream,int limit,TimeSpan timeout,CancellationToken cancellation=default)
    {
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(cancellation);deadline.CancelAfter(timeout);
        var size=new byte[4];stream.ReadExactlyAsync(size,deadline.Token).AsTask().GetAwaiter().GetResult();
        int count=BitConverter.ToInt32(size);
        if(count<2||count>limit)throw new InvalidDataException("Mensagem inválida do serviço.");
        var data=new byte[count];stream.ReadExactlyAsync(data,deadline.Token).AsTask().GetAwaiter().GetResult();
        return JsonSerializer.Deserialize<T>(data,Json.Options)??throw new InvalidDataException("Mensagem vazia do serviço.");
    }
    public static NamedPipeServerStream Server(string name)
    {
        var acl=new PipeSecurity();acl.SetAccessRuleProtection(true,false);
        acl.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid,null),PipeAccessRights.FullControl,AccessControlType.Deny));
        acl.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid,null),PipeAccessRights.FullControl,AccessControlType.Allow));
        acl.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid,null),PipeAccessRights.ReadWrite,AccessControlType.Allow));
        return NamedPipeServerStreamAcl.Create(name,PipeDirection.InOut,8,PipeTransmissionMode.Byte,PipeOptions.Asynchronous,65536,65536,acl);
    }
    public static NamedPipeClientStream Client(string pipe)=>new(".",pipe,PipeDirection.InOut,PipeOptions.Asynchronous,TokenImpersonationLevel.Identification);
    public static RequestVerifier Verifier()
    {
        var config=Json.Read<AgentConfig>(Path.Combine(AppContext.BaseDirectory,"agent.json"));config.Device.Validate();
        return new(config.Technicians,config.Device.Id,config.Enrollment?.Approval.Permission.TeamWide==true?config.Enrollment.Approval.Permission.Issuer:null);
    }
}

// One persistent connection per consented remote session. Closing it revokes the
// helper and releases held input, including on disconnect, expiry or user lock.
sealed class DesktopBridge:IDisposable
{
    readonly NamedPipeClientStream pipe;
    readonly SemaphoreSlim serial=new(1,1);
    public static string LastError {get;private set;}="";
    DesktopBridge(NamedPipeClientStream pipe)=>this.pipe=pipe;
    public static async Task<DesktopBridge?> Open(DesktopProof proof,string sessionId)
    {
        LastError="";
        if(!DesktopWire.TrustedLocation){LastError="default_path_required";return null;}
        NamedPipeClientStream? worker=null;
        try
        {
            using var broker=DesktopWire.Client(DesktopWire.BrokerPipe);
            await broker.ConnectAsync(1500);
            // Do not connect to a pipe impersonating the installed SYSTEM service.
            DesktopNative.VerifyServer(broker);
            var packet=new DesktopPacket(proof,sessionId);
            await Task.Run(()=>DesktopWire.Write(broker,packet,DesktopWire.MaxPacket));
            var response=await Task.Run(()=>DesktopWire.Read<DesktopReply>(broker,DesktopWire.MaxReply,TimeSpan.FromSeconds(5)));
            if(response.Error!="")throw new IOException(response.Error);
            if(!response.WorkerPipe.StartsWith("SDE-Desktop-",StringComparison.Ordinal)||response.WorkerPipe.Length!=44)throw new IOException("Canal inválido do serviço.");
            worker=DesktopWire.Client(response.WorkerPipe);await worker.ConnectAsync(5000);DesktopNative.VerifyServer(worker,response.WorkerPid);
            await Task.Run(()=>DesktopWire.Write(worker,packet,DesktopWire.MaxPacket));
            response=await Task.Run(()=>DesktopWire.Read<DesktopReply>(worker,DesktopWire.MaxReply,TimeSpan.FromSeconds(5)));
            if(response.Error!="")throw new IOException(response.Error);
            return new(worker);
        }
        catch(Exception e){LastError=e.GetType().Name+": "+e.Message;worker?.Dispose();return null;}
    }
    public async Task<DesktopReply> Request(DesktopProof proof)
    {
        await serial.WaitAsync();
        try
        {
            var response=await Task.Run(()=>{
                DesktopWire.Write(pipe,new DesktopPacket(proof),DesktopWire.MaxPacket);
                return DesktopWire.Read<DesktopReply>(pipe,DesktopWire.MaxReply,TimeSpan.FromSeconds(5));
            });
            if(response.Error!="")throw new System.Net.Http.HttpRequestException(response.Error,null,(System.Net.HttpStatusCode)response.Status);
            return response;
        }
        finally{serial.Release();}
    }
    public void Dispose()=>pipe.Dispose();
}

static class DesktopService
{
    static readonly CancellationTokenSource stop=new();
    static readonly Dictionary<int,Process> workers=[];
    static readonly object gate=new();
    static readonly SemaphoreSlim slots=new(8,8);
    public static void Start()
    {
        if(!WindowsIdentity.GetCurrent().IsSystem||!DesktopWire.TrustedServiceLocation)throw new UnauthorizedAccessException("O auxiliar requer o serviço instalado em Program Files.");
#if DESKTOP_SERVICE
        System.ServiceProcess.ServiceBase.Run(new BrokerService());
#else
        throw new InvalidOperationException("Execute o auxiliar instalado como serviço.");
#endif
    }
#if DESKTOP_SERVICE
    // Let the Windows service framework own SCM status transitions and control
    // acknowledgement; do not race manual STOPPED reports against the handler.
    sealed class BrokerService:System.ServiceProcess.ServiceBase
    {
        Task? listening;
        public BrokerService(){ServiceName=DesktopWire.ServiceName;CanStop=true;CanShutdown=true;AutoLog=false;}
        protected override void OnStart(string[] args)
        {
            try
            {
                var verifier=DesktopWire.Verifier();
                var first=DesktopWire.Server(DesktopWire.BrokerPipe);
                listening=Task.Run(()=>Listen(verifier,first));
                Record("service_started");
            }
            catch(Exception e){Failure("service_start_failed",e);throw;}
        }
        protected override void OnStop()
        {
            stop.Cancel();
            lock(gate)foreach(var child in workers.Values)try{if(!child.HasExited)child.Kill();}catch{}
            try{listening?.Wait(TimeSpan.FromSeconds(5));}catch(AggregateException e){Failure("service_stop_failed",e);}
            Record("service_stopped");
        }
        protected override void OnShutdown()=>OnStop();
    }
#endif
    static void Record(string action)
    {
        try{OperationalData.Write(Path.Combine(AppContext.BaseDirectory,"desktop-service.jsonl"),new{at=DateTimeOffset.UtcNow,action});}catch{}
    }
    internal static void Failure(string action,Exception e)
    {
        if(!DesktopWire.TrustedServiceLocation||!WindowsIdentity.GetCurrent().IsSystem)return;
        try{OperationalData.Write(Path.Combine(AppContext.BaseDirectory,"desktop-service.jsonl"),new{at=DateTimeOffset.UtcNow,action,error=e.GetType().Name,code=e is Win32Exception win?win.NativeErrorCode:0,detail=e.Message});}catch{}
    }
    static void Listen(RequestVerifier verifier,NamedPipeServerStream first)
    {
        NamedPipeServerStream? pending=first;
        try
        {
            while(!stop.IsCancellationRequested)
            {
                slots.Wait(stop.Token);
                var pipe=pending??DesktopWire.Server(DesktopWire.BrokerPipe);pending=null;
                try{pipe.WaitForConnectionAsync(stop.Token).GetAwaiter().GetResult();}
                catch{pipe.Dispose();slots.Release();throw;}
                _=Task.Run(()=>{try{Accept(pipe,verifier);}finally{slots.Release();}});
            }
        }
        catch(OperationCanceledException)when(stop.IsCancellationRequested){}
        catch(Exception e){Failure("service_failed",e);stop.Cancel();}
        finally
        {
            pending?.Dispose();
            lock(gate)foreach(var child in workers.Values)try{if(!child.HasExited)child.Kill();}catch{}
        }
    }
    static void Accept(NamedPipeServerStream pipe,RequestVerifier verifier)
    {
        Process? child=null;int session=-1;
        try
        {
            int pid=DesktopNative.ClientPid(pipe);
            using var client=Process.GetProcessById(pid);session=client.SessionId;
            if(session==0||!DesktopNative.IsAgent(client)||!DesktopNative.Unlocked(session))throw new UnauthorizedAccessException("Cliente local inválido ou sessão bloqueada.");
            var packet=DesktopWire.Read<DesktopPacket>(pipe,DesktopWire.MaxPacket,TimeSpan.FromSeconds(3),stop.Token);
            if(!StartPacket(packet)||packet.Proof.Verify(verifier) is null)throw new UnauthorizedAccessException("Solicitação de atendimento não autorizada.");
            string name="SDE-Desktop-"+Guid.NewGuid().ToString("N");
            Process? previous;lock(gate)workers.TryGetValue(session,out previous);
            if(previous is not null)try{previous.WaitForExit(1500);}catch(ObjectDisposedException){}
            lock(gate)
            {
                if(workers.TryGetValue(session,out var pending)&&pending.HasExited)workers.Remove(session);
                if(workers.ContainsKey(session)||workers.Count>=8||stop.IsCancellationRequested)throw new IOException("Auxiliar ocupado.");
                child=DesktopNative.Launch(session,name,pid);workers.Add(session,child);
            }
            DesktopWire.Write(pipe,new DesktopReply(WorkerPipe:name,WorkerPid:child.Id),DesktopWire.MaxReply);pipe.Dispose();
            child.WaitForExit();
        }
        catch(Exception e){Failure("helper_failed",e);try{DesktopWire.Write(pipe,new DesktopReply(Error:"Serviço de desktop indisponível ou solicitação recusada."),DesktopWire.MaxReply);}catch{}}
        finally
        {
            pipe.Dispose();
            if(child is not null)
            {
                try{if(!child.HasExited)child.Kill();}catch{}
                lock(gate)if(workers.TryGetValue(session,out var registered)&&ReferenceEquals(registered,child))workers.Remove(session);
                child.Dispose();
            }
        }
    }
    internal static bool StartPacket(DesktopPacket p)=>p.Proof.Method=="POST"&&p.Proof.Path=="/session"&&p.Proof.Session==""&&p.Proof.Body.Length==0
        &&p.SessionId.Length==64&&p.SessionId.All(Uri.IsHexDigit);

    // Runs in the employee's Windows session under SYSTEM. This thread owns no
    // windows/hooks, so it can attach to the active desktop for each operation.
    public static void Worker(string name,int agentPid)
    {
        if(!WindowsIdentity.GetCurrent().IsSystem||!DesktopWire.TrustedServiceLocation||!name.StartsWith("SDE-Desktop-",StringComparison.Ordinal)||name.Length!=44)return;
        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        int session=Process.GetCurrentProcess().SessionId;
        if(session==0||!DesktopNative.Unlocked(session))return;
        using var agent=Process.GetProcessById(agentPid);
        if(agent.SessionId!=session||!DesktopNative.IsAgent(agent))return;
        using var pipe=DesktopWire.Server(name);
        using var connectionLimit=new CancellationTokenSource(TimeSpan.FromSeconds(8));
        pipe.WaitForConnectionAsync(connectionLimit.Token).GetAwaiter().GetResult();
        if(DesktopNative.ClientPid(pipe)!=agentPid)return;
        var verifier=DesktopWire.Verifier();
        var first=DesktopWire.Read<DesktopPacket>(pipe,DesktopWire.MaxPacket,TimeSpan.FromSeconds(3));
        if(!StartPacket(first))return;
        var tech=first.Proof.Verify(verifier);if(tech is null)return;
        var life=Stopwatch.StartNew();var keys=new HashSet<int>();var buttons=new HashSet<int>();
        DesktopWire.Write(pipe,new DesktopReply(),DesktopWire.MaxReply);
        try
        {
            while(!agent.HasExited&&life.Elapsed<TimeSpan.FromHours(1))
            {
                var packet=DesktopWire.Read<DesktopPacket>(pipe,DesktopWire.MaxPacket,TimeSpan.FromSeconds(20));var p=packet.Proof;
                if(!DesktopNative.Unlocked(session))break;
                if(p.Session!=first.SessionId||p.Technician!=tech.Id||p.Verify(verifier) is null)throw new UnauthorizedAccessException();
                if(p.Method=="POST"&&p.Path=="/session/end")break;
                DesktopReply response;
                try
                {
                    using var desktop=DesktopNative.Attach(session);
                    if(p.Method=="GET"&&(p.Path=="/frame"||p.Path.StartsWith("/frame?",StringComparison.Ordinal)))response=Capture(p.Path);
                    else if(p.Method=="POST"&&p.Path=="/input")
                    {
                        var input=JsonSerializer.Deserialize<InputCommand>(p.Body,Json.Options)??throw new ArgumentException("Entrada inválida.");
                        if(input.Text is null||input.Text.Length>1000)throw new ArgumentException("Entrada inválida.");
                        if(input.Layout!=""&&input.Layout!=MonitorCoordinates.Layout(Screen.AllScreens))
                        {
                            Release(keys,buttons);throw new InvalidOperationException("Os monitores mudaram. Aguarde a próxima imagem antes de clicar.");
                        }
                        Native.Apply(input);
                        if(input.Kind=="keyDown")keys.Add(input.Key);if(input.Kind=="keyUp")keys.Remove(input.Key);
                        if(input.Kind=="down")buttons.Add(input.Key);if(input.Kind=="up")buttons.Remove(input.Key);
                        response=new();
                    }
                    // Signed chat polling keeps the consented helper alive when
                    // the technician switches away from the remote screen tab.
                    else if((p.Method=="GET"&&(p.Path=="/chat"||p.Path.StartsWith("/chat?after=",StringComparison.Ordinal)))||(p.Method=="POST"&&p.Path=="/chat"))response=new();
                    else throw new UnauthorizedAccessException("Operação não permitida no auxiliar.");
                    if(!DesktopNative.Unlocked(session))break;
                }
                catch(Exception e){response=new(Error:e.Message,Status:e is InvalidOperationException?409:500);}
                DesktopWire.Write(pipe,response,DesktopWire.MaxReply);
            }
        }
        finally{try{using var desktop=DesktopNative.Attach(session);Release(keys,buttons);}catch{}}
    }
    static void Release(HashSet<int> keys,HashSet<int> buttons)
    {
        foreach(int k in keys)try{Native.Apply(new("keyUp",Key:k));}catch{}
        foreach(int b in buttons)try{Native.Apply(new("up",Key:b));}catch{}
        keys.Clear();buttons.Clear();
    }
    static DesktopReply Capture(string path)
    {
        var uri=new Uri("https://localhost"+path);var query=System.Web.HttpUtility.ParseQueryString(uri.Query);
        var displays=Screen.AllScreens;string? requested=query["monitor"];
        var selected=requested is null or "all"?null:displays.FirstOrDefault(s=>s.DeviceName==requested)??displays.FirstOrDefault(s=>s.Primary)??displays[0];
        var all=SystemInformation.VirtualScreen;var bounds=selected?.Bounds??all;
        if(bounds.Width<=0||bounds.Height<=0||(long)bounds.Width*bounds.Height>40_000_000)throw new IOException("Resolução do desktop indisponível.");
        using var bmp=new Bitmap(bounds.Width,bounds.Height);using(var graphics=Graphics.FromImage(bmp))graphics.CopyFromScreen(bounds.Location,Point.Empty,bounds.Size);
        using var data=new MemoryStream();using var parameters=new EncoderParameters(1);
        parameters.Param[0]=new EncoderParameter(System.Drawing.Imaging.Encoder.Quality,60L);
        bmp.Save(data,ImageCodecInfo.GetImageEncoders().Single(x=>x.FormatID==ImageFormat.Jpeg.Guid),parameters);
        return new(Image:data.ToArray(),Layout:MonitorCoordinates.Layout(displays),Monitor:selected?.DeviceName??"all",OffsetX:bounds.X-all.X,OffsetY:bounds.Y-all.Y);
    }
}

static class DesktopNative
{
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool GetNamedPipeClientProcessId(SafePipeHandle pipe,out uint pid);
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe,out uint pid);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool QueryFullProcessImageName(SafeProcessHandle process,uint flags,StringBuilder name,ref uint length);
    [DllImport("advapi32.dll",SetLastError=true)] static extern bool OpenProcessToken(SafeProcessHandle process,uint access,out SafeAccessTokenHandle token);
    [DllImport("advapi32.dll",SetLastError=true)] static extern bool DuplicateTokenEx(SafeAccessTokenHandle token,uint access,IntPtr attributes,int level,int type,out SafeAccessTokenHandle copy);
    [DllImport("advapi32.dll",SetLastError=true)] static extern bool SetTokenInformation(SafeAccessTokenHandle token,int type,ref int value,int length);
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] struct StartupInfo
    {
        public int Size;public string? Reserved,Desktop,Title;public int X,Y,XSize,YSize,XCount,YCount,Fill,Flags;public short Show,ReservedSize;public IntPtr Reserved2,StdInput,StdOutput,StdError;
    }
    [StructLayout(LayoutKind.Sequential)] struct ProcessInfo {public IntPtr Process,Thread;public int Pid,Tid;}
    [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool CreateProcessAsUser(SafeAccessTokenHandle token,string application,StringBuilder command,IntPtr processAttributes,IntPtr threadAttributes,bool inherit,uint flags,IntPtr environment,string directory,ref StartupInfo startup,out ProcessInfo process);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr value);
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] static extern IntPtr GetThreadDesktop(uint thread);
    [DllImport("user32.dll",SetLastError=true)] static extern IntPtr OpenInputDesktop(uint flags,bool inherit,uint access);
    [DllImport("user32.dll",SetLastError=true)] static extern bool SetThreadDesktop(IntPtr desktop);
    [DllImport("user32.dll")] static extern bool CloseDesktop(IntPtr desktop);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern bool GetUserObjectInformation(IntPtr handle,int index,StringBuilder name,int size,out int needed);
    [DllImport("wtsapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool WTSQuerySessionInformation(IntPtr server,int session,int info,out IntPtr buffer,out uint bytes);
    [DllImport("wtsapi32.dll")] static extern void WTSFreeMemory(IntPtr buffer);
    public static int ClientPid(NamedPipeServerStream pipe)=>GetNamedPipeClientProcessId(pipe.SafePipeHandle,out var pid)?checked((int)pid):throw new Win32Exception();
    [DllImport("kernel32.dll",SetLastError=true)] static extern SafeProcessHandle OpenProcess(uint access,bool inherit,int pid);
    [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern IntPtr OpenSCManager(string? machine,string? database,uint access);
    [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern IntPtr OpenService(IntPtr manager,string name,uint access);
    [StructLayout(LayoutKind.Sequential)] struct ServiceProcessStatus {public uint Type,State,Accepted,Exit,SpecificExit,Checkpoint,WaitHint,Pid,Flags;}
    [DllImport("advapi32.dll",SetLastError=true)] static extern bool QueryServiceStatusEx(IntPtr service,int level,out ServiceProcessStatus status,int size,out int needed);
    [DllImport("advapi32.dll")] static extern bool CloseServiceHandle(IntPtr handle);
    public static bool IsAgent(Process process)=>IsImage(process,DesktopWire.InstalledAgent);
    static bool IsImage(Process process,string expected)
    {
        using var handle=OpenProcess(0x1000,false,process.Id);
        var path=new StringBuilder(32768);uint length=(uint)path.Capacity;
        return !handle.IsInvalid&&QueryFullProcessImageName(handle,0,path,ref length)&&string.Equals(path.ToString(),expected,StringComparison.OrdinalIgnoreCase);
    }
    public static void VerifyServer(NamedPipeClientStream pipe,int expectedPid=0)
    {
        if(expectedPid==0)
        {
            var manager=OpenSCManager(null,null,1);if(manager==IntPtr.Zero)throw new Win32Exception();
            try
            {
                var service=OpenService(manager,DesktopWire.ServiceName,4);if(service==IntPtr.Zero)throw new Win32Exception();
                try
                {
                    if(!QueryServiceStatusEx(service,0,out var status,Marshal.SizeOf<ServiceProcessStatus>(),out _)||status.State!=4)throw new IOException("Serviço não iniciado.");
                    expectedPid=checked((int)status.Pid);
                }
                finally{CloseServiceHandle(service);}
            }
            finally{CloseServiceHandle(manager);}
        }
        if(!GetNamedPipeServerProcessId(pipe.SafePipeHandle,out var pid)||pid!=expectedPid)throw new UnauthorizedAccessException("Identidade do serviço inválida.");
        using var server=Process.GetProcessById(expectedPid);
        if(!IsImage(server,DesktopWire.InstalledHelper))throw new UnauthorizedAccessException("Executável do serviço inválido.");
    }
    public static bool Unlocked(int session)
    {
        // WTSINFOEXW (x64): Level at 0, aligned union at 8; the first
        // three ULONG/LONG members are SessionId, state and lock flags.
        // Require active + explicitly unlocked. A failed query never authorizes.
        if(session<=0||!WTSQuerySessionInformation(IntPtr.Zero,session,25,out var data,out var bytes))return false;
        try{return bytes>=20&&Marshal.ReadInt32(data)==1&&Marshal.ReadInt32(data,8)==session&&Marshal.ReadInt32(data,12)==0&&Marshal.ReadInt32(data,16)==1;}
        finally{WTSFreeMemory(data);}
    }
    [StructLayout(LayoutKind.Explicit,Size=16)] struct TokenPrivileges {[FieldOffset(0)]public uint Count;[FieldOffset(4)]public long Luid;[FieldOffset(12)]public uint Attributes;}
    [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool LookupPrivilegeValue(string? system,string name,out long luid);
    [DllImport("advapi32.dll",SetLastError=true)] static extern bool AdjustTokenPrivileges(SafeAccessTokenHandle token,bool disable,ref TokenPrivileges state,int length,IntPtr previous,IntPtr needed);
    static void Enable(SafeAccessTokenHandle token,string name)
    {
        if(!LookupPrivilegeValue(null,name,out var luid))throw new Win32Exception();
        var state=new TokenPrivileges{Count=1,Luid=luid,Attributes=2};
        if(!AdjustTokenPrivileges(token,false,ref state,0,IntPtr.Zero,IntPtr.Zero)||Marshal.GetLastWin32Error()!=0)throw new Win32Exception();
    }
    public static Process Launch(int session,string pipe,int pid)
    {
        using var own=Process.GetCurrentProcess();
        if(!OpenProcessToken(own.SafeHandle,0x2B,out var token))throw new Win32Exception();
        using(token)
        {
            Enable(token,"SeTcbPrivilege");Enable(token,"SeAssignPrimaryTokenPrivilege");Enable(token,"SeIncreaseQuotaPrivilege");
            if(!DuplicateTokenEx(token,0xF01FF,IntPtr.Zero,2,1,out var copy))throw new Win32Exception();
            using(copy)
            {
                if(!SetTokenInformation(copy,12,ref session,4))throw new Win32Exception();
                var startup=new StartupInfo{Size=Marshal.SizeOf<StartupInfo>(),Desktop=@"winsta0\default"};
                var command=new StringBuilder($"\"{DesktopWire.InstalledHelper}\" --desktop-worker {pipe} {pid}");
                if(!CreateProcessAsUser(copy,DesktopWire.InstalledHelper,command,IntPtr.Zero,IntPtr.Zero,false,0x08000000,IntPtr.Zero,AppContext.BaseDirectory,ref startup,out var info))throw new Win32Exception();
                try{return Process.GetProcessById(info.Pid);}
                finally{CloseHandle(info.Thread);CloseHandle(info.Process);}
            }
        }
    }
    public static IDisposable Attach(int session)
    {
        if(!Unlocked(session))throw new UnauthorizedAccessException("Sessão bloqueada ou desconectada.");
        var old=GetThreadDesktop(GetCurrentThreadId());var current=OpenInputDesktop(0,false,0x181);
        if(current==IntPtr.Zero)throw new Win32Exception();
        var name=new StringBuilder(256);
        if(!GetUserObjectInformation(current,2,name,512,out _)||!(name.ToString().Equals("Default",StringComparison.OrdinalIgnoreCase)||name.ToString().Equals("Winlogon",StringComparison.OrdinalIgnoreCase))||!SetThreadDesktop(current))
        {CloseDesktop(current);throw new UnauthorizedAccessException("Desktop protegido indisponível.");}
        return new Attached(old,current);
    }
    sealed class Attached(IntPtr previous,IntPtr current):IDisposable
    {
        public void Dispose(){if(!SetThreadDesktop(previous))Environment.FailFast("Não foi possível restaurar o desktop do auxiliar.");CloseDesktop(current);}
    }
}
