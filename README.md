# SDE Suporte — desenvolvimento 0.3

Projeto Windows nativo para atendimento na rede interna, com autenticação do domínio e autorização por grupo/setor em cada máquina. Entrega em código-fonte; NÃO inclui executável compilado, homologação Windows ou teste de carga. Não instalar nas 400 máquinas antes do piloto.

## O que está implementado

- Dois executáveis com entradas separadas: `SDE-Suporte-Tecnico.exe` e `SDE-Suporte-Cliente.exe`. O executável cliente sempre inicia o agente; não oferece console por argumentos.
- Agente sem painel administrativo. Fica na bandeja quando ocioso e mostra aviso de atendimento com o login corporativo autenticado, setor e hora de início.
- Acesso sem confirmação configurável exclusivamente na política protegida pelo administrador, com `-AuthorizedUnattended` no instalador. Por padrão exige confirmação.
- HTTPS com certificado confiável e autenticação integrada Windows/Negotiate, usando a conta já conectada no console. Não solicita nem armazena senhas.
- Lista de computadores configurada por arquivo. Cada agente verifica o SID dos grupos permitidos, independentemente da lista do console.
- Confirmação do funcionário quando a política de atendimento exige autorização por sessão.
- Uma sessão por computador, vinculada à identidade autenticada, com token aleatório e duração máxima de 15 minutos.
- Tela principal em JPEG a aproximadamente 1 quadro por segundo, clique esquerdo/direito, digitação de texto e teclas Enter/Tab/Backspace/Escape.
- Aviso visível durante a sessão, sem botões de administração para o funcionário. Após desconexão, o aviso desaparece. Sem solicitações válidas por 20 segundos, a sessão expira. O console tenta encerrar a sessão ao fechar.
- Informações básicas em `/info` para técnicos autorizados e log JSONL local de autorização, sessão, tipos de entrada e erros. Não registra o texto digitado nem salva telas.

## Limites desta versão

Não contém chat, transferência de arquivos, reinicialização, descoberta automática, cadastro visual de permissões, inventário completo, auditoria central, vídeo fluido, arraste do mouse, atalhos completos, suporte a múltiplos monitores, tela de login, UAC ou acesso sem funcionário conectado. O console usa uma lista local e não oculta previamente máquinas sem permissão; o agente é quem bloqueia o acesso. A lista não indica disponibilidade em tempo real.

O agente roda sem elevação na sessão interativa, não como serviço. Não atende máquinas sem usuário conectado, bloqueadas ou com sessões simultâneas. Não contorna UAC. O funcionário não tem botões no agente; como o processo roda na sua conta, ainda pode encerrá-lo pelo Gerenciador de Tarefas. Bloqueio de encerramento não está implementado. Logs locais são evidência operacional do piloto, não auditoria inviolável. Para 400 máquinas, a próxima etapa é validar gerenciamento por GPO, centralização de logs e transporte de vídeo mais eficiente. Não há teste de capacidade para 400 conexões simultâneas.

## Autorização sem grupos — versão 0.3

Não é necessário criar grupos no domínio. Informe as contas em `-AllowedUsers 'SICM\jorge.alvin'`. Para vários técnicos, use uma lista PowerShell: `-AllowedUsers 'SICM\jorge.alvin','SICM\OUTRO_LOGIN_REAL'`. Substitua o segundo login pelo real; ele não está autorizado previamente.

A lista é aplicada por computador/setor durante a instalação, e a autorização compara o SID da conta autenticada, não um nome enviado pelo console. Remover o SID da política e reiniciar o agente revoga novas solicitações dessa conta. O aplicativo não modifica o Active Directory. A conta Jorge somente terá acesso nas máquinas onde for explicitamente incluída pela instalação; esta atualização não instala nem concede permissões nas máquinas da empresa remotamente.

## Domínio confirmado da empresa

- Domínio Windows: `SICM`.
- Domínio DNS: `SICM.INTRANET`.
- Login confirmado do técnico: `SICM\jorge.alvin`.
- O aviso de atendimento usa a identidade autenticada real; o nome acima não fica fixado no aplicativo.
- `SICM\SDE-Suporte-CMTIC` é uma sugestão de grupo, ainda não confirmado/criado. A conta do técnico precisa pertencer ao grupo autorizado configurado na máquina.
- `pc-piloto.SICM.INTRANET` é apenas exemplo. Substitua pelo FQDN real; certificado e DNS devem corresponder à máquina.

## 1. Preparação pela equipe de domínio

