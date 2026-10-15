# SDE Suporte 0.7.0 — tela cheia, chat e avisos globais

Suporte remoto interno com dois instaladores: **Técnico** e **Cliente**. Não exige Active Directory, domínio, servidor externo ou exportação de identificações entre computadores.

## Instalação para uso diário

1. Nos computadores da TI, execute **SDE-Suporte-Tecnico-Autorizado.exe**, entregue privadamente à equipe.
2. Conclua a instalação interativa. O console abre já autorizado, usando o nome do técnico existente ou o usuário do Windows.
3. Nos funcionários, execute **SDE-Suporte-Cliente-Instalar.exe**. A configuração é automática.
4. No console, selecione a máquina encontrada e clique em **Conectar**.

**Não há código para digitar, troca de arquivos de identificação nem geração de instaladores pelo técnico.** O mesmo instalador autorizado serve para os computadores da equipe.

A autorização é criada na conta Windows que iniciou a instalação, usando a chave pessoal não exportável dessa conta. Nos próximos acessos, o atalho abre normalmente. O instalador não precisa permanecer no computador depois da primeira abertura bem-sucedida do console.

Atualize os dois instaladores para **0.7.0** para usar chat e avisos. Clientes 0.6 continuam compatíveis com o atendimento básico, mas o console indica quando é necessário atualizar para as novas funções.

Ao conectar, a visualização abre em tela cheia (opção desmarcável); **F11** ou o botão superior retorna à janela. Há abas **Tela remota**, **Chat do atendimento** e **Avisos globais**. O funcionário responde em uma janela própria de chat, sem receber o console de administração. Os avisos globais são enviados aos Clientes online conhecidos pelo console, com prévia/confirmacão e resultado por máquina; não existe fila central para máquinas desligadas ou não descobertas.

O Cliente inicia automaticamente ao entrar no Windows, por uma entrada HKLM Run para todas as contas. A instalação verifica essa entrada; atualizar o Cliente também a reaplica. Não há agente ativo antes do login.

O funcionário vê um ícone de disponibilidade e, durante o atendimento, o nome do técnico e o horário de início. Ele não recebe o console de administração.

O arquivo Técnico-Autorizado possui a autorização da equipe e é distribuído somente à TI por canal privado. O repositório público contém o código-fonte, o Cliente e o modelo **Técnico-Base**, sem autorização privada.

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

Cada conta de técnico mantém sua própria chave ECDSA P-256 não exportável no armazenamento CNG do Windows. O instalador autorizado emite automaticamente uma credencial que vincula essa chave ao usuário do Windows. O perfil instalado guarda apenas a credencial pública e a referência à chave pessoal; a autorização privada permanece somente no arquivo de instalação da TI.

O agente confia na autoridade pública da equipe distribuída pelo instalador. Assim, um técnico novo pode conectar com sua própria chave e credencial sem alterar a configuração de cada cliente. Toda operação de suporte exige assinatura, horário válido e nonce não reutilizado. As sessões vinculam o token ao técnico e ao computador.

A descoberta HTTPS transporta apenas identificação pública assinada. Antes de incluir uma máquina, o console confere a autoridade da equipe e compara o certificado apresentado na conexão com o certificado assinado no cadastro do agente. A verificação de presença e todas as operações posteriores usam HTTPS com pinagem estrita.

**Quem possui o instalador Técnico-Autorizado pode autorizar um console da equipe.** Distribua esse arquivo somente à TI. Ele não é publicado nos artefatos públicos nem nas versões públicas do GitHub. A credencial pessoal tem validade de cinco anos. Não há revogação central individual nesta versão; retirar o acesso de alguém que possui o instalador exige rotacionar a autoridade e redistribuir a configuração.

`src/TeamDeployment.json` contém a autoridade **pública** e uma delegação limitada a cadastrar agentes. A chave dessa delegação permite instalar novos clientes, mas não assinar credenciais de técnicos nem controlar máquinas. A autorização pública dos instaladores desta equipe vence em outubro de 2036.

## Funções e limites atuais

Implementado: lista automática, varredura por IP, filtro, estado de conexão, visualização da área de trabalho, mouse, arraste, roda, teclado, envio de texto, aviso de técnico, tela cheia, chat bidirecional, avisos globais autenticados e logs locais de início/fim/erros/recebimento de mensagens. Existe uma sessão por computador, com expiração e encerramento ao bloquear ou desconectar a área de trabalho.

O agente funciona **na sessão do funcionário**. É necessário estar conectado ao Windows e com a área de trabalho desbloqueada. Não controla login, tela bloqueada ou a área protegida do UAC; aplicações elevadas podem recusar entrada de um agente sem elevação. Não é um serviço do Windows. Um usuário local pode encerrá-lo pelo Gerenciador de Tarefas.

Ainda não há transferência de arquivos, inventário completo, impressão remota, gravação de sessão ou auditoria centralizada. Não há equivalência integral ao NetSupport nem assinatura Authenticode dos instaladores.

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

O pacote público contém o Cliente, o modelo Técnico-Base e as instruções. O instalador Técnico-Autorizado é gerado privadamente com scripts/Authorize-Technician-Installer.py e entregue apenas à TI. O script recebe caminhos de arquivos, verifica a autorização da equipe e não imprime os dados privados.

Os testes verificam o instalador personalizado no Windows, a autorização automática de seis perfis, a preservação da identidade ao reinstalar, a conexão real de um perfil provisionado automaticamente e a rejeição de instaladores adulterados ou de outra equipe.

## Protocolo de mensagens

Chat e avisos usam o mesmo HTTPS com pinagem e assinatura de cada requisição. `POST /announcement` aceita apenas técnico autorizado, sem iniciar sessão de controle, com identificador idempotente, limite de três segundos entre novos avisos por técnico e um único painel de avisos reutilizado. A confirmação é de recebimento pelo agente, não de leitura pelo funcionário. `GET/POST /chat` exige a sessão ativa vinculada ao técnico e ao dispositivo. Os nomes são obtidos da credencial verificada e do usuário Windows, nunca de um campo remetente enviado livremente.

As mensagens são texto simples de até 2.000 caracteres; o chat mantém até 200 mensagens em memória por atendimento e é limpo ao encerrar. O funcionário só pode responder durante o atendimento. O agente registra eventos de envio/recebimento no log, sem guardar o conteúdo do chat no arquivo de auditoria. Os avisos podem chegar a um agente em sessão bloqueada e ficar visíveis após desbloquear; isso não autoriza captura ou entrada na tela bloqueada.
