using System.Security.Cryptography;
using System.Text.Json;

namespace SdeSuporte;

public static class ClientPackage
{
    public static string Verify(string installer,Version? requiredVersion=null)
    {
        string stage="localizar";
        try
        {
            if(!File.Exists(installer))throw new FileNotFoundException("Pacote Cliente ausente.");
            stage="metadados";
            if(!File.Exists(installer+".sha256"))throw new FileNotFoundException("Hash do pacote Cliente ausente.");
            string expected=File.ReadAllText(installer+".sha256").Trim();
            if(expected.Length!=64||!expected.All(Uri.IsHexDigit))throw new InvalidDataException("Metadados de integridade inválidos.");
            stage="integridade";
            using var input=File.OpenRead(installer);
            if(!Convert.ToHexString(SHA256.HashData(input)).Equals(expected,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Pacote Cliente corrompido ou atualização incompleta: SHA256 divergente.");
            stage="compatibilidade";
            var package=System.Diagnostics.FileVersionInfo.GetVersionInfo(installer).ProductVersion;
            var current=requiredVersion??typeof(ClientPackage).Assembly.GetName().Version!;
            if(!Version.TryParse(package,out var version)||version.Major!=current.Major||version.Minor!=current.Minor||version.Build!=current.Build)
                throw new InvalidDataException("Versão do pacote Cliente incompatível com o Técnico.");
            Log(installer,stage,"válido");return expected;
        }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Log(installer,stage,e is UnauthorizedAccessException?"acesso negado":e.Message);
            throw new IOException(e.Message+" Use a aba Atualizações para recuperar pelo atualizador confiável. Se persistir, reinstale o Técnico da versão oficial.",e);
        }
    }
    static void Log(string path,string stage,string reason)
    {
        try{
            var folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"SdeSuporte");Directory.CreateDirectory(folder);
            File.AppendAllText(Path.Combine(folder,"package-verification.jsonl"),JsonSerializer.Serialize(new{at=DateTimeOffset.UtcNow,version=typeof(ClientPackage).Assembly.GetName().Version?.ToString(),path,stage,reason})+Environment.NewLine);
        }catch(IOException){}catch(UnauthorizedAccessException){}
    }
}
