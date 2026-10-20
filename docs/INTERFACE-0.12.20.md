# Interface 0.12.20

O Técnico mantém Windows Forms e os fluxos existentes. Não há interface web ou aplicativo para celular nesta entrega.

## Padrão visual

- Cores centralizadas em `Premium`: fundo claro, azul profundo, turquesa, texto e bordas com contraste.
- Botões, campos, tabelas e abas padronizados; foco de teclado preservado.
- Ações de atendimento na tela remota; ações adicionais de máquinas em “Mais ações”.
- A lista lateral permanece no atendimento e no chat. Inventário, avisos, diagnóstico e atualizações usam a largura disponível.
- Fichas de máquinas distinguem nome, informações e estado por texto e cor.
- Inventário e avisos oferecem rolagem vertical em janelas compactas; instruções de instalação ficam em “Como instalar”.
- Remoção do cadastro exige confirmação; cliques repetidos nas ações assíncronas são bloqueados.
- O agente Raspberry usa as mesmas cores e identifica o campo de mensagem. Envio fica desabilitado sem atendimento.

## Verificação

Testes visuais de desenvolvimento usam perfil temporário, identidade de teste e descoberta automática desabilitada. Nenhum cadastro real é substituído por dados de demonstração no produto.

O comando `dotnet run --project tests/SdeSuporte.Tests.csproj -c Release -- ui` captura as seis áreas em 900×560, 1024×768 e 1360×850, e verifica filtros, seleção, mensagens e retorno da tela cheia.

Regressão: suíte `protocol`, testes de operações, testes Python Linux e publicação de instaladores. A validação de TLS no Windows pode requerer execução fora do sandbox.

Pendências ambientais: validar monitores com escalas DPI variadas, leitor de tela e toque; testar o agente Tk/X11 em Raspberry físico. As capturas locais e os testes automatizados não substituem esses testes.

Esta revisão não altera certificados, autenticação, permissões de atendimento, protocolos, rotas nem formato de dados.
