# Operação 0.12.14

Esta entrega melhora atualização, diagnóstico, coleta de eventos e presença. Não instala serviço antes do login, não atravessa NAT e não controla tela bloqueada/UAC.

## Windows

- Instalar Técnico e Cliente 0.12.14 uma primeira vez. Agentes antigos não entendem o encerramento voluntário.
- Tarefa SDE-Suporte-Atualizacao consulta o GitHub a cada cinco minutos. Verifica tamanho e SHA256; não permite downgrade.
- Depois de preparar o pacote, solicita manutenção em arquivo administrativo válido por três minutos. O Cliente novo encerra somente sem requisição em curso, sem atendimento e após 60 segundos ocioso. Um auxiliar na sessão do funcionário aguarda o atualizador e reabre o agente. Não se mata processo para atualizar.
- Console aberto, sessão ativa ou Cliente antigo: atualização fica adiada. O updater espera no máximo 85 segundos pelo encerramento; nova tentativa ocorre depois.
- O console deve ser fechado para atualizar. A tarefa também detecta pacote Cliente ausente, adulterado ou hash divergente na mesma versão e reinstala o Técnico oficial.
- UpdateCache/status.json informa preparado, aguardando, adiado, instalando, confirmado ou falha, com horário e versões. Confirmação verifica arquivo instalado; a presença posterior confirma comunicação.
- Se a instalação falhar, não há rollback automático de Windows: consulte update.log. O auxiliar tenta reabrir o binário disponível. Não há promessa de recuperação de binário ausente.
- Bootstrap e manutenção precisam manter pasta de instalação e scripts protegidos por permissões administrativas; não mova o programa para pasta gravável por funcionários.

## Estado e logs

A aba Estado e logs consulta agentes com presença recente, a cada minuto enquanto visível, com oito consultas simultâneas. Usa os mesmos certificados fixados e credenciais assinadas das outras operações. /health e /audit exigem autenticação de técnico; não permitem leitura de arquivo arbitrário.

Os agentes mantêm log JSONL rotativo (4 MiB por arquivo, três anteriores). O console recebe até 200 eventos recentes por consulta, deduplica e mantém seu próprio histórico rotativo por identidade da máquina. É uma coleta no computador do técnico, sem servidor: não sincroniza vários técnicos e pode perder eventos durante períodos prolongados offline. Não captura teclas, tela, navegação ou todo uso do funcionário.

Registros de atualização mostram quando foram escritos; confirme versão e presença depois. Estado sem comunicação não prova máquina desligada: use Diagnosticar selecionada para separar porta recusada, timeout, TLS, equipe e autorização. Não é possível distinguir firewall de máquina desligada somente por timeout.

## Raspberry ARM64

- Continua exigindo usuário normal, desktop X11 e dependências; Wayland e acesso antes do login não são suportados.
- Bootstrap: baixar o pacote ARM64 da release raspberry-stable e executar install.sh no desktop. A primeira atualização de instalações antigas é manual.
- O launcher reinicia após falha com espera progressiva até 60 segundos. Não é um daemon do sistema.
- O worker consulta linux-update.json no canal fixo raspberry-stable a cada hora. Verifica versão, tamanho e SHA256; não aceita URL de download fornecida pelo manifesto.
- Agente sem atendimento e ocioso por 60 segundos encerra com código 75. O supervisor aplica o pacote preparado, valida caminhos e recusa links no tar, troca diretórios preservando a identidade em ~/.local/share/SdeSuporte e inicia novamente.
- Há cópia anterior dos arquivos, mas não rollback automático de falhas de execução nesta entrega. Execute diagnose.sh para IPs, portas, processos, versão, atualização e últimas mensagens.
- Atualização é por usuário. Internet é necessária para obter versões; suporte local funciona sem internet.

## Presença e piloto

O console limita presença a 32 consultas simultâneas. Falhas repetidas esperam progressivamente até 120 segundos. Resposta HTTP 429 não renova a prova de presença. Observações são gravadas em lote a cada quatro segundos.

Validar primeiro em dois Windows e um Raspberry: descoberta, nome/login/SO/RAM, consentimento, atendimento ativo durante atualização, fim de atendimento, encerramento/reabertura, identidade preservada, coleta e recusa de técnico desconhecido. Depois ampliar para 20, 100 e 400. Não há benchmark comprovado para 400 máquinas reais.

## Locais externos

Informe faixas privadas roteáveis em Varredura de IP e libere TCP 7443 entre TI e agentes; UDP 7445/7446 ajuda descoberta na mesma rede. Em VLANs e locais separados, solicite à equipe de rede rotas e regras restritas. Instalar apenas o agente não atravessa NAT. Não exponha portas administrativas à internet para tentar contornar falta de conectividade.
