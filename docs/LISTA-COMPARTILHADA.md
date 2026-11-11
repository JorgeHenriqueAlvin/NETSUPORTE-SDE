# Lista de máquinas entre técnicos

No Técnico atualizado, use **Importar lista do Técnico antigo** no computador
que possui o cadastro completo. Selecione o arquivo `computers.json` do perfil
antigo (`%LOCALAPPDATA%\SdeSuporte` na respectiva conta Windows). O arquivo
contém endereços e certificados públicos. Nenhuma chave pessoal é importada.

Depois use **Compartilhar máquinas** e informe uma pasta de rede restrita à TI,
com leitura e gravação para os técnicos, por exemplo
`\\servidor\compartilhamento-ti\Suporte`. Configure o mesmo caminho nos demais
Técnicos atualizados, autorizados para a mesma equipe. Não basta usar pastas
locais diferentes com o mesmo nome. O programa não cria compartilhamentos nem
altera permissões do Windows.

O cadastro é publicado por console em arquivos separados, com substituição
atômica, e consultado a cada 30 segundos. As novas máquinas são adicionadas à
lista local. O computador que publica precisa abrir o Técnico pelo menos uma
vez para enviar seu cadastro. O compartilhamento pode ficar em um servidor
interno que permaneça disponível; não exige que esse Técnico fique aberto.

A lista permanece local quando a rede está indisponível. Importações são
limitadas por tamanho e quantidade. Pins de certificado conflitantes não
substituem entradas locais. A lista compartilhada é cumulativa: excluir uma
máquina localmente não a exclui dos outros consoles e ela pode reaparecer.
Mudanças de IP em cadastros existentes não são replicadas por este recurso;
a descoberta e a edição local continuam disponíveis. Não há sincronização de
histórico, descrições personalizadas, status online ou credenciais.

Importar uma lista NÃO concede acesso. A conexão continua autenticando a
identidade individual do técnico no Cliente, conferindo o certificado e a
política de atendimento. Equipes diferentes continuam incompatíveis: instalar
o novo Cliente é necessário para migrar máquinas da equipe antiga. A presença
online só é confirmada após comunicação autenticada com cada Cliente.

## Validação no Windows

1. Use duas contas de técnicos autorizadas para a mesma equipe e uma pasta
   de rede restrita à TI. Importe uma lista de teste apenas no primeiro.
2. Configure o mesmo compartilhamento em ambos; aguarde 30 segundos e confirme
   que as máquinas aparecem sem copiar arquivos manualmente.
3. Confirme uma conexão autorizada com Cliente da mesma equipe e a rejeição
   de Cliente da equipe antiga. A lista nunca deve eliminar essa verificação.
4. Desconecte o compartilhamento e confira preservação da lista local.
5. Coloque um JSON inválido entre os arquivos de teste; a sincronização dos
   demais arquivos deve continuar. Verifique que um certificado conflitante
   não substitui o cadastro local existente.

Compilação e teste de integração Windows ainda precisam ser executados.
