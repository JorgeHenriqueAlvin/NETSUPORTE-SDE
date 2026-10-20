## Descrição com sistema e RAM — console 0.12.12

A lista mostra o nome do computador cliente, o IP, o sistema operacional e a RAM total enviados pelo inventário autenticado do agente. O console consulta `/inventory` automaticamente após verificar a presença, para agentes que anunciam essa capacidade, e limita as consultas de hardware a uma a cada cinco minutos por máquina. Os dados ficam salvos para consulta posterior; o estado conectado continua dependendo da comunicação recente.

Se a consulta de inventário falhar, a presença confirmada por `/info` continua válida e os últimos dados conhecidos são preservados. Agentes antigos sem inventário mostram “RAM total: não informada”; atualize o Cliente para obter a memória. A descrição administrativa permanece disponível abaixo do sistema e da RAM. “Nome do cliente” nesta lista significa o nome do computador.

# SDE Suporte 0.8.0 — descoberta sem agente e implantação remota

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

## Identificação das máquinas — console 0.12.11

O console mostra o hostname informado pelo agente ou, enquanto a consulta estiver indisponível, o nome já salvo no cadastro da máquina. Agentes antigos sem o campo `hostname` também usam esse nome. O IP aparece em linha separada; falhas da consulta indicam problemas de conexão, tempo de resposta ou autorização. A identificação não depende de DNS reverso e não altera as credenciais dos agentes.

## Buscar agentes instalados — console 0.12.10

Clique em **Buscar agentes instalados** no Técnico. A busca também inicia ao abrir o console: envia descoberta UDP, verifica os endereços já salvos e pesquisa por HTTPS as redes locais (limite inicial /22 por interface) junto às faixas configuradas em **Varredura de IP**. Uma faixa salva não substitui mais as redes locais. A varredura continua limitada a 65.536 endereços por pesquisa.

A lista prioriza máquinas com comunicação recente, mostra contagens de conectados/salvos/exibidos e permite **Mostrar somente conectados**, combinado com a pesquisa por nome, descrição ou IP. Máquinas salvas sem resposta permanecem no histórico; não são tratadas como conectadas. A verificação de presença e a busca periódica continuam automáticas.

O agente precisa estar executando, pertencer à equipe autorizada e estar acessível pela rede. Instalação sem agente em execução, computador desligado ou firewall/roteamento bloqueado não produz resultado conectado. Para outras sub-redes, informe as faixas em **Varredura de IP**. O agente existente não precisa ser reinstalado para usar estes controles do console.

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
& "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe" /DSetupRole=client installer/SDE-Suporte.iss
(Get-FileHash release/SDE-Suporte-Cliente-Instalar.exe).Hash | Set-Content release/SDE-Suporte-Cliente-Instalar.exe.sha256
& "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe" /DSetupRole=technician installer/SDE-Suporte.iss
```

O workflow Windows compila aplicativos autocontidos, verifica assinaturas e rejeição de acessos indevidos, usa captura e entrada reais, encontra o agente por UDP e IP e conecta seis identidades novas ao mesmo cliente sem reinstalá-lo. Também testa instalação/remoção dos dois EXEs e atualização de um cliente em execução, preservando sua identidade. As credenciais dos testes são descartáveis e não entram no artefato publicado.

O pacote público contém o Cliente, o modelo Técnico-Base e as instruções. O instalador Técnico-Autorizado é gerado privadamente com scripts/Authorize-Technician-Installer.py e entregue apenas à TI. O script recebe caminhos de arquivos, verifica a autorização da equipe e não imprime os dados privados.

Os testes verificam o instalador personalizado no Windows, a autorização automática de seis perfis, a preservação da identidade ao reinstalar, a conexão real de um perfil provisionado automaticamente e a rejeição de instaladores adulterados ou de outra equipe.

## Protocolo de mensagens

Chat e avisos usam o mesmo HTTPS com pinagem e assinatura de cada requisição. `POST /announcement` aceita apenas técnico autorizado, sem iniciar sessão de controle, com identificador idempotente, limite de três segundos entre novos avisos por técnico e um único painel de avisos reutilizado. A confirmação é de recebimento pelo agente, não de leitura pelo funcionário. `GET/POST /chat` exige a sessão ativa vinculada ao técnico e ao dispositivo. Os nomes são obtidos da credencial verificada e do usuário Windows, nunca de um campo remetente enviado livremente.

As mensagens são texto simples de até 2.000 caracteres; o chat mantém até 200 mensagens em memória por atendimento e é limpo ao encerrar. O funcionário só pode responder durante o atendimento. O agente registra eventos de envio/recebimento no log, sem guardar o conteúdo do chat no arquivo de auditoria. Os avisos podem chegar a um agente em sessão bloqueada e ficar visíveis após desbloquear; isso não autoriza captura ou entrada na tela bloqueada.

## Implantação administrativa sem agente prévio

A aba **Implantar Clientes** separa inventário de rede e suporte remoto. Descobre hosts acessíveis usando ICMP e abertura TCP limitada a 445/5985/5986/7443, com 32 IPs simultâneos, até 65.536 endereços privados e cancelamento. Nomes sugeridos por DNS reverso são conferidos por resolução direta. Nenhuma credencial é usada na descoberta, e uma porta aberta não confirma o sistema operacional.

O técnico seleciona destinos e confirma o lote antes da instalação. O instalador Técnico contém uma cópia do instalador Cliente e seu SHA256, mantidos em Program Files. `Deploy-Agent.ps1` pede uma credencial administrativa localmente, mantém apenas o PSCredential em memória e abre sessões WinRM: HTTPS com validação normal do certificado, ou Kerberos para nomes do domínio. Não há fallback para Basic/CredSSP, `TrustedHosts`, desativação de certificados ou alteração de políticas UAC. O nome DNS precisa corresponder ao IP privado selecionado.

Após verificar a permissão administrativa remota, a cópia é feita por `Copy-Item -ToSession` para uma pasta temporária restrita a administradores/SYSTEM, sem compartilhar a senha ou depender de acesso SMB duplo. O payload `Install-RemoteAgent.ps1` confere novamente o hash, executa apenas o instalador Cliente incluído, valida o cadastro e a entrada de início automático e tenta iniciar na conta interativa existente usando uma tarefa agendada visível, sem senha de funcionário. Sem sessão interativa, aguarda login; nunca lança a GUI na sessão do WinRM. A desinstalação remove a tarefa pertencente ao agente.

O lote é sequencial, admite até 512 destinos e salva resultado por máquina, sem credenciais. O programa não configura previamente WinRM, certificados ou autorização remota do Windows. Não garante detectar hosts que bloqueiam todos os testes e não oferece instalação sem uma credencial administrativa válida. Os testes CI exercitam a descoberta sem agente, recusa de hash errado e o payload de instalação/removação no Windows. A autenticação WinRM na infraestrutura da empresa não foi testada; realize um piloto com máquinas autorizadas e gerenciamento preparado.
