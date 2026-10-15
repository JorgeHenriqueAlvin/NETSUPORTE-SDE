# Instalar uma vez e aparecer no console

1. Instale a versão 0.5 do console pelo instalador geral, escolhendo Console do técnico.
2. Abra o console e clique em **Gerar instalador do cliente**. Informe o setor, inclua outros técnicos se necessário e salve o executável. Você faz isso uma vez por setor/equipe.
3. Execute o instalador gerado nos computadores dos funcionários. Confirme a instalação como administrador. O agente inicia na sessão normal do funcionário.
4. Com o console aberto, os computadores aparecem automaticamente como **Conectado**. Selecione um deles e clique em **Conectar** para atender.

Não é preciso exportar um arquivo de cada computador, digitar IP nem configurar certificados. O funcionário vê o nome do técnico e a hora do atendimento, sem painel administrativo.

Para outros técnicos incluídos no instalador: cada um importa o arquivo `.sdeteam` gerado ao lado do instalador uma única vez no seu console. Depois a descoberta das máquinas autorizadas é automática. O nome/identidade de cada técnico permanece individual.

A descoberta usa UDP 7445/7446. Na mesma sub-rede, o console procura por broadcast. Os agentes também anunciam sua presença nos endereços dos técnicos registrados no instalador. Outras VLANs precisam permitir essas portas e a conexão TCP 7443. Reserva de DHCP/IP ou nome DNS estável para os computadores dos técnicos facilita a descoberta entre VLANs.

O executável gerado autoriza cadastrar máquinas nessa equipe; distribua-o pela TI. Ele não contém a chave privada de controle remoto do técnico. Apenas instalar um agente genérico não autoriza qualquer pessoa da rede a acessá-lo.

Esta versão exige funcionário conectado ao Windows e tela desbloqueada. Os testes automatizados não substituem um teste entre duas máquinas da empresa.
