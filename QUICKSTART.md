# Começar com a versão 0.12.14

1. Atualize Técnico e Clientes Windows uma primeira vez; preserve as configurações existentes.
2. Para Raspberry, baixe o pacote ARM64 da release `raspberry-stable` e execute `install.sh` no desktop X11 como usuário normal.
3. No Técnico, abra **Estado e logs** para conferir versões, atualizações, atendimento, eventos e diagnóstico.
4. Novos Clientes Windows atualizam ociosos; feche o Técnico para atualizar o console. Raspberry consulta seu canal a cada hora e aplica sem atendimento.
5. Leia [o guia da versão](docs/OPERACAO-0.12.14.md) antes do piloto. Instalar o agente não cria conectividade entre redes externas.

## Instruções gerais e histórico

## Instalar e conectar

## Nos computadores dos técnicos

1. Use **SDE-Suporte-Tecnico-Autorizado.exe**, entregue privadamente à equipe de TI.
2. Execute o instalador normalmente e conclua a instalação.
3. O console abre automaticamente, já autorizado, com a identificação do usuário do Windows.
4. As máquinas com o agente da equipe aparecem na lista. Selecione uma e clique em **Conectar**.

Não é necessário digitar código, importar identificações ou preparar outro instalador de cliente. Use esse mesmo instalador autorizado nos cinco computadores dos técnicos.

O nome exibido ao funcionário é o nome do perfil de técnico existente ou, em uma instalação nova, o usuário do Windows. Nos próximos acessos, abra o atalho **SDE Suporte - Técnico**.

## Nos computadores dos funcionários

1. Execute **SDE-Suporte-Cliente-Instalar.exe** com permissão de administrador.
2. Conclua a instalação. A configuração é automática.
3. O agente inicia após a instalação interativa e nos próximos logins do Windows.

Para usar chat e avisos, atualize também os Clientes para **0.7.0**, executando o novo instalador por cima da instalação existente. O atendimento básico continua compatível com Clientes 0.6, mas as novas abas exigem Cliente atualizado.

O início automático vale para todas as contas e ocorre **ao entrar no Windows após ligar/reiniciar a máquina**. Não exige abrir o aplicativo manualmente. Antes do login, a área de trabalho do funcionário ainda não existe; esta versão não é um serviço de acesso à tela de login.

## Tela cheia, chat e avisos

- Ao conectar, a tela remota abre em tela cheia. Use **F11** ou **Sair da tela cheia** para voltar. Desmarque **Abrir em tela cheia** para conectar em janela.
- Na aba **Chat do atendimento**, envie mensagens ao funcionário. Ele responde pela janela de mensagens do Cliente, sem acesso ao console de técnicos. O ícone do Cliente permite reabrir o chat do atendimento ativo.
- Na aba **Avisos globais**, escreva o comunicado e clique em **Enviar aviso aos clientes online**. Confira a prévia e confirme o envio. Todos os Clientes online conhecidos pelo console recebem, inclusive os que ficaram fora do filtro de pesquisa.
- O resultado informa cada máquina que recebeu, falhou ou precisa de atualização. Entrega ao agente não comprova leitura. Máquinas desligadas ou ainda não descobertas não recebem aviso; não há fila central.
- O aviso mostra o técnico remetente e não inicia controle remoto. Cada conversa fica restrita ao atendimento atual e é encerrada com ele. São aceitos textos de até 2.000 caracteres.

## Buscar outra rede ou faixa

A lista usa descoberta automática e varredura de IP. Clique em **Varredura de IP** para informar outra rede ou uma faixa, como `10.37.2.0/23` ou `10.37.2.1-10.37.3.254`. O botão **Parar varredura** interrompe a busca.

A busca inicial usa até /22 por interface. São aceitos até 65.536 IPs privados por pesquisa. Entre VLANs, permita TCP 7443 entre técnicos e clientes. A busca não depende de ping.

