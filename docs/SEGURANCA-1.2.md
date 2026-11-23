# Revisão de segurança — Leitor 1.2.0

Data: 17/11/2026. Revisão técnica do código e testes locais; não constitui certificação, pentest independente ou garantia de ausência de vulnerabilidades.

## Achados e tratamento

| Achado na versão anterior | Correção / resultado |
| --- | --- |
| Espera de PIN limitada à memória da sessão | Estado salgado persistido com DPAPI; atrasos de 2–30 s. Corrupção causa espera temporária, nunca apagamento. |
| Ausência de alteração do PIN e encerramento explícito de sessão | Reenvolvimento atômico da mesma chave; comando de bloqueio salva/interrompe, limpa chave e recarrega placeholders. |
| Payload cifrado sem vínculo autenticado à linha | Novas gravações LVR2 usam AAD com ID; ID/flag conferidos também no JSON. Formatos antigos continuam legíveis. |
| Gravação direta de cofre/configuração | Temporário exclusivo, WriteThrough/Flush e substituição atômica; limpeza de temporários. SQLite usa transações e parâmetros. |
| Documento inválido interrompia a carga da biblioteca | Isolamento como indisponível, sem sobrescrita; desativação de proteção recusada antes de remover a chave. |
| Configurações numéricas e JSON inválido | Normalização de faixas/enums; cópia preservada do arquivo inválido antes de defaults. |
| Permissões dependiam apenas da herança | ACL de novas pastas restringe ao usuário atual, SYSTEM e administradores. Pastas existentes não têm ACL reescrita. |
| Downloads confiavam principalmente em HTTPS; bootstrap mutável | Manifesto SHA-256 para Python/pip, wheels fixados com hashes e modelos/configurações com hash confiável. Rejeição antes da publicação. |
| Extração genérica de ZIP | Prevalidação de caminhos, diretório autorizado e rejeição de links/reparse/Zip Slip no extrator compartilhado; script Python aplica proteção equivalente. |
| Runtime Python antigo | Instalador opcional passa a usar Python 3.14.10, preservando runtime anterior até o estágio novo passar em pip check/check_piper.py. |
| Exceção inesperada mostrava detalhes internos | Mensagem genérica; textos/PIN não são incluídos em logs ou relatórios. Erros operacionais continuam informando o necessário. |

## Criptografia, dados e processos

AES-256-GCM, PBKDF2-SHA256 (600.000 iterações), DPAPI CurrentUser e geração criptográfica aleatória foram mantidos. Nonce de 96 bits novo por cifra; tag de 128 bits verificada. Testes cobrem aleatoriedade, adulteração, associação à identidade, payload legado e ausência do conteúdo protegido em texto simples no banco. Isso não prova matematicamente ausência de colisões nem impede comprometimento do sistema.

PIN novo/alterado: 6–64 dígitos ASCII com confirmação; verificação permite PIN legado. Buffers de derivação e chave são zerados quando possível. Strings gerenciadas, cópias internas e páginas do sistema não têm garantia de descarte imediato. Os arquivos de chave não armazenam o PIN diretamente. Não há mecanismo de recuperação.

Proteção é individual; bloquear a abertura da interface não cifra documentos desprotegidos. A limitação de tentativas é uma barreira da aplicação e pode ser contornada por quem controla os arquivos da própria conta. Um administrador ou malware na sessão desbloqueada pode capturar textos/chaves. DPAPI impede simples transferência do cofre para outra conta, não protege uma conta já comprometida.

Novos campos são opcionais; o schema SQLite/caminho de dados não mudou. Escritas 1.2 com AAD não são legíveis pela 1.1: downgrade requer backup anterior. Não foi feita migração destrutiva nem teste na biblioteca real. Transações e substituição atômica reduzem falhas parciais, mas não substituem backups nem garantem integridade sob qualquer falha de energia/hardware.

