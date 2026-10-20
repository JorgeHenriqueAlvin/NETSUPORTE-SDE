# Cliente SDE Suporte 0.12.19 para Raspberry Pi 4

Alvo: **Raspberry Pi OS com desktop, 64 bits (ARM64), sessão X11**.
Este projeto adiciona o papel **Cliente**: um técnico no Windows encontra e atende o Raspberry na rede interna usando o console existente. O console Técnico continua sendo Windows.

## Recursos implementados

- Identidade persistente, certificado HTTPS próprio e cadastro assinado da equipe.
- Descoberta UDP e `/discover` compatíveis com o console Windows.
- Requisições assinadas, proteção contra repetição e sessão vinculada ao técnico.
- Consentimento local obrigatório: recusa ou ausência de resposta não concede acesso.
- Captura JPEG, escolha de monitor, mouse, teclado e digitação pelo X11.
- Chat bidirecional durante a sessão e avisos com imagens PNG/JPEG.
- Nome do técnico visível, encerramento por inatividade, bloqueio da sessão ou indisponibilidade do desktop.
- Logs locais de eventos, sem registrar o conteúdo digitado ou das conversas.
- Instalação por usuário e início automático após login no desktop.

## Preparar o Raspberry

1. Use Raspberry Pi OS **com desktop**, 64 bits. Lite não oferece a tela necessária.
2. Se estiver em Wayland, abra `sudo raspi-config`, procure a opção de Wayland/X11 em Advanced Options e selecione X11. Reinicie e entre no desktop. O instalador verifica `XDG_SESSION_TYPE=x11`; não troca a interface automaticamente.
3. Baixe o pacote ARM64 na release `raspberry-stable` (ou o artefato do workflow Raspberry Pi Client). Verifique o SHA256 fornecido e extraia o `.tar.gz` em uma pasta.
4. Abra o terminal nessa pasta e execute:

```bash
bash install.sh
```

A instalação precisa de internet para dependências via apt e solicita sudo apenas nessa etapa. Não execute o programa como root.
O cadastro usa a mesma equipe embutida no Cliente Windows. O técnico precisa do seu console já autorizado.

O Cliente inicia a cada login. Para abrir manualmente:

```bash
~/.local/bin/sde-suporte-cliente
```

A rede deve permitir TCP 7443 e UDP 7446 entre a TI e o Raspberry. O programa não altera firewall ou roteador.

## Logs e remoção

Os arquivos ficam em `~/.local/share/SdeSuporte/`:

- `audit.jsonl`: eventos de atendimento.
- `startup.log`: erros de inicialização.
- `agent.json` e `agent.pfx`: identidade e certificado; não compartilhar o PFX.

Para remover:

```bash
bash ~/.local/lib/sde-suporte/uninstall.sh
```

Identidade e logs são preservados. Reinstalar o novo pacote atualiza os binários sem recriar a identidade.

## Compilar e verificar

Em um ambiente com .NET SDK 10:

```bash
dotnet build linux/SdeSuporte.Linux.csproj
python3 -m unittest discover -s linux/tests -p 'test_*.py'
dotnet publish linux/SdeSuporte.Linux.csproj -c Release -r linux-arm64 --self-contained true -o release/raspberry
cp linux/{desktop.py,install.sh,uninstall.sh,README.md} release/raspberry/
```

No desktop do Raspberry, `SDE-Suporte-Cliente-Linux --check` valida o acesso aos monitores e cria/preserva a identidade local.

## Limites desta prévia

A compilação e os testes automatizados não substituem um piloto em Raspberry Pi físico. Não há validação física de desempenho, layout de teclado ABNT2 ou dois monitores neste ambiente. Alguns atalhos especiais podem exigir ajustes de mapeamento; a opção de enviar texto aceita Unicode.

Wayland, tela de login, desktop bloqueado, implantação WinRM/SMB em Linux, console Técnico Linux e acesso pela internet sem roteamento não estão implementados. Após o bootstrap manual da versão 0.12.19, o supervisor reinicia após falha e consulta atualizações verificadas a cada hora, aplicando somente depois de o agente encerrar ocioso, sem atendimento. Use `bash ~/.local/lib/sde-suporte/diagnose.sh` para diagnóstico. Consulte o guia de operação correspondente no repositório. O Cliente não muda o modo quiosque do Chromium, a configuração da TV ou o fullscreen do Painel TV.

A verificação de bloqueio depende de `loginctl` com `Active=yes` e `LockedHint=no`. Se o desktop não informa esses estados, o acesso é recusado; não remova essa verificação. A sessão termina após 20 segundos sem atividade do console e libera teclas/botões mantidos.

Referência do Raspberry Pi para selecionar X11: https://www.raspberrypi.com/documentation/computers/configuration.html

## Adicionar manualmente no Técnico Windows

No Técnico atualizado, clique em **Adicionar Raspberry/Linux**. Digite o IP mostrado por `hostname -I` e clique em **Testar conexão**. Após verificar a equipe, o certificado e a autorização, o nome real da máquina aparece. Clique em **Adicionar à lista** e depois em **Conectar**. O cadastro é salvo em `computers.json` e reaparece após abrir o Técnico novamente, sem duplicar o mesmo identificador.

O teste distingue IP inválido, endereço inacessível, porta recusada, tempo esgotado, falha HTTPS, cadastro de outra equipe e credencial não autorizada. Não configura firewall ou roteador. A tela bloqueada pode ser cadastrada, mas o controle exige o desktop disponível e a autorização local.
