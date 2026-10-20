# Implantação e identificação — 0.12.9

## Diagnóstico

A geração já calculava o SHA256 depois de compilar o Cliente e antes de compilar o Técnico. Porém, o Inno Setup incluía EXE e hash sem `ignoreversion`: regras de versão/data diferentes para arquivos versionados e não versionados podiam manter o EXE anterior com hash novo durante atualização/reinstalação. Essa é uma causa possível demonstrada pelo código; sem arquivos e logs da máquina afetada não é possível afirmar qual foi a causa naquele computador.

O instalador agora substitui ambos. O console valida em segundo plano existência, metadados, SHA256 exato e versão major/minor/patch. Não altera autorização da equipe, certificado fixado, validação remota ou autenticação administrativa. Erros e sucesso são registrados em `%LOCALAPPDATA%\SdeSuporte\package-verification.jsonl`, com caminho, versão, etapa e motivo. Falhas de leitura/acesso têm mensagem própria. Hash local continua dependendo da proteção administrativa da pasta instalada e da distribuição oficial, como no mecanismo existente; não é uma assinatura Authenticode nova.

A aba Atualizações pode disparar a tarefa confiável já registrada. O atualizador pode exigir versão superior para instalar: para corrupção da mesma versão, reinstale o instalador oficial do Técnico. Não se baixa pacote de URL arbitrária.

## Identificação

`machine-metadata.json` guarda apelido, descrição administrativa, setor e local por ID estável do agente. `machine-observations.json` guarda hostname, descrição do Windows, SO, versão do agente e última comunicação obtidos em `/info` autenticado. O cadastro assinado `Device` não foi modificado. Os arquivos ficam no perfil do técnico e sobrevivem à atualização e troca de IP; não são sincronizados entre técnicos. Reinstalação que apague a identidade do agente requer reconciliação manual.

Editar identificação não renomeia o sistema operacional. O hostname aparece separado do apelido; dados de descoberta usam colunas específicas. Ausência de descrição/OS não é preenchida por suposição. Agentes antigos continuam conectando e a tela de inventário indica atualização quando não suportada. O inventário autenticado existente fornece arquitetura, CPU, RAM, discos e versão do agente.

A descoberta básica usa ping/portas e DNS reverso com validação direta, timeout e cancelamento, sem credenciais. Não identifica Windows por SMB nem Raspberry por portas. Redes que só anunciam nomes por NetBIOS/mDNS e não fornecem DNS reverso podem continuar mostrando nome não identificado até o agente ser instalado/adicionado pelo fluxo Linux existente. Antes da implantação o nome é resolvido novamente e deve corresponder ao IP. SMB + Agendador exige ADMIN$, direitos administrativos e RPC/Agendador remoto, sem WinRM. A descoberta preserva as linhas e seleção por IP; linhas antigas permanecem na lista e não provam presença atual.

## Verificação

Executados neste ambiente: 5 testes Python de publicação e 5 testes Python do desktop Linux; `git diff --check`. Sem SDK .NET ou Windows neste ambiente: compilação e testes C#/PowerShell não executados localmente.

O workflow Windows inclui regressões para pacote válido, ausente, hash ausente/divergente, incompatibilidade e persistência por ID. Também reinstala o Técnico sobre um hash adulterado e EXE com data futura, verificando o par instalado. A publicação automática continua restrita a main e ao sucesso dos testes existentes; a branch de revisão não publica release.

Validar na rede real antes de distribuir:

1. Técnico anterior → novo Técnico: conferir versão do console, EXE e SHA256 em `Packages`; repetir com pacote ausente e hash divergente e consultar log.
2. Windows com descrição definida e Raspberry Pi 4 Linux: conferir hostname autenticado, distribuição, inventário, arquitetura, RAM e discos. Testar agente antigo sem novos campos.
3. Máquina sem DNS reverso, sem agente e Linux com SMB: conferir ausência de nomes/OS inventados. Cancelar busca e repetir mantendo seleção e pesquisa.
4. Editar metadados, reiniciar Técnico e máquina, trocar IP mantendo ID do agente: metadados devem permanecer. Testar pesquisa por cada campo.
5. Desabilitar WinRM no alvo autorizado, manter SMB/Agendador remoto e executar implantação. Conferir hash no alvo e resultado real no console.
6. Executar atualização pela tarefa registrada, fechar aplicativos e conferir log do atualizador. O teste de reinstalação CI não substitui o teste agendado na rede real.

## Atualizar

Após revisão e sucesso do CI Windows, integrar a branch. Somente uma release efetivamente publicada poderá ser recebida pelo atualizador. Fechar Cliente/Técnico para permitir instalação. Para recuperar implantação quebrada, instalar o Técnico oficial completo, preservando o perfil do técnico; não copiar EXE ou hash isoladamente. Atualizar os Clientes Windows para receber novos campos; Linux continua com atualização manual pelo procedimento de `linux/README.md`.

Nenhum instalador ou release foi gerado localmente nesta alteração.
