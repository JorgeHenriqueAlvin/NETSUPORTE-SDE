namespace SdeSuporte;

public record MessageText(string Text);
public record AnnouncementRequest(string Id,string Text);
public record SupportMessage(long Sequence,string Sender,string Text,DateTimeOffset At);
public record ChatPage(SupportMessage[] Messages);

// Session-scoped, bounded, plain-text messages. No executable or formatted content.
public sealed class MessageHistory
{
    readonly Queue<SupportMessage> messages=new();
    long sequence;
    public static string Validate(string? text)
    {
        if(string.IsNullOrWhiteSpace(text)||text.Length>2000||text.Any(c=>char.IsControl(c)&&c is not '\n' and not '\r' and not '\t'))
            throw new ArgumentException("Digite uma mensagem de 1 a 2.000 caracteres.");
        return text.Trim();
    }
    public SupportMessage Add(string sender,string text)
    {
        var message=new SupportMessage(++sequence,sender,Validate(text),DateTimeOffset.UtcNow);
        messages.Enqueue(message);while(messages.Count>200)messages.Dequeue();return message;
    }
    public ChatPage After(long after)=>new(messages.Where(m=>m.Sequence>after).ToArray());
}

public sealed class MessagePanel:UserControl
{
    readonly RichTextBox history=new(){Dock=DockStyle.Fill,ReadOnly=true,DetectUrls=false,BackColor=Color.White};
    readonly TextBox input=new(){Dock=DockStyle.Fill,Multiline=true,MaxLength=2000,ScrollBars=ScrollBars.Vertical};
    readonly Button send=new(){Dock=DockStyle.Right,Width=110,Text="Enviar"};
    readonly Label heading=new(){Dock=DockStyle.Top,Height=45,Padding=new Padding(8),Text="Chat do atendimento"};
    public Func<string,Task>? Send {get;set;}
    public MessagePanel()
    {
        Dock=DockStyle.Fill;Font=new Font("Segoe UI",10);
        var composer=new Panel{Dock=DockStyle.Bottom,Height=80,Padding=new Padding(6)};
        composer.Controls.Add(input);composer.Controls.Add(send);
        Controls.Add(history);Controls.Add(composer);Controls.Add(heading);
        send.Click+=async(_,_)=>{
            if(Send is null)return;
            try{string value=MessageHistory.Validate(input.Text);send.Enabled=false;input.ReadOnly=true;await Send(value);input.Clear();}
            catch(Exception e){MessageBox.Show(this,e.Message,"Mensagens",MessageBoxButtons.OK,MessageBoxIcon.Information);}
            finally{if(!IsDisposed){send.Enabled=true;input.ReadOnly=false;}}
        };
    }
    public void SetTitle(string title)=>heading.Text=title;
    public void SetAvailable(bool value){input.Enabled=value;send.Enabled=value;}
    public void Append(SupportMessage message)
    {
        // Keep the visible history bounded as well as the protocol history.
        if(history.TextLength>450000)history.Clear();
        history.AppendText($"[{message.At.ToLocalTime():HH:mm}] {message.Sender}\n{message.Text}\n\n");
        history.SelectionStart=history.TextLength;history.ScrollToCaret();
    }
    public void ClearHistory()=>history.Clear();
}

sealed class EmployeeChat:Form
{
    public MessagePanel Messages {get;}=new();
    public EmployeeChat(string technician)
    {
        Text="SDE Suporte — mensagens";Size=new(500,540);MinimumSize=new(360,350);
        StartPosition=FormStartPosition.CenterScreen;Controls.Add(Messages);
        Messages.SetTitle("Atendimento com: "+technician);
        FormClosing+=(_,e)=>{if(e.CloseReason==CloseReason.UserClosing){e.Cancel=true;Hide();}};
    }
    public void Receive(SupportMessage message,bool reveal)
    {Messages.Append(message);if(reveal){Show();Activate();}}
}

sealed class EmployeeAnnouncements:Form
{
    readonly RichTextBox history=new(){Dock=DockStyle.Fill,ReadOnly=true,DetectUrls=false,BackColor=Color.White,Font=new Font("Segoe UI",12)};
    public EmployeeAnnouncements()
    {
        Text="SDE Suporte — avisos da TI";Size=new(620,430);MinimumSize=new(380,250);TopMost=true;
        StartPosition=FormStartPosition.CenterScreen;Controls.Add(history);
        var close=new Button{Dock=DockStyle.Bottom,Height=40,Text="Fechar"};close.Click+=(_,_)=>Hide();Controls.Add(close);
        FormClosing+=(_,e)=>{if(e.CloseReason==CloseReason.UserClosing){e.Cancel=true;Hide();}};
    }
    public void Receive(SupportMessage message)
    {
        if(history.TextLength>100000)history.Clear();
        history.AppendText($"AVISO DA TI • {message.At.ToLocalTime():dd/MM HH:mm}\nEnviado por: {message.Sender}\n\n{message.Text}\n\n────────────\n\n");
        history.SelectionStart=history.TextLength;history.ScrollToCaret();Show();Activate();
    }
}

sealed class FullScreenWindow:Form
{
    public FullScreenWindow(Control screen,Action exit,Action chat,Func<Task> disconnect)
    {
        Text="SDE Suporte — tela cheia";FormBorderStyle=FormBorderStyle.None;StartPosition=FormStartPosition.Manual;
        Bounds=Screen.FromControl(screen).Bounds;WindowState=FormWindowState.Maximized;BackColor=Color.Black;
        var bar=new FlowLayoutPanel{Dock=DockStyle.Top,Height=38,BackColor=Color.White};
        var back=new Button{AutoSize=true,Text="Sair da tela cheia (F11)"};back.Click+=(_,_)=>exit();
        var messages=new Button{AutoSize=true,Text="Chat"};messages.Click+=(_,_)=>chat();
        var end=new Button{AutoSize=true,Text="Encerrar atendimento"};end.Click+=async(_,_)=>await disconnect();
        bar.Controls.AddRange([back,messages,end]);Controls.Add(screen);Controls.Add(bar);
    }
}
