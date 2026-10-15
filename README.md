# SDE Suporte 0.6 — rede local Windows

Suporte remoto interno com dois instaladores: **Técnico** e **Cliente**. Não exige Active Directory, domínio, servidor externo ou exportação de identificações entre computadores.

## Instalação para uso diário

1. Nos computadores da equipe de TI, execute **SDE-Suporte-Tecnico-Instalar.exe**.
2. Na primeira abertura do console, informe o nome do técnico e cole o **código privado da equipe**, entregue separadamente pelo administrador. O mesmo código serve para os técnicos autorizados; ele é informado uma vez por conta do Windows.
3. Nos computadores dos funcionários, execute **SDE-Suporte-Cliente-Instalar.exe**. A configuração é automática. O código privado não é necessário no cliente.
4. Deixe o console aberto. As máquinas da equipe aparecem na lista; selecione uma e clique em **Conectar**.

O cliente inicia ao concluir uma instalação interativa e nos próximos logins do Windows. Ele apresenta um ícone de disponibilidade e, durante o atendimento, um aviso com **nome do técnico e horário de início**. O funcionário não recebe o console de administração.

**Não é necessário gerar instaladores no console, importar arquivos de equipe ou reinstalar os clientes para adicionar outro técnico.** Para adicionar um técnico, instale o console e informe o código da mesma equipe.

Computadores com a versão 0.5 ou anterior precisam receber o novo instalador Cliente uma vez para migrar da lista individual para a autorização da equipe.

## Descoberta e varredura de IP

- O console envia descoberta UDP autenticada a cada cinco segundos.
- Ao abrir, também pesquisa os IPs das redes privadas locais pela porta **TCP 7443**, sem depender de ping ou broadcast.
- A busca inicial usa a máscara da interface até o limite de **/22 por interface**. Por exemplo, uma interface /23 pesquisa seus 510 endereços de host.
- Para pesquisar uma rede maior, outra VLAN ou uma faixa específica, clique em **Varredura de IP**. Exemplos: `10.37.2.0/23`, `10.37.2.1-10.37.3.254`. Separe várias faixas com ponto e vírgula.
- A faixa escolhida é lembrada neste console e repetida a cada dois minutos. O console verifica a presença dos agentes já conhecidos durante o uso.
- Há progresso, filtro por nome/setor/IP e botão **Parar varredura**.
- Cada pesquisa admite até 65.536 endereços privados e 32 consultas simultâneas.
- A lista reconhece **agentes SDE da equipe instalados**, não qualquer aparelho da rede. A varredura não instala agentes remotamente.
- Não há teste de capacidade com 400 computadores reais ou sessões simultâneas em grande escala.

Entre VLANs, o roteamento e os equipamentos de rede precisam permitir TCP 7443 entre técnicos e clientes. A varredura HTTPS funciona mesmo se os broadcasts UDP forem bloqueados. Para descoberta UDP, permita UDP 7446 no cliente e UDP 7445 no console. Os instaladores criam as regras locais de firewall para perfis Privado/Domínio e endereços internos.

## Autorização e identificação

Cada conta de técnico mantém sua própria chave ECDSA P-256 não exportável no armazenamento CNG do Windows. O código da equipe emite uma credencial que vincula essa chave ao nome informado. O console guarda apenas a credencial pública; não guarda o código da equipe.

O agente confia na autoridade pública da equipe distribuída pelo instalador. Assim, um técnico novo pode conectar com sua própria chave e credencial sem alterar a configuração de cada cliente. Toda operação de suporte exige assinatura, horário válido e nonce não reutilizado. As sessões vinculam o token ao técnico e ao computador.

A descoberta HTTPS transporta apenas identificação pública assinada. Antes de incluir uma máquina, o console confere a autoridade da equipe e compara o certificado apresentado na conexão com o certificado assinado no cadastro do agente. A verificação de presença e todas as operações posteriores usam HTTPS com pinagem estrita.

**O código privado da equipe não está no código-fonte nem nos instaladores.** Entregue-o somente aos técnicos autorizados. Quem possui o código pode autorizar um console; nomes são declarados pelos integrantes da equipe. A credencial individual tem validade de cinco anos. Para retirar o acesso de alguém que conhece o código compartilhado, é necessário rotacionar a autoridade e redistribuir a configuração da equipe; não existe revogação central individual nesta versão.

`src/TeamDeployment.json` contém a autoridade **pública** e uma delegação limitada a cadastrar agentes. A chave dessa delegação permite instalar novos clientes, mas não assinar credenciais de técnicos nem controlar máquinas. A autorização pública dos instaladores desta equipe vence em outubro de 2036.

## Funções e limites atuais

Implementado: lista automática, varredura por IP, filtro, estado de conexão, visualização da área de trabalho, mouse, arraste, roda, teclado, envio de texto, aviso de técnico e logs locais de início/fim/erros. Existe uma sessão por computador, com expiração e encerramento ao bloquear ou desconectar a área de trabalho.

O agente funciona **na sessão do funcionário**. É necessário estar conectado ao Windows e com a área de trabalho desbloqueada. Não controla login, tela bloqueada ou a área protegida do UAC; aplicações elevadas podem recusar entrada de um agente sem elevação. Não é um serviço do Windows. Um usuário local pode encerrá-lo pelo Gerenciador de Tarefas.

Ainda não há transferência de arquivos, chat, inventário completo, impressão remota, gravação de sessão ou auditoria centralizada. Não há equivalência integral ao NetSupport nem assinatura Authenticode dos instaladores.

## Compilar e verificar

Em Windows x64 com .NET SDK 10 e Inno Setup 6:

```powershell
./scripts/Build.ps1
./scripts/Build-Template.ps1
./scripts/Test-Windows.ps1
& "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe" /DSetupRole=technician installer/SDE-Suporte.iss
& "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe" /DSetupRole=client installer/SDE-Suporte.iss
```

O workflow Windows compila aplicativos autocontidos, verifica assinaturas e rejeição de acessos indevidos, usa captura e entrada reais, encontra o agente por UDP e IP e conecta seis identidades novas ao mesmo cliente sem reinstalá-lo. Também testa instalação/remoção dos dois EXEs e atualização de um cliente em execução, preservando sua identidade. As credenciais dos testes são descartáveis e não entram no artefato publicado.

O pacote para download contém somente os dois instaladores e estas instruções.
