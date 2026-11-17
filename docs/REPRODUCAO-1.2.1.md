# Transporte e sincronização — 1.2.1

## Causa raiz da barra

Na 1.2.0, a barra usava o template comum de Slider com RepeatButtons `Slider.DecreaseLarge`/`IncreaseLarge`. O mouse-down apenas removia o binding; o comportamento nativo continuava aplicando `LargeChange`, em vez de posicionar o cursor diretamente. Mouse-up/teclado confirmavam percentuais contra uma duração que podia ter mudado durante a interação. O binding era restaurado antes de o novo áudio estar pronto.

`Playback.SeekAsync` mapeava o destino linearmente e chamava `RestartAsync`, que sempre definia `paused=false`. Solicitações concorrentes eram enfileiradas pelo semáforo; o pedido antigo não era descartado antes de sintetizar. A descoberta da duração real também alterava o deslocamento dentro de um trecho estimado, podendo aparentar retorno. Esses mecanismos combinados explicam saltos involuntários, retomada de uma leitura pausada e demora sob ações rápidas; não há uma subtração explícita de dez segundos nesse caminho.

## Fluxo atual

1. `Presentation/SeekSlider` trata clique, arraste e teclado antes dos RepeatButtons. Clique calcula a fração útil da pista; arrastar o indicador conserva o ponto de pega. Captura do mouse entrega o mouse-up mesmo fora da pista; perda inesperada da captura/Esc cancela.
2. O início do gesto congela a duração de referência e a prévia. Coerção da Value mantém o binding original, mas impede o temporizador de deslocar o indicador durante a interação.
3. Hover mostra o tempo/qualidade da posição; durante a seleção há uma prévia textual. Movimentos não chamam o motor. Soltar confirma uma vez; uma confirmação antiga não encerra uma interação nova.
4. `PlaybackLocation` captura índice, deslocamento, tempo lógico, qualidade estimada e fração interna. A timeline encontra o índice em O(log n). A construção da árvore é O(n).
5. O transporte usa versão crescente de solicitação. Um pedido novo cancela a sessão anterior, descarta pedidos antigos antes de iniciar síntese e agrupa entradas rápidas em 40 ms. O semáforo ainda serializa a posse do dispositivo. Parado permanece parado; pausado prepara o destino e conserva a pausa; reproduzindo continua no destino.
6. Geração pendente pode ser abandonada com observação de falhas tardias. Mesmo um motor que demore a responder ao cancelamento não mantém a interface esperando seu término. Motores reais recebem o token; Piper mata o worker cancelado e Windows cancela a fala. Trabalho não cooperativo externo pode terminar em segundo plano, mas não adquire um novo dispositivo pelo player cancelado.
7. No trecho estimado, sua fração é convertida à duração real da frase/pausa. Um ajuste de origem conserva o tempo lógico selecionado e a retomada salva. Durações futuras de outra voz/velocidade/tom são invalidadas; durações históricas já ouvidas são preservadas. O total continua aproximado até os trechos serem medidos.
8. Só o destino é preparado enquanto pausado; a frase seguinte é antecipada após começar a reprodução. O estado Preparando alimenta o indicador discreto. O destaque muda quando o destino está pronto/pausado ou começa no dispositivo, sem avançar durante a síntese.

O áudio dentro de uma pausa configurada pula corretamente a parte já narrada, sem reproduzir um WAV vazio nem mostrar temporariamente o fim do áudio como se fosse o destino. Selecionar uma frase sozinho não executa transporte. A cor de destaque e a rolagem têm preferências independentes.

## Persistência e recuperação

A posição audível é congelada antes de parar/resetar WaveOut; depois disso o player usa o valor retido em vez de consultar um dispositivo zerado. Trocas de documento cancelam preparação, interrompem áudio e salvam a posição final antes de abrir o destino. Trocas são serializadas; uma gravação antiga não substitui o documento atualmente aberto.

Fechamento desabilita edição durante a captura/gravação final. Falha cancela o fechamento, reabilita a janela e reinicia os temporizadores de salvamento/atualização. O texto permanece em memória para repetir a gravação. Isso não garante salvamento quando não existe espaço/permissão ou quando o processo termina abruptamente antes do próximo autosave.

Suspensão solicita pausa/salvamento e não retoma o som automaticamente. Falhas do dispositivo ou de síntese são recuperáveis por nova tentativa, com mensagens sem diagnóstico interno. Desconexão/reconexão física e suspensão real ainda exigem validação em hardware.

Um modelo Piper que falha fica marcado para alternativa Windows naquela sessão, evitando repetir a carga inválida a cada frase. Abrir/reparar os recursos de voz libera o worker e limpa essa marca. Texto permanece local. PIN, AES-GCM, PBKDF2, DPAPI e os formatos SQLite/cofre/documento da 1.2.0 não foram alterados.

## Testabilidade e limites

O contrato ISpeechEngine existente permite motor sintético controlado nos testes. WPF é exercitado com bindings, eventos de teclado, RangeValue acessível e coordenadas da mesma lógica de clique/arraste chamada pelos handlers nativos. Esses testes não simulam o mouse físico/driver; a execução de mouse real foi interrompida por atividade simultânea na área de trabalho.

Sincronização continua por frase/bloco. Tempo lógico não é medição acústica, e voz regenerada pode ter outra duração. Uma posição estimada não identifica palavra/amostra exata; o percentual pode variar com o total refinado. Não há cache permanente nem pré-síntese do documento inteiro. A janela mantém segmentos necessários à leitura; o processo Piper pode manter o modelo carregado entre frases para reduzir latência, sendo liberado ao gerenciar vozes/fechar.