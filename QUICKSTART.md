# SDE Suporte 0.6 — começar

## No seu computador e nos outros cinco técnicos

1. Execute **SDE-Suporte-Tecnico-Instalar.exe** e conclua a instalação.
2. Abra **SDE Suporte - Técnico**, pelo atalho da Área de Trabalho.
3. Informe seu nome.
4. Cole o **código privado da equipe** entregue separadamente. O mesmo código funciona para os técnicos autorizados e só é solicitado na primeira abertura de cada conta do Windows.

## Nos computadores dos funcionários

1. Execute **SDE-Suporte-Cliente-Instalar.exe** com permissão de administrador.
2. Conclua a instalação. Não escolha técnicos, não importe arquivos e não informe o código da equipe.
3. O agente fica disponível e volta a iniciar nos próximos logins do Windows.

O mesmo instalador Cliente serve para todos os computadores da equipe. Ao adicionar outro técnico, não é necessário reinstalar os clientes.

Se já instalou o agente 0.5 ou anterior, execute este novo instalador Cliente uma vez em cada computador para migrar a autorização.

## Para conectar

1. Abra o console. A lista é preenchida automaticamente.
2. Selecione o computador e clique em **Conectar**.
3. O funcionário verá o nome de quem está atendendo e o horário.
4. Ao terminar, clique em **Encerrar**.

O funcionário deve estar conectado ao Windows e com a área de trabalho desbloqueada.

## Se a máquina não aparecer

1. Confira se o novo agente está instalado e disponível no computador.
2. Clique em **Atualizar máquinas**.
3. Clique em **Varredura de IP** para informar a rede, por exemplo `10.37.2.0/23`, ou uma faixa como `10.37.2.1-10.37.3.254`.
4. Aguarde o progresso dos IPs. O botão **Parar varredura** interrompe a busca.
5. Entre VLANs, peça a liberação de TCP 7443 entre técnicos e clientes. Não é necessário liberar ping para essa busca.

A busca inicial pesquisa até /22 por interface. Para uma rede maior, informe a faixa completa em Varredura de IP. São aceitos até 65.536 IPs privados por pesquisa.

**Guarde o código da equipe somente com a TI. Ele não deve acompanhar o instalador entregue aos funcionários.**
