using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text.Json;

namespace SdeSuporte;

public enum AgentConnectionState { Ready, InvalidAddress, Unreachable, Refused, Timeout, TlsError, InvalidAgent, UntrustedAgent, CertificateMismatch, Unauthorized }
public record AgentConnectionResult(AgentConnectionState State,string Message,Device? Device=null,bool DesktopAvailable=false)
{
    public bool CanSave=>State==AgentConnectionState.Ready&&Device is not null;
}

public static class AgentConnection
{
    public static IPAddress ParseAddress(string input)
    {
        var range=IPv4Range.Parse(input);
        if(input.Contains('/')||input.Contains('-')||range.Count!=1)throw new ArgumentException("Informe apenas o IP do Raspberry, como 10.37.3.163.");
        return IPv4Range.Address(range.First);
    }
    public static Device ValidateDiscovery(DiscoveryMessage? message,string? actualPin,int port,string[] roots,string technicianId)
    {
        if(message?.Protocol!="SDE-DISCOVERY-1"||message.Enrollment is null)throw new InvalidDataException("A porta respondeu, mas não identificou um agente SDE compatível.");
        Enrollment.VerifyDevice(message.Enrollment,roots,technicianId);
        var device=message.Enrollment.Device;
        if(device.Port!=port||!string.Equals(actualPin,device.CertificateSha256,StringComparison.OrdinalIgnoreCase))throw new AuthenticationException("O certificado da conexão não corresponde à identificação assinada do agente.");
        return device;
    }
    public static async Task<AgentConnectionResult> Test(string input,string[] roots,TechnicianIdentity identity,CancellationToken cancellation=default,int port=7443)
    {
        IPAddress address;
        try{address=ParseAddress(input);}catch(ArgumentException){return new(AgentConnectionState.InvalidAddress,"Informe um IP privado válido, como 10.37.3.163. Não inclua https://, porta ou faixa de rede.");}
        cancellation.ThrowIfCancellationRequested();
        using(var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancellation))
        {
            timeout.CancelAfter(TimeSpan.FromSeconds(3));
            try{using var tcp=new TcpClient(AddressFamily.InterNetwork);await tcp.ConnectAsync(address,port,timeout.Token);}
            catch(OperationCanceledException)when(!cancellation.IsCancellationRequested){return new(AgentConnectionState.Timeout,$"O IP {address} não respondeu na porta {port} em 3 segundos. Confira o IP atual, a rede, o firewall e possível isolamento entre Wi-Fi e cabo.");}
            catch(SocketException e){return e.SocketErrorCode==SocketError.ConnectionRefused
                ?new(AgentConnectionState.Refused,$"O computador recusou a porta {port}. Confira se o agente está iniciado e escutando nessa porta.")
                :new(AgentConnectionState.Unreachable,$"Não foi possível alcançar {address}:{port} pela rede ({e.SocketErrorCode}). Confira o IP e a comunicação entre as máquinas.");}
        }
        string? pin=null;
        using var handler=new HttpClientHandler{UseProxy=false,AllowAutoRedirect=false,
            ServerCertificateCustomValidationCallback=(_,cert,_,_)=>{
                if(cert is null||DateTime.UtcNow<cert.NotBefore.ToUniversalTime()||DateTime.UtcNow>cert.NotAfter.ToUniversalTime())return false;
                pin=Convert.ToHexString(SHA256.HashData(cert.RawData));return true;
            }};
        using var http=new HttpClient(handler){Timeout=TimeSpan.FromSeconds(5),MaxResponseContentBufferSize=50000};
        Device device;
        try
        {
            // This first request contains public enrollment metadata only. No technician
            // credential is sent until the signed enrollment AND connection pin match.
            using var response=await http.GetAsync($"https://{address}:{port}/discover",cancellation);
            if(!response.IsSuccessStatusCode)return new(AgentConnectionState.InvalidAgent,$"A porta está acessível, mas o agente não respondeu à identificação (HTTP {(int)response.StatusCode}). Confira a instalação e o log do Cliente.");
            DiscoveryMessage? message;
            try{message=JsonSerializer.Deserialize<DiscoveryMessage>(await response.Content.ReadAsByteArrayAsync(cancellation),Json.Options);}
            catch(JsonException){return new(AgentConnectionState.InvalidAgent,"A porta respondeu com dados incompatíveis. Confira se é o agente SDE e se a porta está correta.");}
            if(message?.Protocol!="SDE-DISCOVERY-1"||message.Enrollment is null)return new(AgentConnectionState.InvalidAgent,"O endereço não identificou um agente SDE compatível.");
            try{device=ValidateDiscovery(message,pin,port,roots,identity.Public.Id) with{Host=address.ToString()};}
            catch(AuthenticationException){return new(AgentConnectionState.CertificateMismatch,"O certificado não corresponde ao cadastro assinado. O computador não foi adicionado. Confira a identidade do agente; não ignore essa verificação.");}
            catch(Exception e)when(e is InvalidDataException or CryptographicException or FormatException or ArgumentException){return new(AgentConnectionState.UntrustedAgent,"O cadastro do agente não pertence a uma equipe confiável ou sua assinatura é inválida/expirada. Use o Cliente da mesma equipe do Técnico.");}
        }
        catch(OperationCanceledException)when(!cancellation.IsCancellationRequested){return new(AgentConnectionState.Timeout,"A porta abriu, mas a identificação HTTPS não terminou em 5 segundos. Tente novamente e confira o log do agente.");}
        catch(HttpRequestException){return new(AgentConnectionState.TlsError,"A porta abriu, mas a comunicação HTTPS falhou. Confira data/hora das máquinas e o log do agente.");}
        using var client=new RemoteClient(device,identity,TimeSpan.FromSeconds(5));
        try
        {
            using var response=await client.Request(HttpMethod.Get,"/info").WaitAsync(cancellation);
            using var info=JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellation));
            if(!info.RootElement.TryGetProperty("id",out var id)||id.GetString()!=device.Id)return new(AgentConnectionState.InvalidAgent,"A identificação autenticada diverge do cadastro. O computador não foi adicionado.");
            bool available=info.RootElement.TryGetProperty("available",out var ready)&&ready.ValueKind==JsonValueKind.True;
            return new(AgentConnectionState.Ready,$"Agente identificado: {device.Name}\nIP: {device.Host}\nSetor: {device.Sector}\nEquipe e certificado verificados.\n"+(available?"Desktop disponível. Clique em Adicionar à lista; o acesso solicitará autorização no Raspberry.":"Agente acessível, mas o desktop está bloqueado ou indisponível. Você pode salvar agora e conectar após entrar no desktop X11."),device,available);
        }
        catch(HttpRequestException e)when(e.StatusCode==HttpStatusCode.Forbidden){return new(AgentConnectionState.Unauthorized,"O agente foi identificado, mas recusou a credencial deste Técnico. Confira a autorização da equipe e a data/hora das duas máquinas.");}
        catch(HttpRequestException e)when(e.StatusCode==HttpStatusCode.TooManyRequests){return new(AgentConnectionState.Timeout,"O agente está ocupado. Aguarde alguns segundos e teste novamente.");}
        catch(OperationCanceledException)when(!cancellation.IsCancellationRequested){return new(AgentConnectionState.Timeout,"O agente não concluiu a verificação autenticada no prazo. Tente novamente.");}
        catch(Exception e)when(e is HttpRequestException or JsonException){return new(AgentConnectionState.InvalidAgent,"O agente foi identificado, mas a verificação autenticada falhou. Confira o log do Cliente.");}
    }
}
