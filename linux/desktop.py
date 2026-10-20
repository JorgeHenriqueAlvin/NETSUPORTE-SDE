#!/usr/bin/env python3
"""X11 desktop bridge. Never accepts shell commands from the remote console."""
import base64
import io
import json
import queue
import re
import subprocess
import sys
import threading


def run(*args):
    return subprocess.check_output(args, timeout=5, text=True).strip()


def monitors():
    result = []
    for line in run('xrandr', '--listactivemonitors').splitlines()[1:]:
        match = re.search(r'\s\d+:\s+([+*]*)(\S+)\s+(\d+)/\d+x(\d+)/\d+([+-]\d+)([+-]\d+)', line)
        if match:
            flags, name, w, h, x, y = match.groups()
            result.append(dict(id=name, name=name, x=int(x), y=int(y), width=int(w), height=int(h), primary='*' in flags))
    if not result:
        raise RuntimeError('Nenhum monitor X11 disponível')
    return result


def layout(items):
    return base64.b64encode(json.dumps(items, separators=(',', ':'), ensure_ascii=True).encode()).decode()


KEYS = {8:'BackSpace',9:'Tab',13:'Return',16:'Shift_L',17:'Control_L',18:'Alt_L',19:'Pause',20:'Caps_Lock',27:'Escape',32:'space',33:'Prior',34:'Next',35:'End',36:'Home',37:'Left',38:'Up',39:'Right',40:'Down',45:'Insert',46:'Delete',91:'Super_L',92:'Super_R',93:'Menu',144:'Num_Lock',145:'Scroll_Lock',186:'semicolon',187:'equal',188:'comma',189:'minus',190:'period',191:'slash',192:'grave',219:'bracketleft',220:'backslash',221:'bracketright',222:'apostrophe',106:'KP_Multiply',107:'KP_Add',109:'KP_Subtract',110:'KP_Decimal',111:'KP_Divide'}
KEYS.update({i:chr(i).lower() for i in range(65,91)})
KEYS.update({i:chr(i) for i in range(48,58)})
KEYS.update({112+i:'F'+str(i+1) for i in range(24)})
KEYS.update({96+i:'KP_'+str(i) for i in range(10)})


