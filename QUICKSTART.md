# SDE Suporte 0.6.1 — instalar e conectar

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

Clientes da versão **0.6 continuam funcionando** com os novos técnicos, sem reinstalação. Somente clientes 0.5 ou anteriores precisam receber o novo instalador Cliente uma vez.

## Buscar outra rede ou faixa

A lista usa descoberta automática e varredura de IP. Clique em **Varredura de IP** para informar outra rede ou uma faixa, como `10.37.2.0/23` ou `10.37.2.1-10.37.3.254`. O botão **Parar varredura** interrompe a busca.

A busca inicial usa até /22 por interface. São aceitos até 65.536 IPs privados por pesquisa. Entre VLANs, permita TCP 7443 entre técnicos e clientes. A busca não depende de ping.

Para atendimento, o funcionário deve estar conectado e com o Windows desbloqueado. O aviso de atendimento mostra quem está acessando.

## Distribuição à TI

O arquivo **Técnico-Autorizado** já concede acesso à equipe e deve ser distribuído somente aos técnicos. Ele é entregue por canal privado e não acompanha o instalador dos funcionários.

O arquivo **Técnico-Base**, disponível no repositório público, é um modelo para gerar o instalador autorizado; ele não contém a autorização privada. A versão anterior com código permanece como compatibilidade para instalações antigas.
