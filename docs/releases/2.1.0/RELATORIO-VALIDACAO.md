# Validação do Leitor de Texto 2.1.0

## Exportação individual

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
