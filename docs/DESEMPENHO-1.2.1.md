# Desempenho — 1.2.1 versus 1.2.0

Data: 17/11/2026. Mesmo computador Windows x64, Release, SDK 10.0.404. Binários 1.2.0 preservados antes das alterações. Cenários são cargas de teste, não limites de uso.

## Método

Textos sintéticos exatos de 1.000, 5.000, 10.000, 25.000 e 50.000 palavras, com frases de dez palavras. Segmentação: aquecimento seguido de três execuções, mediana e alocação média. Transporte: o mesmo proxy ISpeechEngine gera WAV local silencioso de 2 s, com atraso não cooperativo de 40 ms, volume zero e dispositivo real. Após iniciar/pausar, vinte pedidos de destino são emitidos rapidamente.

É uma comparação do comportamento do transporte diante da mesma entrada, não de velocidade intrínseca do Piper/SAPI. A 1.2.0 retirava a pausa e executava pedidos anteriores; a 1.2.1 conserva a pausa e processa o último destino. O ensaio foi escolhido para expor essa diferença e cancelamento lento; não representa toda sessão normal. Tempos de áudio são de início do dispositivo, sem microfone ou latência acústica.
| Palavras | Segmentação 1.2.0 / 1.2.1 (ms) | 20 destinos 1.2.0 / 1.2.1 (ms) | Chamadas de síntese 1.2.0 / 1.2.1 | Working set 1.2.0 / 1.2.1 (MB) |
| ---: | ---: | ---: | ---: | ---: |
| 1000 | 0.24 / 0.27 | 974.28 / 145.95 | 21 / 1 | 41.73 / 41.33 |
| 5000 | 0.29 / 0.36 | 981.15 / 135.22 | 21 / 1 | 43.86 / 42.16 |
| 10000 | 0.79 / 1.31 | 958.97 / 120.42 | 21 / 1 | 45.85 / 44.15 |
| 25000 | 1.76 / 3.48 | 983.78 / 131.94 | 21 / 1 | 49.47 / 50.61 |
| 50000 | 2.11 / 4.80 | 977.15 / 132.66 | 21 / 1 | 52.78 / 53.53 |

Em 50 mil palavras, a sequência caiu de 977,15 para 132,66 ms; as chamadas de síntese passaram de 21 para uma. A parada nesse cenário caiu de 16,99 para 4,05 ms. O primeiro áudio sintético ficou em 71,93 / 75,54 ms, respectivamente, após aquecimento dos cenários anteriores. O primeiro cenário teve 160,57 / 162,83 ms. Esses números incluem o atraso artificial de 40 ms e o dispositivo.

A segmentação da 1.2.1 ganhou checagens de listas/abreviações e teve custo maior nessa amostra: 4,80 ms contra 2,11 ms em 50 mil palavras. A alocação permaneceu em aproximadamente 3,84 MB. Não há justificativa para alegar melhoria geral da segmentação; o ganho principal está no transporte e na redução de trabalho descartado. Os testes integrados também processaram 3,24 milhões de caracteres/120 mil frases com cerca de 51,5 MB de alocação.

O working set mostrado é do harness C# após descarte/GC, com históricos anteriores no mesmo processo. Não inclui um processo Python/Piper/modelo real. CPU no intervalo curto foi quantizada em incrementos pelo Windows e variou; valores zero em uma amostra curta não significam ausência de consumo. Não há comprovação de melhora sustentada de CPU/bateria.

## Navegação repetida

Ensaio separado de 30,03 s em 50 mil palavras: 6.400 solicitações agrupadas em 320 lotes, 320 chamadas de síntese, parada em 5,59 ms. CPU do processo no intervalo: 875 ms (aproximadamente 2,9% de um núcleo). Working set após descarte: 53,64 MB, variação de -0,34 MB entre início/fim do intervalo. Não foi observado crescimento acumulado nesse ensaio curto. Isso não substitui sessões de estudo de várias horas nem prova ausência de vazamentos em todos os caminhos.

## Inicialização e limitações da medição

A primeira amostra de janela vazia variou de 1.491,86 ms na 1.2.0 para 1.900,28 ms na 1.2.1, com atividade simultânea na máquina. Por isso não foi usada como prova isolada de regressão ou ganho; a comparação foi repetida em processos alternados com janelas de teste sem ativação. Na mediana de três processos alternados sem ativação, a janela ficou pronta em 948,03 / 978,86 ms (+30,84 ms; 3,3%) e o working set em 144,99 / 144,85 MB. CPU ociosa em 2 s variou 93,75–593,75 / 62,50–578,13 ms, sem conclusão sustentada. Os resultados das três amostras estão no JSON anexo e no relatório de validação.

As medições são observacionais, sem exclusividade de CPU, sem controle térmico/energia e sem máquina Windows limpa. A inspeção prática de mouse/arraste não pôde ser concluída porque havia interação simultânea no computador. Responsividade da lógica WPF, teclado, acessibilidade, confirmação/cancelamento e ausência de síntese durante o arraste foram verificadas nos testes integrados.

Para reproduzir: compile tests/Leitor.Bench e execute `Leitor.Bench.dll <versão> <caminho-Leitor.dll> --study`; `--soak` usa 30 s de navegação; `--ui` mede janela vazia com LEITOR_DATA_DIR isolado. Resultados brutos sanitizados acompanham esta documentação. Nenhum texto real foi usado.