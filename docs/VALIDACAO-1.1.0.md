# Validação da versão 1.1.0

Data: 17/11/2026. Windows x64; .NET SDK 10.0.404; Piper 1.8.0, modelos pt_BR-faber-medium e pt_BR-edresson-low. Resultados medidos neste computador, sem garantia de desempenho equivalente em outros dispositivos.

## Regressão automatizada

19 testes na execução completa com `LEITOR_TEST_DOWNLOADS=1`; todos aprovados. A execução padrão, sem baixar novamente os modelos, contém 18 testes quando Piper e vozes já estão instalados. Testes de áudio usam volume zero e o dispositivo real do Windows. Bibliotecas e cofres dos testes são temporários; runtime/modelos ficam em `.tools/piper regression`.

| Verificação | Resultado |
| --- | --- |
| Original, offsets, CRLF, Unicode e abreviações | Aprovado |
| Linha de 100 mil caracteres, sem perda | Aprovado |
| 3,24 milhões de caracteres / 120 mil frases | Aprovado; aproximadamente 90–302 ms e 51,5 MB de alocação |
| Cancelamento do processamento | Aprovado |
| Vírgulas, decimais, aspas, reticências e exclamações | Aprovado; pontuação preservada, sem pausa de fim de frase em corte interno |
| JSON/configurações 1.0 e índices da divisão anterior | Aprovado |
| Download incompleto/configuração inválida | Aprovado; modelo anterior preservado e arquivos `.part` removidos |
| Falha HTTP transitória seguida de sucesso | Aprovado; novas tentativas concluem o download sem arquivos parciais |
| Pausas do modo Estudos | Aprovado |
| SQLite: salvar, reabrir, etiquetas, progresso e excluir | Aprovado |
| PIN, AES-GCM, adulteração e ausência de segredos em texto simples | Aprovado |
| Configurações persistentes e proteção desligada por padrão | Aprovado |
| Motor Windows: geração WAV local e cancelamento | Aprovado |
| Reprodução progressiva: pausa, continuar, alteração e cancelamento | Aprovado |
| Cursor audível, retomada perto de 01:12 e reinício em zero | Aprovado |
| Navegação independente por parágrafos e títulos, incluindo limites | Aprovado |
| WPF: janela vazia, temas, categorias, modos, destaque, reinício e fechamento | Aprovado |
| Download real de Faber e Edresson pelo serviço da interface | Aprovado |
| Síntese neural com ambos os modelos em processo local persistente | Aprovado |

### Precisão da posição

O ensaio controlado restaura 72,275 segundos, inicia o dispositivo, pausa, salva SQLite e reconstrói outro player a partir dos dados. Nas execuções, posições na faixa de 72,45–72,48 segundos foram preservadas com diferença inferior a 1 ms entre o valor capturado e o restaurado. A retomada foi verificada com tolerância de 300 ms para início do driver; isso não é uma garantia de reprodução da mesma amostra. Um teste WPF adicional fecha uma leitura pausada com deslocamento fracionário real e compara o valor persistido, com tolerância de 30 ms. O reinício deve voltar ao índice zero e a menos de 300 ms de deslocamento.

São usados `waveOutGetPosition`/NAudio, pausa da saída antes da captura final, temporizador de pausas baseado em Stopwatch e metadados opcionais de tempo/durações. O teste cobre persistência e cursor lógico; não mede a latência acústica física da caixa de som.

### Piper / PowerShell 5.1

O erro original foi reproduzido executando a instrução antiga pelo `powershell.exe`: `print("Piper pronto")` chegava ao Python como `print(Piper`, resultando em parêntese não fechado. A instrução foi removida. O script novo executa `check_piper.py` como arquivo, após `pip check`.

Instalação do zero em caminho com espaços e reparação da instalação existente concluíram com código zero, mensagem `Piper pronto: 1.8.0` e nenhuma dependência incompatível. Os scripts têm BOM/saída UTF-8 para preservar mensagens em português no PowerShell 5.1. Versões do ambiente estão fixadas em `runtime/piper-requirements.txt`.

Os downloads reais passaram pelo mesmo `VoiceDownloads` usado pela tela. Faber e Edresson produziram WAV não vazio. A geração após carga do modelo levou aproximadamente 92–196 ms para as frases de teste; a carga inicial/processo/mudança de modelo levou cerca de 0,6–2,3 s nessas execuções. Uma repetição encontrou transferência sem avanço do servidor; foi incluído limite de 30 segundos sem dados e até três tentativas. Após a correção, a execução completa de 19 testes, incluindo ambos os downloads, passou. Não foi feita avaliação subjetiva de naturalidade.

## Conferência prática da interface

A habilidade computer-use foi usada para observar a janela WPF, a árvore de acessibilidade e as capturas de tela. Foram conferidos tema de livro, painel lateral dedicado à narração, botão de engrenagem, categorias Aparência/Reprodução/Privacidade e segurança, alternância lua/sol, tema escuro preservado e grupos de navegação com dicas e nomes acessíveis. Os testes integrados da própria aplicação conferem o destaque separado da seleção, bindings dos modos e fechamento com salvamento. Um problema antigo de fechamento recursivo de janela vazia foi encontrado e corrigido.

## Compatibilidade e privacidade

Não foi alterado o caminho de dados, o formato de criptografia ou o esquema da tabela SQLite. Novos campos são opcionais dentro do payload JSON existente; documentos/configurações antigos continuam legíveis. Leituras em andamento da versão 1.0 mantêm a divisão anterior até edição ou reinício explícito, preservando seus índices. A versão 1.0 não armazenava todos os tempos históricos, portanto não é possível reconstruir retroativamente a precisão que faltava.

Não foram usados dados reais da biblioteca do usuário nos testes. A síntese só usa arquivos locais e pipes; a rede é usada pelo instalador e pelo gerenciador de downloads. Não houve envio de texto da biblioteca para servidor. O teste não desconectou fisicamente a rede nem simulou firewall: o caminho de síntese não contém chamadas HTTP.

## Pacotes e limites

Distribuição autossuficiente `win-x64`, ZIP portátil, instalador por usuário e ZIP do código-fonte, todos na pasta `artifacts`. A verificação `--verify-payload` do instalador confere arquivos obrigatórios e lê todos os arquivos da carga comprimida. Hashes estão em `SHA256SUMS.txt`. O instalador mantém a biblioteca/cofre/modelos nos diretórios existentes e registra a versão 1.1.0.

Continuam pendentes a instalação/desinstalação em Windows limpo, assinatura Authenticode, validação completa com Narrador/NVDA, escalas de DPI variadas e uso prolongado. A identificação de títulos/parágrafos continua heurística. Tempos totais e saltos em áudio ainda não gerado são estimativas. A ressíntese neural e o driver podem gerar pequenas diferenças no som retomado. A proteção é individual por documento; o PIN de entrada sozinho não cifra toda a biblioteca. Exportação e serviços de IA na nuvem continuam fora do escopo.