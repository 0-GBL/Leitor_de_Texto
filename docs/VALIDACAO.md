# Relatório de validação — Leitor 1.2.1

Data: 17/11/2026. Windows x64, SDK .NET 10.0.404, Python opcional 3.14.10, Piper 1.8.0. Projeto C#/WPF/MVVM existente preservado. Sem commits, push ou publicação externa. Todos os documentos/cofres/modelos de ensaio ficam em pastas temporárias ou .tools; a biblioteca real não foi usada.

## Causa raiz e solução

A barra anterior removia o binding no mouse-down, mas mantinha os RepeatButtons padrão do Slider: clicar fora do indicador executava LargeChange em vez de posicionar diretamente. Mouse-up confirmava contra uma duração que podia ter mudado. SeekAsync chamava um reinício que sempre retirava a pausa e enfileirava pedidos antigos; cada reinício podia sintetizar áudio descartado. Durações estimadas corrigidas durante a geração também deslocavam o cursor. Não havia uma subtração fixa de dez segundos nesse fluxo; o salto percentual podia equivaler a vários segundos conforme o texto.

SeekSlider agora trata clique/arraste/teclado antes do comportamento padrão, congela a prévia/duração do gesto, mostra tempo estimado ou medido e confirma um destino ao soltar. O transporte conserva pausa/parada/reprodução, cancela a sessão anterior, descarta solicitações antigas e agrupa entradas rápidas em 40 ms. Destinos não gerados são priorizados com indicador de preparação; enquanto pausado, só o destino é sintetizado. O mapeamento da timeline passa a O(log n), com construção O(n), e o tempo lógico escolhido fica ancorado durante a calibração da frase. Detalhes em REPRODUCAO-1.2.1.md.

## Correções adicionais

- Captura da posição antes de parar/resetar o dispositivo: o teste detectou cursor zerado durante uma etapa intermediária; a ordem foi corrigida e a regressão passou.
- Selecionar um ponto dentro de uma pausa configurada não retrocede ao fim do áudio ao continuar.
- Mudança de voz/velocidade/tom invalida durações futuras incompatíveis, mantendo o histórico já ouvido e a posição atual. Volume permanece imediato.
- Troca de documentos cancela preparação, interrompe e salva o cursor final. Trocas são serializadas e uma gravação antiga não substitui o documento aberto.
- Falha de gravação cancela o fechamento, reabilita a janela e mantém autosave utilizável. Editor não é descartado. Edição fica desabilitada durante a captura final de fechamento.
- Suspensão solicita pausa/salvamento sem retomada automática. Falhas isoladas de síntese/dispositivo geram mensagem de recuperação.
- Modelo Piper inválido usa Windows na sessão sem tentar carregar o mesmo modelo a cada frase; gerenciar/reparar vozes permite nova tentativa.
- Listas numeradas e marcadores deixam de receber divisão/pausa indevida de título; abreviações adicionais e cortes internos de títulos longos foram tratados. Original, datas e decimais preservados. Modos e preferências existentes não são redefinidos.
- Destaque configurável independentemente da rolagem, ajuda/tooltip da barra e refinamentos discretos. Design e temas 1.2.0 preservados.

## Testes e resultados

O conjunto local completo contém **34 testes** no ambiente com Piper/vozes preparados. Todos passaram após as correções. O ensaio opcional de downloads reais completa **35 cenários distintos**. Os 25 cenários da versão anterior foram executados novamente, junto aos novos. A execução completa anterior de 34 testes (antes do caso adicional de pausa) passou integralmente; uma repetição posterior atingiu o prazo de três minutos no download externo, com 34 aprovados/uma falha de rede. A reexecução isolada do download usa prazo de teste de dez minutos, preservando os limites/repetições normais do aplicativo. A reexecução isolada concluiu Faber e Edresson: um teste aprovado, zero reprovados. Resultado consolidado: 35 cenários distintos aprovados, sem falha persistente; a ocorrência de timeout permanece documentada.

