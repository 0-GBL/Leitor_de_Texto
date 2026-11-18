# Desempenho da biblioteca — 1.3.0

Execuções reais em 17/11/2026, Windows x64, .NET 10.0.404, Release. Dados exclusivamente fictícios, sem amostragem da biblioteca pessoal. Valores são desta máquina e execução, não garantias ou limites de capacidade. Nenhuma nova dependência de produção.

## Comparação com a 1.2.1

`tests/Leitor.Bench --library` carrega por reflexão o DLL preservado da 1.2.1 e o DLL Release 1.3.0, em processos separados. Para cada tamanho cria banco legado com documentos de 2.048 caracteres e sem pastas. Mede construção/migração, reabertura e mediana de três consultas. A 1.2.1 usa Load e devolve todos os corpos; a 1.3.0 usa Query e devolve a primeira página de 100 resumos. **São estratégias de abertura diferentes, não operações de trabalho idêntico.**

| Documentos | Lista 1.2.1 (ms) | Página 1.3.0 (ms) | Alocação 1.2.1 (MB) | Alocação 1.3.0 (MB) | Primeira migração 1.3.0 (ms) |
| --- | ---: | ---: | ---: | ---: | ---: |
| 140 | 4.94 | 12.24 | 0.956 | 0.076 | 87.53 |
| 500 | 11.85 | 8.09 | 3.206 | 0.064 | 83.87 |
| 1000 | 28.33 | 4.32 | 6.408 | 0.064 | 100.53 |
| 5000 | 135.73 | 10.14 | 32.108 | 0.066 | 554.88 |
| 10000 | 167.55 | 5.87 | 64.222 | 0.064 | 1278.08 |

Em 10 mil documentos, a carga antiga reteve 20.480.000 caracteres de conteúdo na coleção. A página nova reteve zero caracteres de corpo e 100 resumos, com cerca de 0,064 MB alocados na consulta. O corpo do texto aberto no editor continua sendo necessário. A amostra de 140 ficou mais lenta na versão nova; aquecimento/cache e custo fixo afetam bibliotecas pequenas. Não há ganho universal de latência em toda execução.

A migração inicial inclui backup consistente, catálogo e índices e custa mais que simplesmente abrir o banco antigo. A execução de 10 mil levou 1.278,08 ms; reabrir o armazenamento levou 2,33 ms, antes de carregar a interface/voz. Não confundir esses tempos com início completo do aplicativo.

Dados: [1.2.1](library-compare-1.2.1.json) e [1.3.0](library-compare-1.3.0.json).

## Hierarquia, busca e áudio simultâneo

O cenário automatizado cria documentos de 2.048 caracteres, 14–200 pastas e até oito níveis. Mede página inicial, busca com correspondências espaçadas, carga de pastas/contagens e movimentação seguida de nova busca. O transporte executa um WAV controlado em volume zero durante busca e movimentação; não é uma medição de naturalidade do Piper. Um cenário separado percorre/exclui 1.100 subpastas encadeadas.

| Documentos | Pastas | Página (ms) | Busca (ms) | Pastas (ms) | Mover + buscar (ms) | Alocação acumulada (MB) | Working set (MB) | CPU do cenário (ms) |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 140 | 14 | 4.42 | 12.83 | 1.53 | 24.48 | 3.00 | 169.11 | 46.88 |
| 500 | 14 | 4.56 | 49.09 | 2.43 | 60.24 | 6.44 | 172.24 | 203.12 |
| 1000 | 20 | 5.87 | 85.43 | 2.81 | 104.47 | 11.25 | 176.23 | 265.62 |
| 5000 | 100 | 6.20 | 373.76 | 7.67 | 380.75 | 50.27 | 150.15 | 765.62 |
| 10000 | 200 | 6.10 | 722.07 | 15.99 | 763.17 | 99.61 | 155.14 | 1406.25 |

Alocação é o total de bytes alocados durante **várias operações**, incluindo varreduras de busca, não memória retida nem pico. Working set é uma amostra do processo de testes, que já executou WPF, criptografia e áudio; inclui runtime e testes anteriores. Não mede isoladamente o aplicativo nem o processo Python. CPU é tempo de processador acumulado nessas operações, não percentual. Mover+buscar não isola o custo da transação. O GC pode diminuir o working set em tamanhos maiores. Dados: [escala](library-scale-1.3.0.json).

## Responsividade e limites

Consultas e estatísticas extensas executam em tarefas com cancelamento; a resposta antiga não substitui uma busca nova nem reaparece após bloquear a sessão. Listas têm reciclagem WPF e páginas de 100. Não se conserva simultaneamente o conteúdo de todos os documentos. Busca de conteúdo ainda faz varredura; na amostra de 10 mil levou cerca de 0,72 s e alocou objetos transitórios. Não há FTS persistente de textos protegidos: buscas desbloqueadas decifram um documento por vez e podem custar mais. Tamanho de cada texto, disco, antivírus e número de protegidos mudam esses valores.

O áudio permaneceu em reprodução durante as operações verificadas; não foi medido jitter acústico ou carga Piper simultânea em todas as escalas. A árvore armazena metadados de pastas e só materializa as linhas expandidas; expandir dezenas de milhares de pastas ainda pode custar tempo de interface. A migração inicial é síncrona. Não há promessa de capacidade ilimitada ou estabilidade por horas. Mouse físico/DnD, Windows limpo, múltiplos DPI, leitor de tela e sessões longas permanecem pendentes. Os ensaios de transporte anteriores estão preservados em [DESEMPENHO-1.2.1.md](DESEMPENHO-1.2.1.md).

## Reproduzir

Execute o runner em Windows com uma saída de áudio e `LEITOR_DATA_DIR` apontando para **uma pasta de teste**, nunca a biblioteca pessoal. `LEITOR_TEST_DOWNLOADS=1` habilita o download real; `LEITOR_BENCH_OUTPUT` define o JSON de escala. O runner cria suas fixtures em diretórios próprios. O benchmark usa `dotnet run --project tests/Leitor.Bench -c Release -- 1.3.0 <caminho-absoluto-do-Leitor.dll> --library`. Para comparar versões, preserve uma distribuição anterior antes de publicar por cima dela.
