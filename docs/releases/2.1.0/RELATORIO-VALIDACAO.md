# Validação do Leitor de Texto 2.1.0

## Refinamento da exportação e da biblioteca

A edição anterior reproduziu a Faber e exportou uma aula fictícia de 38 min 30 s no mesmo ambiente de teste. A falha da aula relatada não se repetiu com dados fictícios, e a mensagem original agrupava as causas. A correção valida os arquivos da voz e testa o primeiro trecho antes de iniciar a codificação. Um seletor permite trocar a voz apenas para a exportação e abrir o gerenciador de vozes. O diagnóstico distingue voz ausente, modelo inválido, inicialização, síntese, PCM, codec e destino. O código técnico contém etapa, número do trecho e HRESULT, sem conteúdo do documento.

## Refinamento visual com a referência aprovada

A referência distribui a ilustração em duas faixas, com o abajur e a estante no cabeçalho e os livros empilhados e o porta-lápis no rodapé. A nova biblioteca adota essa composição, com iluminação quente, cartões para a árvore e contorno azul na pasta selecionada no tema escuro. A versão anterior concentrava a decoração em um pequeno desenho vetorial no rodapé.

O recurso local `src/Leitor/Assets/StudyRoom.jpg` foi gerado para o projeto a partir da composição de referência. O WPF recorta as duas faixas de um único JPEG de 1152 × 768 pixels e cerca de 85 KiB. O rodapé é ocultado quando falta altura, e o cabeçalho pode virar um botão compacto. O tema claro usa superfícies em bege e mantém a iluminação do estudo nas faixas.

As capturas foram renderizadas pela janela WPF real com dados fictícios em [tema escuro com subpasta e lista](../../images/biblioteca-estudos-escura-2.1.0.png), [tema bege com grade](../../images/biblioteca-estudos-clara-2.1.0.png) e [janela compacta em tema escuro](../../images/biblioteca-estudos-compacta-2.1.0.png). O painel continua redimensionável e mantém pesquisa, comandos e opções de exibição. A largura observada depende da preferência do usuário, e a árvore usa os nomes, ícones e contagens da biblioteca em vez dos elementos ilustrativos da referência.

## Exportação individual

### Correção reproduzida no trecho 11

A aula foi consultada em uma conexão SQLite somente leitura, com autorização do usuário. O segmentador gerou 467 trechos. Na Faber, o trecho 11 não gerava fonemas. A falha ocorreu tanto isoladamente quanto depois dos dez trechos anteriores. O worker distribuído anteriormente reproduziu `PiperFailure` com HRESULT `0x80131509`. No Python, `wave.Wave_write.close` chamou `_ensure_header_written`, que lançou `wave.Error: # channels not specified`. O método do Piper configurava o cabeçalho quando recebia o primeiro bloco de áudio. Para o trecho sem fonemas, esse bloco nunca chegava.

O worker agora configura o formato PCM antes da síntese. Quando um separador sem letras ou números não gera áudio, ele produz 100 ms de silêncio na posição original. Se um trecho com letras ou números não gerar áudio, a exportação interrompe com erro. A correção mantém o segmentador, os índices e as pausas configuradas. Nenhuma repetição automática foi adicionada, pois a falha original era determinística.

A reprodução e a exportação usam o mesmo worker. No teste isolado, ambas receberam o mesmo WAV de 100 ms, sem troca de voz. A exportação real com Faber, velocidade 2 e 467 trechos gerou um MP3 de 28 min 38 s em cerca de 63 s. Todos os quadros foram decodificados e o MP3 temporário foi apagado. O teste não gravou alterações na biblioteca ou no progresso.

Quando há falha de síntese na exportação, o último diagnóstico fica em `%LOCALAPPDATA%/Leitor/diagnostics/mp3-synthesis.json` no perfil padrão. Ele registra etapa, número do trecho, motor, voz, operação do worker, tipos de exceção e frames. Somente mensagens de origem conhecida e sem conteúdo do documento são registradas. Títulos, texto, áudio, caminhos pessoais e stderr bruto ficam fora do arquivo.

O diálogo **Exportar áudio** usa uma cópia em memória do documento aberto e das opções de narração. O player permanece separado da exportação. O diálogo pede que a reprodução seja encerrada antes de exportar e mantém uma exportação por janela. Documentos protegidos exigem PIN e confirmação explícita sobre o acesso ao MP3.

A exportação reutiliza o segmentador e os motores Piper e Windows. O pipeline mantém o WAV de um trecho por vez em memória, faz a conversão para PCM mono e entrega áudio e pausas ao codificador do Windows Media Foundation por demanda. A integração usa NAudio.Wasapi 2.2.1 sob licença MIT. O codec faz parte do Windows. [Especificação Microsoft](https://learn.microsoft.com/en-us/windows/win32/medfound/mp3-audio-encoder) · [Licenças e dependências](../../../THIRD-PARTY-NOTICES.md).

O formato de saída é MP3 mono a 44,1 kHz e 96 kbps. A alternativa de 64 kbps reduziria o volume por hora de cerca de 43,2 MB para 28,8 MB, com menos margem para preservar detalhes da fala. A suíte verifica 96 kbps e não inclui uma avaliação auditiva comparativa entre bitrates.

O arquivo temporário fica em uma pasta com ACL restrita no volume escolhido. O arquivo completo é movido para o nome final sem sobrescrever outro. Cancelamento e falhas gerenciadas limpam os temporários. Uma interrupção forçada pode deixar um arquivo incompleto nessa pasta privada. Exclusão lógica não garante apagamento físico em SSD.

## Ensaio em Windows x64

A primeira execução direcionada usou perfis de teste e os motores instalados localmente. Os arquivos curtos foram decodificados pelo Media Foundation e verificados quanto a duração, volume de sinal, canais, amostragem e bitrate.

| Ensaio | Duração do MP3 | Tempo observado | Tamanho |
| --- | ---: | ---: | ---: |
| Windows | 11,10 s | 0,68 s | 133.456 bytes |
| Piper | 8,15 s | 3,71 s | 98.087 bytes |
| Fluxo sintético | 20 min | 4,3 s | 14.379.651 bytes |
| Fluxo sintético | 30 min | 6,1 s | 21.568.635 bytes |
| Fluxo sintético | 40 min | 9,3 s | 28.757.932 bytes |
| Fluxo sintético | 60 min | 12,4 s | 43.136.526 bytes |

A variação do conjunto de trabalho do processo de teste foi de +31,9 MiB no caso Windows e +6,3 MiB no caso Piper. Os tempos de CPU no mesmo processo foram 0,41 s e 0,27 s. Essas medições incluem a decodificação de verificação, excluem o processo filho do Piper e não representam pico de RAM. O armazenamento intermediário tem aproximadamente o tamanho do MP3. Os fluxos longos medem o pipeline de codificação e a ordem dos blocos com um motor sintético.

## Cobertura e limites

- Os cenários incluem texto com pontuação e parágrafos, pausas, PIN cancelado, preservação do progresso, resposta do dispatcher durante a exportação, destino existente, texto vazio e cancelamento em andamento.
- A falha de disco foi simulada. A suíte não enche o volume. A reprodução em aparelhos Android e iPhone e o uso em instalações sem Media Foundation ficam fora do ensaio.
- O portátil e o instalador x64 são publicados em `artifacts/2.1.0/` após a suíte Release. A carga incorporada e os SHA-256 são verificados pelo script.