| Verificação | Resultado local |
| --- | --- |
| Offset/CRLF/Unicode, frases gigantes, original e cancelamento | Aprovado |
| Pontuação: vírgulas, finais, dois-pontos, ponto e vírgula, perguntas, exclamações, reticências, abreviações, decimais, datas, listas, títulos e parênteses | Aprovado em segmentação/preservação; naturalidade subjetiva não certificada |
| Texto de 3,24 milhões de caracteres/120 mil frases | Aprovado; alocação aproximadamente 51,5 MB |
| Materiais de 1.000 / 5.000 / 10.000 / 25.000 / 50.000 palavras | Aprovado, sem limites artificiais de tamanho |
| SQLite: salvar/reabrir/excluir, etiquetas, histórico de progresso e JSON antigo | Aprovado |
| PIN/AES-GCM/PBKDF2/DPAPI, espera persistente, troca de PIN, nonces, identidade, sessão e payload antigo | Aprovado; formatos criptográficos não alterados |
| SQL parametrizado, corrupção isolada e preservação da chave | Aprovado |
| Configuração inválida, gravação atômica e extração segura | Aprovado |
| Download interrompido/transitório/hash divergente | Aprovado; modelo anterior preservado |
| Windows SAPI, WAV/cancelamento, pausa/continuar/troca/reinício e dispositivo real | Aprovado com volume zero |
| Retomada em torno de 01:12 | Aprovado; exemplo 72,467 s com erro lógico de persistência inferior a 1 ms |
| Fechamento WPF com deslocamento fracionário | Aprovado; 0,222 s capturados/salvos, tolerância de 30 ms |
| Navegação de parágrafos/títulos e limites | Aprovado |
| Clique direto/arraste na lógica WPF do controle, confirmação/cancelamento e binding congelado | Aprovado com coordenadas programáticas dos mesmos handlers |
| Navegação parada, pausada, em reprodução e em intervalo de silêncio | Aprovado |
| Destino estimado: somente uma síntese pausada e tempo lógico ancorado | Aprovado |
| Cem destinos consecutivos e mudança imediata mantendo pausa | Aprovado; último pedido prevalece |
| Motor com erro e motor lento que ignora cancelamento | Aprovado; falha recuperável e transporte não fica aguardando o motor antigo |
| Barra por teclado e RangeValue acessível; ajuda F1, modos, temas e destaque/seleção separados | Aprovado |
| Busca por conteúdo/título, etiqueta e progresso independente de dois documentos | Aprovado |
| Falha SQLite por trigger de teste, falha ao fechar e autosave após recuperar escrita | Aprovado; editor e janela preservados |
| Solicitação de suspensão pelo serviço da aplicação | Aprovado; pausa e salvamento, sem suspender fisicamente o computador |
| Piper com modelo inválido, alternativa Windows e aviso sem caminho técnico | Aprovado |
| Faber/Edresson: duas frases locais por modelo e worker persistente | Aprovado |

A barra é testada dentro da janela WPF com layout/bindings reais: início de clique em 60%, arraste até 35%, ticks concorrentes, liberação única, cancelamento, teclado e interface de acessibilidade. O teste de ponteiro chama a mesma lógica de coordenadas dos handlers nativos, sem injetar movimento físico. A habilidade computer-use foi usada para observar a janela e iniciar a conferência visual; a atividade simultânea do usuário impediu concluir o clique/arraste físico. Nenhuma entrada foi enviada ao outro aplicativo. As janelas automatizadas de teste usam posição fora da área visível e não pedem ativação para reduzir interferência.

Durante o desenvolvimento também houve bloqueio do executável por uma prévia antiga isolada; somente esse processo de teste foi encerrado após conferir PID/caminho. Consultas NuGet inicialmente falharam pela restrição de rede e foram refeitas com acesso autorizado. Nenhuma falha de gravação/disco foi provocada na biblioteca real.

## Desempenho

Comparação controlada descrita em DESEMPENHO-1.2.1.md, com resultados JSON sanitizados anexos. Mesmo texto/contrato de motor sintético, atraso não cooperativo de 40 ms e áudio de 2 s, volume zero. Sequência de vinte pedidos emitidos após pausar; a versão anterior retirava a pausa e executava a fila, enquanto a nova a mantém e prepara só o destino final.

