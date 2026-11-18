# Migração e compatibilidade — 1.3.0

## Atualização da 1.2.1

Feche todas as instâncias do Leitor antes de substituir os executáveis. Instalação e portátil continuam usando `%LOCALAPPDATA%\Leitor` por padrão. Nenhuma biblioteca pessoal foi aberta ou migrada durante o desenvolvimento: a migração efetiva ocorrerá quando o usuário abrir a nova versão. Todos os ensaios usaram dados fictícios.

LibraryStore verifica `PRAGMA user_version`: a versão antiga é 1 e a nova é 2. Um mutex local por caminho serializa migradores. Antes de alterar um banco existente, cria uma pasta privada `backups/pre-1.3.0-<UTC>-<GUID>` dentro da pasta de dados. A API BackupDatabase do SQLite produz uma cópia consistente do banco, incluindo dados ainda presentes em journal/WAL. Também são copiados, quando existentes, `vault.json`, `settings.json` e `auth-state.bin`. O cofre é conferido novamente antes de prosseguir; uma mudança concorrente aborta a operação. Falha de backup impede a migração.

Em uma transação são criadas as tabelas/índices de organização e o catálogo. Os payloads antigos não são regravados, descriptografados ou recifrados. Documentos existentes ficam inicialmente sem pasta. Conteúdo, títulos, etiquetas, progresso, datas e criptografia antigos permanecem nos mesmos bytes; o PIN e a chave não são recriados. Vozes/modelos/runtime não são movidos ou alterados. Configurações recebem novos valores padrão apenas para as preferências ainda ausentes.

A data original de salvamento é preservada. Como a 1.2.1 não registrava criação separada, ela é usada como aproximação da criação nos documentos legados; não é possível reconstruir uma data histórica inexistente. Documentos protegidos só revelam seus metadados após autenticação, sem criar cópias em texto simples no catálogo ou backup.

A versão do esquema só muda ao confirmar a transação. Reabrir um banco já na versão 2 não duplica dados, pastas ou backups. Esquemas futuros desconhecidos são recusados. Registros inválidos são isolados como indisponíveis, sem substituição silenciosa do conteúdo; operações que exigem validar um protegido inválido são recusadas.

## Backup e retorno a versões anteriores

A cópia automática cobre a migração; não substitui uma política de backup periódico nem é uma funcionalidade de exportação geral. Verifique sua existência antes de descartar uma distribuição anterior. Preserve cópias privadas de banco, cofre e preferências em conjunto, com o aplicativo fechado.

Não use a 1.2.1 para gravar um banco já atualizado: ela desconhece o catálogo e pode produzir inconsistências nas associações e resumos. Para voltar, feche o aplicativo, guarde uma cópia do estado atual e restaure **em conjunto** os arquivos da pasta de backup para a pasta de dados, usando então a versão anterior. Isso descarta alterações feitas depois do backup; a nova cópia guardada permite reconsiderar. Não misture banco de uma data com cofre de outra. Não há utilitário automático de downgrade.

DPAPI continua vinculada ao mesmo perfil Windows; a cópia não resolve perda do PIN ou migração de conta/computador. Backups preservam o estado da ocasião: um texto comum protegido depois pode continuar legível em backups antigos. `secure_delete` não apaga backups, snapshots ou fisicamente um SSD. Os nomes/hierarquia das novas pastas são locais e não criptografados. [Segurança](SEGURANCA-1.2.md).

## Evidências

Os testes criam um banco esquema 1 com documentos comuns e cifrados, comparam bytes do payload e cofre, verificam backup antes da mudança, abrem o protegido com o PIN original e repetem a inicialização. A migração foi idempotente, sem perda observada nas fixtures. Não se afirma que a biblioteca real já foi migrada. O benchmark com 10 mil documentos de 2.048 caracteres mediu aproximadamente 1,28 s para primeira migração/backup/indexação; esse trabalho inicial ainda é síncrono. Aberturas seguintes reutilizam o esquema. [Medições](DESEMPENHO-1.3.0.md).
