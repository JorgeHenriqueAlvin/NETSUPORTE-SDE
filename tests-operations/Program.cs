using System.Text.Json;
using SdeSuporte;
var root=Path.Combine(Path.GetTempPath(),"sde-operations-"+Guid.NewGuid());Directory.CreateDirectory(root);
void Check(bool test,string reason){if(!test)throw new Exception(reason);}
try
{
    var log=Path.Combine(root,"audit.jsonl");
    for(int i=0;i<450;i++)OperationalData.Write(log,new{id=i,action="test",at=DateTimeOffset.UtcNow});
    var events=OperationalData.Tail(log);Check(events.Length==200,"Tail must be bounded");
    Check(events[0].GetProperty("id").GetInt32()==250,"Tail must retain newest events");
    File.AppendAllText(log,"broken\n{\"action\":\"last\"}\n");
    Check(OperationalData.Tail(log).Last().GetProperty("action").GetString()=="last","Ignore incomplete lines");
    File.WriteAllText(log,new string('x',4*1024*1024+1));
    OperationalData.Write(log,new{action="rotate"});
    Check(File.Exists(log+".1"),"Audit retention must rotate");
    Check(OperationalData.Tail(log).Length==1,"Rotated file starts with valid event");
    var cache=Path.Combine(root,"UpdateCache");Directory.CreateDirectory(cache);
    var now=DateTimeOffset.UtcNow;
    File.WriteAllText(Path.Combine(cache,"maintenance.json"),JsonSerializer.Serialize(new{at=now,role="Agent"}));
    Check(OperationalData.MaintenanceRequested(root,now),"Fresh maintenance request");
    Check(!OperationalData.MaintenanceRequested(root,now.AddMinutes(4)),"Expired maintenance request");
    Check(!OperationalData.MaintenanceRequested(root,now.AddMinutes(-1)),"Future maintenance request");
    File.WriteAllText(Path.Combine(cache,"maintenance.json"),"broken");
    Check(!OperationalData.MaintenanceRequested(root,now),"Malformed maintenance must not stop agent");
    File.WriteAllText(Path.Combine(cache,"status.json"),"{\"state\":\"Installed\"}");
    Check(OperationalData.UpdateStatus(root)?.GetProperty("state").GetString()=="Installed","Update report");
    Check(PresencePolicy.RetryDelay(0).TotalSeconds==12,"Online cadence");
    Check(PresencePolicy.RetryDelay(8).TotalSeconds==120,"Bound offline backoff");
    Check(!PresencePolicy.IsRecent(now.AddSeconds(-46),now),"Old heartbeat must not be online");
    // Model a 400-host round with bounded concurrent slots and real task delays.
    int active=0,maximum=0;
    using(var gate=new SemaphoreSlim(32))
    {
        await Task.WhenAll(Enumerable.Range(0,400).Select(async _=>{
            await gate.WaitAsync();
            int count=Interlocked.Increment(ref active);
            int observed;
            do{observed=maximum;if(count<=observed)break;}while(Interlocked.CompareExchange(ref maximum,count,observed)!=observed);
            try{await Task.Delay(2);}finally{Interlocked.Decrement(ref active);gate.Release();}
        }));
    }
    Check(maximum<=32&&maximum>1&&active==0,"400-host concurrency simulation");
    Console.WriteLine("OK: bounded audit, rotation, maintenance expiry, status and presence policy.");
}
finally{Directory.Delete(root,true);}