| Medida, 50 mil palavras | 1.2.0 | 1.2.1 |
| --- | ---: | ---: |
| Segmentação, mediana | 2,11 ms | 4,80 ms |
| Alocação da segmentação | 3,84 MB | 3,84 MB |
| Vinte pedidos de destino | 977,15 ms | 132,66 ms |
| Chamadas de síntese nesses pedidos | 21 | 1 |
| Parada após navegação | 16,99 ms | 4,05 ms |
| Primeiro áudio sintético, cenário aquecido | 71,93 ms | 75,54 ms |
| Working set do harness após descarte | 52,78 MB | 53,53 MB |
| Janela vazia pronta, mediana de 3 processos sem ativação | 948,03 ms | 978,86 ms |
| Working set da janela, mediana | 144,99 MB | 144,85 MB |

A segmentação adicionou verificações e teve custo maior nessa amostra; não se alega ganho geral de processamento. Inicialização aumentou 30,84 ms (3,3%) na mediana, com memória estável. CPU ociosa variou bastante nas duas versões, sem conclusão sustentada. A máquina tinha atividade simultânea: medições não são uma garantia nem um laboratório exclusivo.

Ensaio de 30,03 segundos com 50 mil palavras: 6.400 solicitações em 320 lotes, 320 sínteses, parada em 5,59 ms, CPU 875 ms no intervalo e working set final 53,64 MB (variação -0,34 MB). Não foi observado crescimento acumulado nessa amostra curta. Não substitui estudo por horas. Consumo do processo Python/modelo real não está incluído nesse harness.

## Segurança, compatibilidade e privacidade

PIN permanece desligado por padrão para instalações novas e respeita preferências existentes. AES-256-GCM, PBKDF2-SHA256/600 mil, DPAPI, AAD, rate limit, consultas parametrizadas, ACL de novas pastas, hashes e extração segura foram preservados. Não houve alteração do schema SQLite, envelope do cofre ou formato protegido LVR2. Novo campo opcional de preferência controla destaque. A restrição de downgrade da 1.2.0 permanece: payloads LVR2 não devem ser abertos na 1.1 sem backup compatível.

Auditoria NuGet direta/transitiva e metadados PyPI das sete versões fixadas foram reconsultados em 17/11/2026, sem vulnerabilidades conhecidas reportadas nas fontes consultadas. Isso não é prova de ausência de falhas. Python 3.14.10 e os hashes fixados permanecem; fontes e limites em SEGURANCA-1.2.md e THIRD-PARTY-NOTICES.md. Texto é processado por motores locais; não existe envio de textos para serviços externos. Não foi realizado bloqueio físico da rede/firewall ou pentest independente.

## Entrega e pendências explícitas

Arquivos em artifacts: Leitor-Instalador.exe, Leitor-portatil.zip, Leitor-codigo-fonte.zip, RELATORIO-VALIDACAO-1.2.1.md e SHA256SUMS.txt. O pacote portátil é autossuficiente win-x64. O instalador tem --verify-payload para conferir a carga; fontes incluem código compartilhado, testes, scripts e documentação e excluem biblioteca/modelos/cofre reais. Verificações finais aprovadas: --verify-payload do instalador com código zero; SHA-256 dos três pacotes conferidos; 68 arquivos no ZIP-fonte, sem bancos/cofres/modelos locais; código extraído compilado em Release win-x64 com RestoreLockedMode=true, zero avisos e erros. A DLL publicada também abriu/fechou a janela WPF em perfil vazio isolado e fora da área visível (smoke test de recursos/SQLite, sem interromper o uso da área de trabalho). A instalação real em Windows limpo permanece pendente.

Instalador **sem assinatura Authenticode** e sujeito a avisos de editor desconhecido/SmartScreen. Pendentes: clique/arraste com mouse físico sem atividade simultânea, desconectar/reconectar fones e trocar saída real, suspensão/retomada física, término forçado/perda de energia, disco fisicamente cheio, máquina Windows limpa, múltiplos monitores/DPI, ABNT2 físico, avaliação completa NVDA/Narrador e uso de várias horas. Falha de escrita e suspensão foram verificadas por simulação do caminho da aplicação, não pelo evento físico.

Tempo lógico e persistência não são medição de latência acústica. Total/percentual podem variar enquanto trechos não gerados são estimados; não há alinhamento por palavra/amostra. Sem cache permanente. Bibliotecas extensas podem consumir memória conforme o tamanho e Piper pode manter o modelo carregado para reutilização. Recursos de IA, exportação de áudio, nuvem, sincronização e versão web permanecem fora do escopo.