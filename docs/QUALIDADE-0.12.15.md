# Qualidade e usabilidade 0.12.15

## Desempenho
A lista de máquinas agrupa atualizações em 180 ms e mantém os itens nativos quando ordem e cadastro não mudaram. Verificações de presença redesenham os detalhes sem reconstruir toda a lista. Pesquisa espera uma pausa curta na digitação; seleção e primeira máquina visível são preservadas quando possível. A filtragem vazia evita montar textos de pesquisa de todas as máquinas.

A coleta de eventos grava cada lote fora da thread da interface. IDs só são marcados recebidos após a gravação; falha de disco permite nova tentativa. Máquinas removidas deixam a tabela de operação; logs anteriores continuam no perfil do técnico.

## Interface e acessibilidade
Abas podem ocupar mais de uma linha, a coluna de máquinas se adapta à largura da janela, e altura dos cartões acompanha a fonte/DPI. Lista, busca, filtros e tabela de operação têm nomes acessíveis. Ctrl+F pesquisa, F5 busca agentes e Esc limpa a pesquisa; quando a tela remota tem foco durante atendimento, preserva os atalhos do computador atendido. Tabela de operação quebra linhas e mantém rolagem horizontal.

Uma falha em editar metadados ou outra ação não para mais o timer da tela remota. Falha do fluxo de imagens encerra a sessão correspondente e mostra indicação para reconectar.

## Comunicação, segurança e persistência
RemoteClient aceita CancellationToken e cancela conexões e esperas de repetição. Telas de inventário, operação e verificações de presença usam cancelamento real em vez de apenas abandonar a espera. As regras de certificado, assinatura, consentimento e equipe continuam vigentes.

Rotas devem ser locais e não podem usar URL absoluta, barras ambíguas ou caracteres de controle. Cadastros validam comprimentos e caracteres; retornam erro controlado em hash ausente. Respostas HTTP têm limite de 32 MiB, e mensagens de erro são resumidas em até 512 caracteres.

Json.Write publica um snapshot por arquivo temporário no mesmo diretório e substituição atômica, evitando truncar o cadastro anterior antes de serializar/gravar o novo. Não introduz sincronização de banco de dados, backup permanente ou recuperação automática de cadastros já corrompidos.

## Validação e limites
Os testes cobrem persistência de seleção, janela compacta, cancelamento antes da conexão, rejeição de rotas e cadastros inválidos, preservação do cadastro após serialização com erro e lote de eventos, além das suítes existentes de Windows e Linux.

Esta revisão não equivale a auditoria de segurança completa nem comprova desempenho em 400 máquinas físicas. Não altera infraestrutura de rede, regras de consentimento, serviços antes do login, bloqueio/UAC ou suporte a Wayland.