def apply_input(c):
    kind = c['kind']
    if kind in ('move','down','up'):
        current = monitors()
        if c.get('layout') and c['layout'] != layout(current):
            raise ValueError('Monitores mudaram; aguarde nova captura')
        x, y = int(c.get('x',0)), int(c.get('y',0))
        x += min(m['x'] for m in current)
        y += min(m['y'] for m in current)
        if not any(m['x'] <= x < m['x']+m['width'] and m['y'] <= y < m['y']+m['height'] for m in current):
            raise ValueError('Coordenada fora dos monitores')
        run('xdotool','mousemove','--',str(x),str(y))
        if kind != 'move':
            # Protocol uses 1 for left and 2 for right.
            button = {1:'1',2:'3'}.get(c.get('key'))
            if not button:
                raise ValueError('Botão inválido')
            run('xdotool', 'mousedown' if kind=='down' else 'mouseup', button)
    elif kind in ('keyDown','keyUp'):
        key = KEYS.get(c.get('key'))
        if not key:
            raise ValueError('Tecla não suportada')
        run('xdotool','keydown' if kind=='keyDown' else 'keyup',key)
    elif kind == 'text':
        text = c.get('text','')
        if not isinstance(text,str) or len(text)>1000:
            raise ValueError('Texto inválido')
        run('xdotool','type','--clearmodifiers','--',text)
    elif kind == 'wheel':
        delta = int(c.get('delta',0))
        for _ in range(min(20,max(1,abs(delta)//120))):
            run('xdotool','click','4' if delta>0 else '5')
    else:
        raise ValueError('Entrada não suportada')


def style_window(root):
    root.configure(background='#f4f7fa')
    root.option_add('*Font', ('DejaVu Sans', 10))
    root.option_add('*Background', '#f4f7fa')
    root.option_add('*Foreground', '#182b42')
    root.option_add('*Button.Background', '#ffffff')
    root.option_add('*Button.activeBackground', '#e6f2f4')
    root.option_add('*Button.padX', 12)
    root.option_add('*Button.padY', 8)
    root.option_add('*Button.highlightColor', '#00727d')
    root.option_add('*Entry.Background', '#ffffff')
    root.option_add('*Text.Background', '#ffffff')


def ui():
    import tkinter as tk
    from tkinter.scrolledtext import ScrolledText
    from PIL import Image, ImageTk
    root=tk.Tk(); root.title('SDE Suporte — Cliente'); root.geometry('600x480'); root.minsize(420,360); style_window(root)
    root.protocol('WM_DELETE_WINDOW',root.iconify)
    label=tk.Label(root,text='SDE Suporte disponível',bg='#143151',fg='white',pady=16); label.pack(fill='x')
    history=ScrolledText(root,state='disabled',wrap='word',borderwidth=1,padx=12,pady=12); history.pack(fill='both',expand=True,padx=12,pady=12)
    image_label=tk.Label(root); image_label.pack()
    tk.Label(root,text='Mensagem para o técnico',anchor='w').pack(fill='x',padx=12)
    entry=tk.Entry(root); entry.pack(fill='x',padx=12,pady=6)
    active=False
    def send():
        value=entry.get().strip()
        if active and value:
            print(json.dumps({'text':value[:2000]}),flush=True); entry.delete(0,'end')
    send_button=tk.Button(root,text='Enviar mensagem ao técnico',command=send,bg='#00727d',fg='white',activebackground='#005b66',activeforeground='white',state='disabled'); send_button.pack(padx=12,pady=(0,12),fill='x')
    entry.bind('<Return>',lambda _:send())
    pending=queue.Queue()
    def read():
        for line in sys.stdin:
            try: pending.put(json.loads(line))
            except (ValueError,TypeError): pass
        pending.put({'kind':'quit'})
    threading.Thread(target=read,daemon=True).start()
    def poll():
        nonlocal active
        while not pending.empty():
            c=pending.get(); kind=c.get('kind')
            if kind=='quit': root.destroy(); return
            if kind=='session':
                active=bool(c.get('name')); label.config(text=('Conectado: '+c['name']) if active else 'SDE Suporte disponível')
                send_button.config(state='normal' if active else 'disabled')
                root.attributes('-topmost',active)
                if active: root.deiconify()
                else:
                    history.config(state='normal'); history.delete('1.0','end'); history.config(state='disabled')
            if kind in ('message','notice'):
                history.config(state='normal'); history.insert('end',c['name']+': '+c['text']+'\n'); history.see('end'); history.config(state='disabled')
                if kind=='notice':
                    image_label.config(image=''); image_label.image=None
                    if c.get('imageBase64'):
                        im=Image.open(io.BytesIO(base64.b64decode(c['imageBase64'],validate=True))); im.thumbnail((500,220))
                        photo=ImageTk.PhotoImage(im); image_label.config(image=photo); image_label.image=photo
                    root.deiconify()
        root.after(100,poll)
    root.after(100,poll); root.mainloop()


def main():
    command=sys.argv[1]
    if command=='monitors': print(json.dumps(monitors()))
    elif command=='layout': print(layout(monitors()))
    elif command=='validate-image':
        from PIL import Image
        value=json.load(sys.stdin)['imageBase64']
        data=base64.b64decode(value,validate=True)
        if len(data)>2*1024*1024: raise ValueError('Imagem muito grande')
        image=Image.open(io.BytesIO(data))
        if image.format not in ('PNG','JPEG') or image.width>4096 or image.height>4096: raise ValueError('Imagem inválida')
        image.verify()
    elif command=='input': apply_input(json.load(sys.stdin))
    elif command=='frame':
        from PIL import ImageGrab
        ms=monitors(); selected=next((m for m in ms if m['id']==sys.argv[2]),None)
        if sys.argv[2]!='all' and selected is None: raise ValueError('Monitor inexistente')
        image=ImageGrab.grab()
        if selected:
            x=selected['x']-min(m['x'] for m in ms); y=selected['y']-min(m['y'] for m in ms)
            image=image.crop((x,y,x+selected['width'],y+selected['height']))
        image.convert('RGB').save(sys.stdout.buffer,format='JPEG',quality=60)
    elif command=='consent':
        import tkinter as tk
        root=tk.Tk(); root.title('SDE Suporte — autorização'); root.attributes('-topmost',True); style_window(root)
        approved=False
        def yes():
            nonlocal approved
            approved=True; root.destroy()
        tk.Label(root,text=sys.argv[2]+' solicita visualizar e controlar este Raspberry.\nA solicitação expira em 30 segundos.',padx=20,pady=20).pack()
        tk.Button(root,text='Recusar',command=root.destroy).pack(fill='x')
        tk.Button(root,text='Autorizar',command=yes).pack(fill='x')
        root.after(30000,root.destroy); root.mainloop(); print('yes' if approved else 'no')
    elif command=='ui': ui()
    else: raise ValueError('Operação inválida')


if __name__=='__main__': main()