Para atendimento, o funcionário deve estar conectado e com o Windows desbloqueado. O aviso de atendimento mostra quem está acessando.

## Distribuição à TI

O arquivo **Técnico-Autorizado** já concede acesso à equipe e deve ser distribuído somente aos técnicos. Ele é entregue por canal privado e não acompanha o instalador dos funcionários.

O arquivo **Técnico-Base**, disponível no repositório público, é um modelo para gerar o instalador autorizado; ele não contém a autorização privada. A versão anterior com código permanece como compatibilidade para instalações antigas.

## Encontrar máquinas sem Cliente e instalar pela rede

1. Atualize o Técnico com o instalador autorizado **0.8.0**. Ele já inclui o instalador Cliente para distribuição remota.
2. Abra **Implantar Clientes**. A busca começa automaticamente nas redes locais (até /22 por interface). Edite a faixa e clique em **Localizar máquinas** para ampliar.
3. A lista usa ICMP e portas TCP 445/5985/5986/7443; não exige Cliente instalado. Portas abertas não comprovam Windows e máquinas que bloqueiam todos esses testes podem não aparecer.
4. Confira IP e nome DNS. A coluna **Nome para autenticar** pode ser corrigida; o nome precisa corresponder ao IP selecionado.
5. Escolha **HTTPS (sem exigir domínio)** se as máquinas têm WinRM HTTPS com certificado confiável. Onde existir domínio, **Domínio (Kerberos)** usa WinRM e o nome DNS. A conta deve ter acesso administrativo efetivo ao endpoint remoto.
6. Marque as máquinas e clique em **Instalar Cliente nas selecionadas**. Confira a confirmação, informe a credencial na janela do PowerShell e acompanhe os resultados. Não envie senhas pelo chat.
7. O pacote é conferido por SHA256 antes e depois da cópia. A instalação é silenciosa, mantém o início automático e tenta iniciar o Cliente na conta já conectada, sem guardar a senha dessa conta.
8. Se não há usuário conectado ou o início interativo falha, aparece **Instalado; aguardando login**. No próximo login, o Cliente inicia automaticamente. Clique em **Atualizar máquinas** para o agente aparecer na lista de suporte.

A descoberta funciona sem agente, mas a instalação sem agente **exige o gerenciamento remoto do Windows previamente autorizado**. Esta função não habilita WinRM por conta própria, não reduz UAC, não pula validação de certificados nem usa credenciais sem autorização. Nas redes sem WinRM, a TI precisa preparar o gerenciamento, distribuir via GPO/ferramenta de gestão ou instalar o primeiro Cliente localmente.

O relatório de cada lote fica em `%LOCALAPPDATA%\SdeSuporte\deployments`. Não guarda senhas. O teste Windows valida descoberta sem agente, integridade e execução do instalador administrativo; autenticação WinRM e políticas reais da empresa dependem do ambiente e precisam de um piloto.


## Versão 0.9.0
Avisos globais exibem somente Aviso da TI. O nome do técnico continua no atendimento remoto e no registro de auditoria. Interface renovada e captura com envio independente dos comandos de entrada.

### Atualizações automáticas
Na versão atual, a tarefa Windows (SYSTEM) consulta o GitHub a cada cinco minutos e ao iniciar o Windows, verifica SHA256 e atualiza Clientes novos quando ociosos. O console precisa estar fechado; Clientes antigos ainda exigem fechamento ou atualização manual inicial. Não interrompe atendimentos. A instalação mantém a identidade da máquina e a autorização do técnico. Exige acesso HTTPS ao GitHub e seus servidores de arquivos; sem internet, o aplicativo continua funcionando na rede local e tenta atualizar depois. O manifesto e os hashes são obtidos do repositório oficial por HTTPS; os executáveis ainda não têm assinatura Authenticode. Logs: pasta UpdateCache ao lado de Agent/Technician.


