using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SdeSuporte;

// Read only, bounded operational data. Endpoints using this class must authenticate
// the technician before reading; never expose arbitrary files or employee input.
public static class OperationalData
{
    static readonly object sync=new();
    public static void Write(string path,object value)
    {
        lock(sync)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            if(File.Exists(path)&&new FileInfo(path).Length>4*1024*1024)
            {
                File.Delete(path+".3");
                for(int i=2;i>=1;i--)if(File.Exists(path+"."+i))File.Move(path+"."+i,path+"."+(i+1),true);
                File.Move(path,path+".1",true);
            }
            File.AppendAllText(path,JsonSerializer.Serialize(value)+Environment.NewLine);
        }
    }
    public static JsonElement[] Tail(string path,int limit=200)
    {
        lock(sync)
        {
            if(!File.Exists(path))return [];
            using var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);
            long start=Math.Max(0,file.Length-256*1024);file.Seek(start,SeekOrigin.Begin);
            using var reader=new StreamReader(file,Encoding.UTF8);
            if(start>0)reader.ReadLine();
            var lines=new Queue<JsonElement>();
            while(reader.ReadLine() is {} line)
            {
                try{using var document=JsonDocument.Parse(line);if(document.RootElement.ValueKind!=JsonValueKind.Object)continue;lines.Enqueue(document.RootElement.Clone());if(lines.Count>limit)lines.Dequeue();}
                catch(JsonException){}
            }
            return lines.ToArray();
        }
    }
    public static JsonElement? UpdateStatus(string root)
    {
        try
        {
            var path=Path.Combine(root,"UpdateCache","status.json");
            if(!File.Exists(path)||new FileInfo(path).Length>65536)return null;
            using var document=JsonDocument.Parse(File.ReadAllText(path));
            return document.RootElement.Clone();
        }
        catch{return null;}
    }
    public static bool MaintenanceRequested(string root,DateTimeOffset now)
    {
        try
        {
            var path=Path.Combine(root,"UpdateCache","maintenance.json");
            if(!File.Exists(path)||new FileInfo(path).Length>4096)return false;
            using var data=JsonDocument.Parse(File.ReadAllText(path));
            var at=data.RootElement.GetProperty("at").GetDateTimeOffset();
            return now>=at&&now-at<TimeSpan.FromMinutes(3)
                &&data.RootElement.GetProperty("role").GetString()=="Agent";
        }
        catch{return false;}
    }
    public static string EventId(JsonElement item)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(item.GetRawText())));
}

public static class PresencePolicy
{
    public static TimeSpan RetryDelay(int failures)=>TimeSpan.FromSeconds(failures==0?12:Math.Min(120,12*Math.Pow(2,Math.Min(failures,4))));
    public static bool IsRecent(DateTimeOffset? last,DateTimeOffset now)=>last is {} at&&now-at<TimeSpan.FromSeconds(45);
}
