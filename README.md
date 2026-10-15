# SDE Suporte LAN — 0.5.0

Aplicativos Windows x64 para acesso remoto na rede local. Esta versão dispensa domínio, Active Directory, grupos corporativos e emissão manual de certificado. O cliente recebe atendimento e vê uma faixa com o nome aprovado do técnico e a hora de início; o console pertence ao técnico.

## Instalação automática

1. Instale a versão 0.5 do console pelo instalador geral, escolhendo Console do técnico.
2. Abra o console e clique em **Gerar instalador do cliente**. Informe o setor, inclua outros técnicos se necessário e salve o executável. Você faz isso uma vez por setor/equipe.
3. Execute o instalador gerado nos computadores dos funcionários. Confirme a instalação como administrador. O agente inicia na sessão normal do funcionário.
4. Com o console aberto, os computadores aparecem automaticamente como **Conectado**. Selecione um deles e clique em **Conectar** para atender.

Não é preciso exportar um arquivo de cada computador, digitar IP nem configurar certificados. O funcionário vê o nome do técnico e a hora do atendimento, sem painel administrativo.

Para outros técnicos incluídos no instalador: cada um importa o arquivo `.sdeteam` gerado ao lado do instalador uma única vez no seu console. Depois a descoberta das máquinas autorizadas é automática. O nome/identidade de cada técnico permanece individual.

A descoberta usa UDP 7445/7446. Na mesma sub-rede, o console procura por broadcast. Os agentes também anunciam sua presença nos endereços dos técnicos registrados no instalador. Outras VLANs precisam permitir essas portas e a conexão TCP 7443. Reserva de DHCP/IP ou nome DNS estável para os computadores dos técnicos facilita a descoberta entre VLANs.

O executável gerado autoriza cadastrar máquinas nessa equipe; distribua-o pela TI. Ele não contém a chave privada de controle remoto do técnico. Apenas instalar um agente genérico não autoriza qualquer pessoa da rede a acessá-lo.

Esta versão exige funcionário conectado ao Windows e tela desbloqueada. Os testes automatizados não substituem um teste entre duas máquinas da empresa.

## Descoberta e cadastro

O console mostra presença online após verificar o agente por HTTPS com a identidade de certificado cadastrada. Computadores que deixam de responder ficam como Desconectado após aproximadamente 25 segundos. A descoberta não inicia uma sessão de tela sozinha: o técnico seleciona a máquina e inicia o atendimento.

O instalador personalizado contém uma autorização de cadastro assinada pelo técnico que o preparou e uma chave limitada a cadastrar computadores para a lista de técnicos/um setor. O agente recebe uma credencial de cadastro vinculada ao seu certificado. Anúncios adulterados ou emitidos por equipes não confiáveis são recusados; a chave privada de controle remoto dos técnicos não vai para o instalador. O console pode gerar esse executável sem SDK, PowerShell extra ou compilador instalado no computador do técnico.

Na instalação normal, o agente inicia depois da elevação com a conta original do funcionário. Também inicia no próximo login. Sua configuração e certificados são gerados automaticamente. A opção manual do instalador geral continua disponível para configuração avançada e compatibilidade com a versão anterior.

## Identificação e autorização

Sem domínio, o nome exibido é o nome da identificação que a TI aprovou no agente. Não é consultado no login corporativo. Uma requisição não pode trocar esse nome: o agente obtém o nome da sua própria lista de técnicos autorizados. A chave privada identifica criptograficamente o técnico; o nome sozinho não libera acesso.

Cada requisição é assinada e vinculada à máquina, ao caminho, ao conteúdo e à sessão. Datas e nonces impedem reutilização de requisições. O console só aceita o certificado cujo SHA-256 consta no arquivo de computador importado; não há opção para aceitar qualquer certificado. Não copie identificações de técnico entre contas, nem importe arquivos de computador de origem desconhecida.

Para mudar os técnicos do cadastro automático, gere e distribua um novo instalador para a equipe desejada. A reinstalação preserva o identificador da máquina e atualiza a credencial/certificado. O console redescobre e verifica o computador automaticamente. O programa não adiciona contas ao Windows e não altera o domínio.

## Limites e implantação

Esta é uma implementação própria, sem promessa de equivalência completa ao NetSupport. Precisa de funcionário conectado e área de trabalho desbloqueada. Não opera antes do login, não acessa desktop seguro/UAC e não é serviço Windows. O agente pode ser encerrado pelo Gerenciador de Tarefas da conta local. Programas executados como administrador podem recusar comandos de um agente sem elevação.

A sessão termina ao bloquear a estação, após 20 segundos sem requisições válidas ou após uma hora. Ao encerrar, as teclas/botões mantidos pela sessão são liberados. Não existe gravação de tela, chat, transferência de arquivos, impressão remota, inventário completo ou gerenciamento central. A transmissão atual é JPEG de tela, sem codec de vídeo. O cadastro suporta uma lista de máquinas, mas 400 sessões simultâneas não foram testadas. Comece em duas máquinas reais antes de ampliar.

A rede deve permitir TCP 7443 entre técnico e agente. O certificado é autoassinado, não exportável, válido por três anos e validado por sua identidade previamente importada. Não exige confiança da CA corporativa. Não encaminhe essa porta para a internet.

Logs locais de início/fim/erro: `%LOCALAPPDATA%\SdeSuporte\logs\audit.jsonl`. Não são uma auditoria central inviolável. Não registram o texto digitado. Lista e identificação do técnico ficam em `%LOCALAPPDATA%\SdeSuporte` da conta do console. Política do agente fica em `C:\Program Files\SDE-Suporte\Agent\agent.json`, protegida contra alteração por usuário comum.

## Compilar e testar

O GitHub Actions compila o console, o agente e o instalador personalizado .NET 10. Testa o protocolo, a autorização de cadastro, a descoberta por UDP sem importar o arquivo da máquina e um atendimento entre processos Windows, incluindo captura e digitação num aplicativo de teste. Também gera o instalador e testa as seleções de técnico e agente, inclusive abrir o agente sem configuração. Confira o resultado real na aba Actions; aprovação no CI não comprova a conectividade/firewall entre duas máquinas da sua empresa.

No Windows com SDK .NET 10: `scripts\Build.ps1`, seguido de `scripts\Build-Template.ps1`. Os testes de integração usam uma porta de teste e certificado temporário, exigem administrador e um desktop Windows interativo: `scripts\Test-Windows.ps1`.

Para uso normal baixe o instalador na pasta **release** do artefato de uma execução bem-sucedida no GitHub. Instalador e executáveis ainda não possuem assinatura Authenticode.

## Remoção

O cliente personalizado registra **SDE Suporte - Cliente** em Aplicativos do Windows. O instalador geral continua com a entrada SDE Suporte. Use a remoção correspondente. A limpeza remove o vínculo e certificado criados pelo agente, a reserva da porta, a regra de firewall e a inicialização automática. Identificação privada do técnico e logs da conta são preservados; uma reinstalação pode reutilizar a identificação do técnico. Arquivos `.sdehost` exportados não são apagados dos locais escolhidos.

Autoria do projeto: Jorge Henrique — CMTIC/SDE. Projeto assistido por IA; validar em máquinas reais antes da distribuição ampla.
