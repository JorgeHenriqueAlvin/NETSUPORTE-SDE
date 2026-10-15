# SDE Suporte LAN — 0.4.0

Aplicativos Windows x64 para acesso remoto na rede local. Esta versão dispensa domínio, Active Directory, grupos corporativos e emissão manual de certificado. O cliente recebe atendimento e vê uma faixa com o nome aprovado do técnico e a hora de início; o console pertence ao técnico.

## Instalação em três passos

### 1. No computador do técnico

Execute `SDE-Suporte-Instalador-Windows-x64.exe`, escolha **Console do técnico** e abra o atalho no menu Iniciar. No primeiro uso, informe seu nome. Clique em **1. Exportar minha identificação** e salve o arquivo `.sdetech` num pendrive ou compartilhamento de TI. Esse arquivo contém sua chave pública; a chave privada fica no Windows da sua conta e não é exportada.

### 2. No computador do funcionário

Use o mesmo instalador, escolha **Agente do cliente** e mantenha marcada a configuração ao concluir. Informe o setor e selecione o arquivo `.sdetech` de cada técnico autorizado. A lista vale somente para essa máquina, permitindo organizar permissões por setor. A configuração cria o certificado local automaticamente, protege a política e configura o firewall. Salve o arquivo de computador `.sdehost` quando solicitado.

**LocalSubnet** libera a conexão somente a partir da sub-rede local. Se os técnicos estiverem em outras VLANs, informe as redes IPv4 reais no campo de redes, separadas por vírgula. O firewall só é liberado nos perfis Privado e Domínio; uma rede classificada como Pública exige configuração da TI.

Por padrão, técnicos previamente autorizados conectam sem confirmação a cada sessão. Para exigir confirmação, marque essa opção na configuração. A identificação do atendimento permanece visível nos dois modos.

Após configurar, saia e entre no Windows do funcionário para iniciar o agente sem elevação. Alternativamente, abra **Iniciar agente** pelo menu Iniciar na sessão normal do funcionário. O ícone fica na bandeja, sem console administrativo. Se faltar configuração, aparece uma explicação com botão para configurar, em vez da exceção de arquivo ausente.

### 3. De volta ao técnico

Clique em **2. Importar computador**, selecione o `.sdehost` gerado na máquina atendida, selecione o computador e clique em **Conectar**. O arquivo inclui o IP local e a identidade do certificado dessa máquina. Use **Alterar IP** se o DHCP mudar o endereço; a identidade do certificado continua sendo conferida. Não é necessário editar JSON nem instalar certificados no computador do técnico.

O console permite pesquisa por computador, setor ou IP, clique e arraste, rolagem, teclado quando a tela remota tem foco, envio de texto e encerramento do atendimento. O botão Verificar conexão testa o agente sem iniciar transmissão de tela.

## Identificação e autorização

Sem domínio, o nome exibido é o nome da identificação que a TI aprovou no agente. Não é consultado no login corporativo. Uma requisição não pode trocar esse nome: o agente obtém o nome da sua própria lista de técnicos autorizados. A chave privada identifica criptograficamente o técnico; o nome sozinho não libera acesso.

Cada requisição é assinada e vinculada à máquina, ao caminho, ao conteúdo e à sessão. Datas e nonces impedem reutilização de requisições. O console só aceita o certificado cujo SHA-256 consta no arquivo de computador importado; não há opção para aceitar qualquer certificado. Não copie identificações de técnico entre contas, nem importe arquivos de computador de origem desconhecida.

Para acrescentar ou retirar técnicos, execute **Configurar agente** como administrador e selecione a lista completa desejada. A reconfiguração encerra o agente, cria nova identidade de conexão e exige reimportar o `.sdehost` no console. O programa não adiciona contas ao Windows e não altera o domínio.

## Limites e implantação

Esta é uma implementação própria, sem promessa de equivalência completa ao NetSupport. Precisa de funcionário conectado e área de trabalho desbloqueada. Não opera antes do login, não acessa desktop seguro/UAC e não é serviço Windows. O agente pode ser encerrado pelo Gerenciador de Tarefas da conta local. Programas executados como administrador podem recusar comandos de um agente sem elevação.

A sessão termina ao bloquear a estação, após 20 segundos sem requisições válidas ou após uma hora. Ao encerrar, as teclas/botões mantidos pela sessão são liberados. Não existe gravação de tela, chat, transferência de arquivos, impressão remota, inventário completo ou gerenciamento central. A transmissão atual é JPEG de tela, sem codec de vídeo. O cadastro suporta uma lista de máquinas, mas 400 sessões simultâneas não foram testadas. Comece em duas máquinas reais antes de ampliar.

A rede deve permitir TCP 7443 entre técnico e agente. O certificado é autoassinado, não exportável, válido por três anos e validado por sua identidade previamente importada. Não exige confiança da CA corporativa. Não encaminhe essa porta para a internet.

Logs locais de início/fim/erro: `%LOCALAPPDATA%\SdeSuporte\logs\audit.jsonl`. Não são uma auditoria central inviolável. Não registram o texto digitado. Lista e identificação do técnico ficam em `%LOCALAPPDATA%\SdeSuporte` da conta do console. Política do agente fica em `C:\Program Files\SDE-Suporte\Agent\agent.json`, protegida contra alteração por usuário comum.

## Compilar e testar

O GitHub Actions compila os dois aplicativos .NET 10, testa o protocolo e executa um atendimento local entre processos Windows, incluindo captura e digitação num aplicativo de teste. Também gera o instalador e testa as seleções de técnico e agente, inclusive abrir o agente sem configuração. Confira o resultado real na aba Actions; aprovação no CI não comprova a conectividade/firewall entre duas máquinas da sua empresa.

No Windows com SDK .NET 10: `scripts\Build.ps1`. Os testes de integração usam uma porta de teste e certificado temporário, exigem administrador e um desktop Windows interativo: `scripts\Test-Windows.ps1`.

Para uso normal baixe o instalador na pasta **release** do artefato de uma execução bem-sucedida no GitHub. Instalador e executáveis ainda não possuem assinatura Authenticode.

## Remoção

Desinstale por Aplicativos do Windows ou pelo menu Iniciar. A limpeza remove o vínculo e certificado criados pelo agente, a reserva da porta, a regra de firewall e a inicialização automática. Identificação privada do técnico e logs da conta são preservados; uma reinstalação pode reutilizar a identificação do técnico. Arquivos `.sdehost` exportados não são apagados dos locais escolhidos.

Autoria do projeto: Jorge Henrique — CMTIC/SDE. Projeto assistido por IA; validar em máquinas reais antes da distribuição ampla.