1. Escolha duas máquinas piloto Windows x64, no domínio, uma do técnico e outra do funcionário. Use versões Windows homologadas pela TI.
2. Tenha o SDK .NET 10 na máquina de compilação (https://dotnet.microsoft.com/download/dotnet/10.0). A compilação inicial precisa baixar componentes; os executáveis publicados serão autocontidos.
3. Sem grupos, use contas individuais em `-AllowedUsers`, por exemplo `SICM\jorge.alvin`. O instalador resolve a conta para seu SID no domínio e grava esse identificador na política protegida da máquina. Opcionalmente, `-AllowedGroups` continua disponível. Uma conta comum do domínio não recebe acesso automaticamente.
4. Emita pela CA interna um certificado de servidor para cada agente, com SAN igual ao FQDN e EKU Server Authentication, instalado em LocalMachine\My com chave privada. O computador do técnico deve confiar na CA e resolver o FQDN pelo DNS interno.
5. Anote a sub-rede real dos técnicos. Não abra porta no roteador nem publique o agente na internet.
6. A atribuição de setor é da máquina: usuários podem alternar seus logins sem mudar os grupos autorizados no agente.

## 2. Compilar

No PowerShell, dentro da pasta do projeto:

```powershell
.\scripts\Build.ps1
```

Os resultados serão `dist\Agent\SDE-Suporte-Cliente.exe` e `dist\Technician\SDE-Suporte-Tecnico.exe`. A compilação foi preparada, mas não executada no ambiente de entrega porque ele não dispõe de Windows/.NET SDK. Havendo erro, não distribua: registre a mensagem para correção.

## 3. Instalar na máquina do funcionário

Copie o projeto com `dist` para essa máquina. No PowerShell como administrador, substitua TODOS os valores abaixo pelos reais:

```powershell
.\scripts\Install-Agent.ps1 `
  -Hostname 'pc-piloto.SICM.INTRANET' `
  -Sector 'CMTIC' `
  -AllowedUsers 'SICM\jorge.alvin' `
  -CertificateThumbprint 'IMPRESSAO_DIGITAL_REAL_DO_CERTIFICADO' `
  -TechnicianSubnets '10.0.10.0/24' `
  -AuthorizedUnattended
```

O script copia para Program Files, protege código e política contra alteração por usuários comuns, reserva uma URL HTTPS, vincula o certificado no HTTP.sys, abre a porta 7443 somente no perfil Domain e nas sub-redes indicadas e registra inicialização visível no login. Não cria serviço nem instala silenciosamente nas demais máquinas. Algumas configurações de domínio podem exigir ajuste de SPN/Negotiate pela TI; use FQDN, nunca IP para o piloto.

A opção `-AuthorizedUnattended` permite acesso sem confirmação, mantendo identificação visível e autorização por grupo. Omita-a para exigir confirmação.

Faça logoff/login do funcionário. O ícone do agente deve aparecer na bandeja, sem painel. Se precisar iniciar manualmente:

```powershell
& 'C:\Program Files\SDE-Suporte\SDE-Suporte-Cliente.exe'
```

## 4. Usar no computador do técnico

1. Copie o conteúdo de `dist\Technician` para uma pasta do técnico.
2. Copie `src\machines.example.json` para a MESMA pasta e renomeie para `machines.json`.
3. Edite os FQDNs e setores reais. Acrescente máquinas ao array JSON conforme o piloto avançar.
4. Abra o executável sem argumentos, com o login corporativo do técnico.
5. Selecione a máquina e clique em Conectar. O funcionário confirma se essa exigência estiver habilitada na política. Clique na imagem para enviar cliques e use o campo de texto/botões para digitar.
6. Encerre pelo console. O agente mantém apenas o aviso de identificação; não contém controles para o cliente. Ao fechar o console há tentativa de encerramento; em queda de rede, a sessão termina após 20 segundos sem requisições autenticadas.

## 5. Homologação obrigatória antes da expansão

- Técnico fora do grupo: recebe 403, nenhuma tela liberada.
- Técnico de outro setor: bloqueado mesmo conhecendo o FQDN.
- Com consentimento habilitado, recusa bloqueia acesso. Com atendimento previamente autorizado, o acesso é direto; em ambos os casos a faixa identifica o login autenticado e horário.
- Encerramento pelo técnico: solicitações posteriores falham; a faixa desaparece. Fechar abruptamente o console: aguardar no máximo 20 segundos mais o intervalo do temporizador.
- Sessão expira em 15 minutos; tokens não funcionam com outra conta.
- Certificado inválido ou FQDN errado: cliente recusa conexão. Não desabilitar validação TLS para resolver.
- Bloquear a estação encerra a sessão; desbloquear não reabre acesso automaticamente.
- Confira entrada, escala da tela, tamanho dos quadros, banda, CPU e logs em um atendimento real.
- Faça piloto com 5 máquinas e 2 técnicos, depois 20 máquinas. Só amplie quando a TI aprovar os resultados.

Logs: `%LOCALAPPDATA%\SdeSuporte\audit.jsonl` na conta do funcionário. O sistema não está conectado ao domínio da empresa durante esta entrega; grupos, DNS, certificado e firewall precisam ser configurados localmente pela TI.

## Desinstalação

Feche o agente. Execute `scripts\Uninstall-Agent.ps1 -Hostname 'FQDN_REAL'` como administrador, confira os resultados e remova a pasta de instalação. O certificado e logs são preservados. Os scripts não revertem automaticamente alterações parciais: em falha, confira reservas HTTP.sys/firewall antes de repetir.

## Referências técnicas

- Windows Authentication: https://learn.microsoft.com/aspnet/core/security/authentication/windowsauth
- HttpListener: https://learn.microsoft.com/dotnet/api/system.net.httplistener
- Windows Forms: https://learn.microsoft.com/dotnet/desktop/winforms/

Autoria do projeto: Jorge Henrique — CMTIC/SDE. Implementação inicial preparada com assistência de IA; requer revisão e homologação da equipe responsável.

## Compilação pelo GitHub

O arquivo `.github/workflows/windows-build.yml` compila os dois aplicativos em Windows e disponibiliza um artefato para download. Publique o CONTEÚDO desta pasta na raiz de um repositório e execute Actions → Compilar SDE Suporte para Windows → Run workflow. A rotina é executada no repositório NETSUPORTE-SDE. Consulte a aba Actions para o resultado da compilação. Os executáveis gerados não possuem assinatura Authenticode. A compilação não comprova funcionamento de domínio ou acesso remoto.

## Próxima etapa de engenharia

O serviço Windows e a comunicação segura com o componente da sessão interativa ainda NÃO estão implementados. Esta versão trabalha somente com funcionário conectado e área de trabalho desbloqueada. O código não deve ser apresentado como equivalente completo ao NetSupport. Não realiza administração da tela de login, UAC ou desktop seguro. A criação do instalador final depende de compilação e homologação Windows, implantação de certificados da empresa e definição dos grupos de domínio.