## Versão 0.10.0 — vários monitores
Ao conectar, o console detecta os monitores do funcionário e abre o principal. Use Monitor do cliente para alternar para Monitor 1, Monitor 2 ou Todos os monitores, inclusive em tela cheia. A lista acompanha monitores conectados ou removidos durante o atendimento. Se o monitor selecionado for removido, volta ao principal. O aviso com o nome do técnico aparece no monitor selecionado. Em Todos os monitores, o aviso permanece no último monitor selecionado. Instale a nova versão de Cliente e Técnico para habilitar a função; a captura de todos os monitores continua compatível com consoles anteriores.


## Versão 0.11.0
Cada atendimento pede confirmação ao funcionário e mostra o nome do técnico. O prazo de resposta é 30 segundos. Recusa ou ausência de resposta bloqueia o atendimento, sem bypass. Solicitações, respostas, acessos e encerramentos ficam no audit.jsonl em %LOCALAPPDATA%\SdeSuporte\logs do funcionário. A TI pode preautorizar uma máquina mediante configuração administrativa local e justificativa usando scripts/Configure-AccessPolicy.ps1; essa política vale para solicitações futuras e não transforma uma recusa em autorização. A política pode ser restaurada para Confirmacao pelo mesmo script. Registro da política: Agent/access-policy-audit.jsonl. São registros locais, não um servidor central imutável.

Avisos globais aceitam JPEG/PNG, normalizados até 1.600 pixels, com legenda opcional. Selecione clientes individualmente ou use Selecionar todos/Limpar seleção. Somente os marcados e online recebem. A confirmação informa o total e se há foto. O quadro do funcionário guarda até oito avisos nesta sessão do Windows; o histórico não é persistido após encerrar o agente. Fotos não são escritas nos logs; o registro contém o hash da imagem e a identidade autenticada do técnico. O remetente continua aparecendo ao funcionário somente como Aviso da TI.


## Buscar Raspberry Pi sem saber o IP (Técnico 0.12.7)

1. No Técnico Windows, clique em **Buscar Raspberry Pi**. A busca começa automaticamente.
2. Aguarde a varredura e a identificação dos agentes Linux da equipe.
3. Selecione o nome/IP encontrado e clique em **Adicionar selecionado**.
4. Na lista principal, clique em **Conectar** para solicitar atendimento.

A janela permite parar ou repetir a busca e mostra a disponibilidade do desktop.
O Raspberry deve estar ligado e com o agente SDE iniciado; não é necessário atualizar
o agente Linux para esta busca. A identificação Linux não confirma o modelo físico.
São pesquisadas redes privadas ativas, até /22 por interface e 65.536 IPs no total,
com 32 consultas simultâneas. Redes maiores ou outras VLANs podem exigir a opção
**Varredura de IP** e liberação de roteamento/firewall.


## Configurações da máquina (0.12.8)

Selecione uma máquina e clique em **Configurações da máquina**. A consulta não
inicia controle remoto. Mostra nome/host, sistema operacional (incluindo Windows
10/11/Server quando informado pelo sistema), arquitetura, processador e CPUs
lógicas, RAM total/disponível, discos e espaço livre, IP/MAC, tempo ligado e
fabricante/modelo/placa-mãe/BIOS quando disponíveis. Atualizar informações faz
uma nova consulta. A RAM e o espaço livre são valores do momento.

É necessário atualizar também o Cliente Windows para 0.12.8. No Raspberry,
instale o novo pacote ARM64 do workflow Raspberry Pi Client; a atualização Linux
passa a ser automática após instalar 0.12.14 pelo procedimento inicial. Agentes anteriores permanecem compatíveis com o atendimento,
mas não fornecem inventário. Nome de host não confirma função de servidor:
o tipo de sistema Windows é mostrado separadamente. Sufixo DNS não comprova
vínculo com Active Directory. A consulta exige credencial de técnico autorizada,
certificado conferido e gera registro no log do agente.
