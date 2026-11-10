# Desktop administrativo e encerramento — 0.13.0

## Implementação

O Cliente continua sem elevação na sessão do funcionário, cuidando do consentimento, chat, identidade da sessão e HTTPS. O serviço `SDE-Suporte-Desktop` usa um executável separado, `Agent/SDE-Suporte-Desktop.exe`, para não impedir a atualização ociosa do Cliente. Sua inicialização automática não abre acesso antes do login.

Depois da autorização normal, o Cliente encaminha a solicitação original assinada ao serviço por pipe local. O serviço valida o PID e o caminho do Cliente instalado, a assinatura da equipe, horário e nonce. Ele abre um auxiliar SYSTEM somente na sessão Windows ativa e explicitamente desbloqueada daquele processo. O auxiliar vincula uma conexão ao PID do Cliente, ao técnico e ao token de atendimento e valida novamente cada operação assinada. Não aceita comandos de shell, caminhos de executáveis ou entrada de teclado sem assinatura. O pipe nega conexões de rede, e o Cliente confere o PID do serviço no SCM e o PID do auxiliar retornado por esse serviço.

Captura e entrada são realizadas numa thread sem janelas, ligada temporariamente ao desktop ativo Default/Winlogon. A consulta WTS exige estado ativo e desbloqueado antes e depois das operações. Bloqueio/logoff encerram a sessão no Cliente; o auxiliar também recusa a sessão bloqueada. Ao fechar o pipe ou ficar 20 segundos sem operação assinada, o auxiliar libera teclas/botões e sai. Há limite de uma hora por auxiliar e oito auxiliares simultâneos. O polling de chat mantém o auxiliar quando a tela remota não está visível.

O serviço só é habilitado no diretório padrão protegido em Program Files. A instalação administrativa para o serviço antes da troca dos binários, reinicia após selar o cadastro e o remove na desinstalação. Nenhuma política UAC, senha de administrador ou senha do funcionário é alterada ou armazenada. O serviço precisa de SYSTEM para trocar a thread para a área protegida; o código mantém os pedidos de consentimento e as assinaturas existentes.

A tela cheia agora guarda o contêiner original do seletor de monitores, em vez de pressupor uma única barra. O encerramento destaca a sessão e limpa a imagem imediatamente; uma captura atrasada não consegue repor a imagem. O descarte da janela ocorre mesmo se a restauração dos controles falhar.

## Verificação automatizada

- Compilar Cliente, Técnico, auxiliar e testes Windows.
- Regressão com as duas barras reais: sair, sair novamente e encerrar com falha da rede; conferir janela descartada, seletor restaurado e imagem limpa antes de aguardar a rede.
- Em runner Windows limpo, `tests/Test-DesktopService.ps1` instala no caminho padrão, verifica criação e parada do serviço, abre atendimento com credenciais descartáveis, exige `desktopHelper=true`, captura JPEG/monitores pelo auxiliar SYSTEM, envia uma liberação de tecla, rejeita layout antigo e processo local não autorizado e testa encerramento, nova sessão e remoção.
- Os testes de protocolo, instaladores e atualização existentes continuam obrigatórios.

O teste automatizado do serviço usa o desktop normal e conta administrativa de CI. Ele não comprova interação por uma conta padrão, UAC real, monitores com escalas diferentes ou transições de bloqueio em equipamentos da SDE.

Diagnóstico do serviço: `Agent/desktop-service.jsonl` (sem imagens, senhas ou corpo dos comandos). Falha ao abrir o auxiliar aparece também no `audit.jsonl` do funcionário como `desktop_helper_unavailable`, com o motivo.

## Piloto Windows obrigatório antes da distribuição

1. Instalar Cliente no caminho padrão com administrador e entrar no Windows com conta de funcionário sem administração.
2. Conectar com Técnico atualizado e autorizar normalmente. Confirmar no log `desktop_helper_connected` e em `/health` autenticado `desktopHelper=true`.
3. No computador remoto, executar um aplicativo como administrador. Conferir que o pedido UAC permanece visível no Técnico, digitar a credencial administrativa e operar o aplicativo elevado. Cancelar um segundo pedido e confirmar retorno à área normal.
4. Repetir com cada monitor e mudança de escala/resolução.
5. Durante outro pedido UAC, encerrar pelo Técnico: a janela deve sair da tela cheia, apagar a imagem e liberar a sessão. Reconectar após cancelar o pedido localmente.
6. Bloquear/desconectar/trocar usuário durante atendimento: nenhuma imagem ou entrada da tela bloqueada pode continuar. Só iniciar outro atendimento após desbloquear e autorizar novamente.
7. Fechar o Cliente ou perder a rede e confirmar término do auxiliar. Repetir com chat aberto por mais de 20 segundos e depois voltar à tela remota.
8. Atualizar e desinstalar, verificando que o serviço e seus auxiliares param e são removidos.

Até esse piloto, considerar o suporte ao UAC implementado e pendente de validação no desktop protegido real. Linux não recebe este serviço.