A síntese recebe texto em JSON pelo stdin, sem formar comandos de shell. Executáveis têm argumentos separados, UseShellExecute=false e caminhos locais. O worker configura modo offline e não contém HTTP. Áudio é mantido em memória, sem cache permanente. A rede é usada exclusivamente para instalar recursos/obter modelos e fichas. Não foi realizado bloqueio físico da rede; a validação de privacidade inclui inspeção do caminho de execução.

## Dependências e procedência

- NuGet: auditoria de pacotes diretos e transitivos não indicou vulnerabilidades conhecidas no ambiente consultado. Locks e versões preservados; nenhuma dependência visual nova.
- PyPI: metadados oficiais das sete versões fixadas retornaram lista de vulnerabilidades vazia. Isso depende da cobertura do catálogo e da data da consulta.
- Python 3.14.10: versão e release verificadas em [python.org](https://www.python.org/downloads/release/python-31410/). O ZIP oficial foi baixado e seu SHA-256 fixado no manifesto; pip/wheels foram comparados aos digests publicados pelo PyPI. O hash localmente fixado protege a distribuição subsequente, sem alegar assinatura independente do ZIP Python.
- [PyPI Piper 1.8.0](https://pypi.org/project/piper-tts/1.8.0/) e [advisories oficiais do Piper](https://github.com/OHF-Voice/piper1-gpl/security/advisories): nenhuma advisory publicada encontrada na consulta. Ausência de advisory não significa ausência de falhas.
- Modelos ONNX: SHA-256 obtido do LFS oficial do repositório [rhasspy/piper-voices](https://huggingface.co/rhasspy/piper-voices); configurações fixadas a partir dos bytes oficiais revisados. Modelos acima de 256 MB/configurações acima de 2 MB são recusados. Atualização upstream exige revisão dos hashes antes de aceitar conteúdo novo.
- Downloads usam HTTPS e validação padrão de certificados. `pip --isolated --require-hashes --only-binary=:all:` instala versões específicas do índice oficial. Nenhum get-pip remoto mutável é executado.

Runtime existente permanece funcional até reparação solicitada. Reparo não apaga modelos ou biblioteca. O novo runtime foi instalado do zero e validado com Faber/Edresson. Licenças permanecem documentadas em THIRD-PARTY-NOTICES.md e nas fichas por modelo; não se presume licença comercial universal.

## Riscos e próximos passos

Instalador sem Authenticode e sem atualização automática; comparar SHA256SUMS obtido por canal confiável. Quem adulterar pacotes e seus hashes no mesmo canal também pode adulterar a verificação. Sem pentest externo, ensaio de desastre/energia, validação em Windows limpo ou revisão criptográfica independente. `secure_delete` não elimina snapshots/backups/blocos de SSD. Backups e segurança da conta Windows continuam essenciais.

## Revisão preventiva 1.2.1 — 17/11/2026

Mecanismos e formatos mantidos. A regressão voltou a conferir PIN desligado por padrão, espera persistente, troca de PIN, sessão, AAD, payload antigo, SQL parametrizado, extração e hashes. NuGet direto/transitivo e as sete versões PyPI foram reconsultados sem registros de vulnerabilidade nas fontes consultadas. Falha de gravação foi simulada em SQLite isolado; fechamento preserva editor/autosave. Falhas de áudio/Piper têm mensagens operacionais sem incluir caminhos/diagnósticos internos. Nenhuma alteração de segurança ou teste destrutivo ocorreu na biblioteca real. Veja releases/1.3.0/RELATORIO-VALIDACAO.md para a distinção entre simulações, testes de rede e ensaios físicos pendentes.

## Complemento da 1.3.0

Os algoritmos e o formato LVR2 permanecem. Pastas têm nomes/hierarquia locais não criptografados. Metadados e associação de documentos protegidos não são persistidos em claro no catálogo; contagens/busca exigem sessão desbloqueada para incluí-los. A migração copia o estado anterior sem descriptografar payloads. Backups antigos podem conter textos que só foram protegidos depois. [Arquitetura](BIBLIOTECA-1.3.0.md) · [Migração](MIGRACAO-1.3.0.md).
