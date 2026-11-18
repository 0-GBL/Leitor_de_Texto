[← Voltar à apresentação do projeto](../README.md)

Atualizado para **1.3.0**. A organização por pastas, os novos atalhos, as estatísticas e a privacidade do catálogo estão no [guia da biblioteca](BIBLIOTECA-1.3.0.md). Antes de atualizar ou voltar de versão, leia [migração e backup](MIGRACAO-1.3.0.md). Os procedimentos de voz e reprodução abaixo permanecem aplicáveis.


# Guia do usuário e desenvolvimento — Leitor 1.3.0

Os comandos deste guia são executados na raiz do repositório. Para a apresentação do projeto e as imagens atuais, consulte o [README](../README.md).

Aplicativo desktop gratuito de leitura de textos em áudio para Windows 10/11 x64. Seu objetivo é oferecer uma leitura confortável para estudo e uso diário, com simplicidade, bom desempenho e naturalidade da voz. **Seu tempo, seu ritmo, sua leitura.**

Construído em C#, .NET 10 LTS, WPF e MVVM, com SQLite, NAudio, System.Speech e Piper opcional. Processamento de textos e síntese locais, sem conta, serviços pagos, telemetria ou envio dos textos para servidores. Código sob GPL-3.0-or-later; veja [LICENSE](../LICENSE) e [THIRD-PARTY-NOTICES.md](../THIRD-PARTY-NOTICES.md).

## Instalação e uso

1. Execute `artifacts/Leitor-Instalador.exe` (instalação por usuário) ou extraia `artifacts/Leitor-portatil.zip` e abra `Leitor.exe`.
2. Use **Novo texto**, digite ou cole conteúdo e informe título e etiquetas separadas por vírgula. O salvamento é automático; Ctrl+S salva imediatamente.
3. Inicie a leitura e acompanhe a frase narrada na aba **Acompanhar**. A seleção manual não desloca o destaque; Enter vai até a frase selecionada.
4. Use pausar/continuar, **Reiniciar leitura**, ±10 segundos e os grupos separados de parágrafos e títulos.
5. Na biblioteca, pesquise título/conteúdo, filtre uma etiqueta exata e abra o texto selecionado. Os itens mostram data, progresso, proteção e conclusão. Textos protegidos bloqueados não expõem seus títulos ou etiquetas.

O pacote é autossuficiente: não exige SDK nem instalação manual de .NET/SQLite. `Iniciar.cmd` abre a versão publicada na pasta do projeto. Portátil e instalada usam a mesma biblioteca em `%LOCALAPPDATA%\Leitor`, e não junto ao executável. Feche o aplicativo antes de atualizar. Desinstalar preserva biblioteca, cofre e vozes. Nenhum instalador tem assinatura Authenticode; o Windows pode apresentar avisos de editor desconhecido.

## Correções da versão 1.2.1

A barra aceita clique direto e arraste, sem executar os saltos percentuais padrão do Slider. Durante a seleção, a prévia fica congelada em relação ao temporizador; soltar confirma um único destino. Esc cancela o gesto. A solicitação mais recente substitui anteriores, evitando filas de síntese; uma janela de 40 ms agrupa ações rápidas. A navegação preserva reprodução, pausa ou parada. Um trecho ainda não gerado recebe prioridade e o indicador discreto de preparação; durante a pausa só o destino é preparado.

A prévia informa **posição estimada** (≈) ou **áudio já medido**. O tempo do gesto usa a duração congelada no início, evitando mudar o destino enquanto o total é recalculado. Em trechos estimados, a fração interna da frase é convertida para sua duração real, mantendo o tempo lógico selecionado. O percentual/total ainda pode variar conforme outras durações se tornam conhecidas; navegação por palavra e precisão acústica de amostra não são prometidas.

Com foco na barra: ←/→ selecionam ±5 s; Page Up/Page Down avançam/retrocedem 30 s; Home/End selecionam início/final. Soltar a tecla confirma. Atalhos globais anteriores e edição nativa permanecem iguais; F1 inclui os comandos da barra.

A captura final do cursor ocorre antes de parar/resetar o dispositivo. Trocas de documento interrompem e salvam a posição final; cada texto conserva seu progresso. Falha de gravação impede fechar/descartar o editor e mantém o salvamento automático disponível para nova tentativa. Suspensão solicita pausa e salvamento, sem retomada automática; falhas reais de hardware/energia continuam limitadas ao que o Windows permite capturar.

Modelos Piper inválidos usam a alternativa Windows durante a sessão, sem tentar novamente o mesmo modelo a cada frase. Abrir/reparar os recursos de voz permite nova tentativa. Listas numeradas não viram frases isoladas nem títulos por causa do marcador, abreviações adicionais são preservadas e cortes internos de títulos longos não acrescentam pausas artificiais. Os quatro modos e preferências não são redefinidos. O destaque pode ser ligado/desligado em Reprodução, independentemente da rolagem.

Resultados, causa raiz, cenários de 1.000 a 50.000 palavras e limites: `VALIDACAO.md`, `DESEMPENHO-1.2.1.md` e `REPRODUCAO-1.2.1.md`.
## Interface e configurações

Cabeçalho com marca, privacidade local, novo/colar, tema rápido, ajuda e configurações. Biblioteca à esquerda, editor/leitura ao centro e narração à direita. O player mostra estado, tempo decorrido, duração, percentual e faixa já processada. Estados vazios, busca sem resultados e geração de áudio têm orientação própria.

A engrenagem organiza **Aparência** (tema/fonte), **Reprodução** (segundo plano/rolagem automática) e **Privacidade e segurança** (PIN, bloqueio inicial, alteração do PIN, bloquear sessão e proteção individual). A lateral principal contém voz, prévia, vozes offline, modo, velocidade, volume e ajustes recolhíveis de pausas/tom/transição.

O botão do cabeçalho oferece **Tema escuro** com lua no claro e **Tema claro** com sol no escuro. A troca salva a preferência sem interromper a reprodução. O claro mantém a identidade de livro: fundo #F5EFE4, painéis #FFFBF5, texto #302820, bordas #DED0BD, ação #79583D e destaque #F5DDA0. O escuro mantém a identidade anterior. Cores centralizadas em `Themes/Light.xaml` e `Dark.xaml`; geometrias em `Icons.xaml` e estilos em `Controls.xaml`. Não há biblioteca externa de interface.

Tab/Shift+Tab percorrem os controles; há nomes acessíveis, dicas com atalhos, foco visível e texto ajustável. Janelas se adaptam à área de trabalho, com painéis compactos em larguras menores e manifesto PerMonitorV2. Validação completa com Narrador/NVDA, várias escalas de DPI e teclado ABNT2 físico continua pendente.

## Vozes offline e Piper

Em **Vozes offline**, use **Instalar / reparar Piper local**. O aplicativo instala Python 3.14.10 e Piper 1.8.0 numa pasta própria, sem alterar o Python do sistema. Selecione um modelo, consulte e aceite sua ficha de licença e baixe-o. O catálogo inclui Faber e Edresson em português brasileiro e LJ Speech em inglês. Vozes SAPI do Windows também são enumeradas; idioma, gênero e suporte a tom dependem do sistema instalado.

A correção de aspas do PowerShell 5.1 permanece: a validação executa `check_piper.py`, sem código Python inline. O ambiente novo usa Python e wheel pip com SHA-256 fixado em `runtime/download-manifest.json`, dependências com versões/hashes e `pip --require-hashes --only-binary=:all:`. Extração rejeita caminhos externos e links. Instalação em estágio só substitui o runtime anterior depois de `pip check` e checagem do Piper. Modelos e configurações também têm hashes confiáveis fixados no catálogo; falhas não substituem uma voz anterior por download não verificado. Confira a auditoria em `SEGURANCA-1.2.md`.

Downloads têm até três tentativas e limite de 30 segundos sem dados. A primeira preparação exige internet (Python.org, PyPI e Hugging Face); depois disso a síntese usa modelos locais e pipes, sem enviar textos. A ficha da licença acompanha o modelo. Runtime já instalado não é atualizado silenciosamente: use **Instalar / reparar** para receber o Python atualizado. Uma falha do motor neural informa a alternativa Windows.

## Reprodução e progresso

| Modo | Velocidade | Pausas: frase / parágrafo / tópico |
| --- | --- | --- |
| Natural | 1× | 0,25 / 0,65 / 1 s |
| Rápida | 1,5× | 0,1 / 0,25 / 0,4 s |
| Estudos | 0,9× | 1 / 3 / 5 s |
| Personalizada | Configurável | Configuráveis |

Pausas de parágrafo/tópico substituem a pausa de frase no mesmo limite. Vírgulas, dois-pontos e ponto e vírgula são interpretados pelo motor; abreviações e decimais comuns são preservados. Cortes internos de frases extensas não recebem pausa artificial de fim de frase. Modo/velocidade passam a valer na próxima frase; volume muda durante o áudio. A voz pode mudar imediatamente, ao final da frase ou do parágrafo; a troca imediata tenta conservar o deslocamento.

A reprodução é progressiva, antecipando uma frase. Destaque e rolagem seguem o início efetivo do áudio, com atualização visual a cada 50 ms. Duração total e saltos em partes ainda não sintetizadas são **estimativas**, substituídas por durações reais conforme a geração. A faixa processada representa a sessão; áudio é descartado após uso e não há cache permanente em disco.

O progresso usa o cursor do dispositivo de áudio, preservando deslocamento fracionário, tempo decorrido, posição no original e durações conhecidas. Fechar pausa a saída antes da captura final. O mostrador usa segundos inteiros; o armazenamento não arredonda. A ressíntese e o driver podem produzir pequenas diferenças: não se promete retomada da mesma amostra ou latência acústica exata.

**Reiniciar leitura** começa o texto original em zero, inclusive depois de uma prévia. Pausar conserva a posição; Esc interrompe e salva. Ao desativar reprodução em segundo plano, minimizar ou mudar de janela pausa a leitura; a retomada é manual. Fechar encerra o aplicativo; não existe bandeja ou reprodução após fechar.

Documentos 1.0 em andamento mantêm a divisão antiga até edição ou reinício, preservando índices. Metadados históricos ausentes não podem ser reconstruídos. A versão 1.2 mantém o SQLite e lê documentos protegidos antigos. Novos campos JSON são opcionais. Documentos protegidos salvos pela 1.2 usam autenticação adicional de identidade e **não devem ser reabertos numa versão anterior**; para voltar à versão antiga, restaure um backup anterior compatível.

## Privacidade e segurança

PIN permanece **desligado por padrão**. Criar ou alterar exige confirmação e de 6 a 64 dígitos. PINs existentes continuam verificáveis. O bloqueio ao abrir restringe a interface; **ele não criptografa automaticamente toda a biblioteca**. Use **Proteger este texto** para criptografar cada documento. Uma pessoa que acesse o SQLite verá documentos desprotegidos normalmente; os protegidos têm título, conteúdo, etiquetas, data e progresso cifrados.

AES-256-GCM com nonce aleatório por gravação protege cada payload. Uma chave aleatória é envolvida por uma chave derivada do PIN com PBKDF2-SHA256 (600.000 iterações) e DPAPI do usuário Windows. O formato 1.2 autentica também a identidade do documento. Trocar o PIN envolve novamente a mesma chave, preservando os textos. **Bloquear sessão protegida** salva/interrompe a leitura e limpa a chave da sessão. Há descarte explícito de buffers criptográficos onde possível; strings gerenciadas e memória do sistema não têm garantia de apagamento imediato.

Tentativas incorretas recebem espera progressiva de 2 a 30 segundos, persistida com DPAPI entre sessões. Nenhuma quantidade de erros apaga os textos ou a chave. Essa limitação desestimula tentativas pela aplicação; alguém com controle da conta/arquivos pode excluir ou restaurar o estado e contorná-la. Ela não substitui a criptografia.

Não há recuperação do PIN. Documentos cifrados dependem também do perfil Windows: copiar arquivos para outra conta/máquina não basta. Guarde originais importantes e backups compatíveis. Desativar o PIN exige autenticação/confirmacão, descriptografa em transação e só depois remove a chave; se houver documento inválido, a operação é recusada para preservar os dados.

Arquivos em `%LOCALAPPDATA%\Leitor`: `library.db`, `settings.json`, `vault.json` e `auth-state.bin` quando aplicáveis, `voices\` e `piper\`. Configurações não contêm PIN. Novas pastas de dados recebem ACL restrita ao usuário, SYSTEM e administradores; pastas já existentes conservam suas permissões. Configurações/cofre usam substituição atômica e SQLite usa transações/SQL parametrizado. Um documento corrompido aparece indisponível sem impedir os demais; configurações JSON inválidas são preservadas numa cópia `.invalid-*`.

`secure_delete` reduz resíduos no SQLite, mas não remove backups, snapshots ou blocos físicos de SSD. A proteção não resiste a malware na mesma conta, administrador, inspeção de memória desbloqueada ou comprometimento do sistema. Não há promessa de segurança absoluta. Erros inesperados não exibem conteúdo de textos nem PIN; testes e pacotes excluem dados pessoais.

## Atalhos

F1 ou **Ajuda e atalhos** abre a central organizada em cinco categorias. Os atalhos atuam somente no Leitor.

| Atalho | Ação |
| --- | --- |
| Ctrl+N | Novo texto |
| Ctrl+V | Colar no campo atual; fora de campos editáveis, no editor |
| Ctrl+S | Salvar |
| Ctrl+Espaço | Iniciar / pausar / continuar |
| Espaço | Reproduzir fora de edição e de controles com uso nativo da tecla |
| Esc | Interromper preservando o progresso; fechar ajuda/configurações |
| Ctrl+R | Reiniciar leitura |
| Ctrl+← / → | ±10 segundos, fora de campos editáveis |
| Ctrl+↑ / ↓ | Parágrafo anterior / próximo, fora da edição |
| Ctrl+Shift+↑ / ↓ | Título anterior / próximo, fora da edição |
| Ctrl+T | Alternar tema |
| Ctrl+F | Focar busca da biblioteca |
| Ctrl+vírgula | Configurações (Key.OemComma, incluindo ABNT2) |
| Enter na lista de frases | Ir à frase selecionada |
| F1 | Central de atalhos |
| Tab / Shift+Tab | Próximo / anterior controle |
| Ctrl+Tab | Sair do editor que aceita tabulação |

Movimentação de cursor, seleção, colagem e espaços mantêm o comportamento nativo nos campos. Não há atalho de exclusão nem captura global.

## Desenvolvimento e validação

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Build.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Build.ps1 -Package
```

O script prepara SDK .NET 10 em `.tools/dotnet` quando necessário, restaura dependências em `.tools/packages`, compila, testa e empacota. SDK validado: 10.0.404. NuGet tem versões e locks; confira com `dotnet restore src/Leitor/Leitor.csproj -r win-x64 --locked-mode`. Primeira preparação requer internet e espaço para SDK/pacotes.

Use `LEITOR_DATA_DIR` para redirecionar biblioteca/cofre/runtime/modelos a uma pasta **isolada de testes**. Testes de reprodução usam volume zero e precisam de dispositivo de áudio. O conjunto padrão tem 34 testes; Piper exige runtime/voz local e informa quando indisponível. `LEITOR_TEST_DOWNLOADS=1` acrescenta o teste de downloads reais (35 no ambiente completo). Para repetir só um cenário, passe `--filter=<parte do nome>` ao runner; isso não substitui a regressão completa. O instalador Piper também pode ser executado isoladamente: `scripts/Install-Piper.ps1 -Destination <pasta-de-testes>\piper`.

`tests/Leitor.Bench` compara processamento, consultas da timeline, troca de recursos de tema e janela vazia entre DLLs. Métodos, resultados, testes e pendências estão em `VALIDACAO.md`; auditoria em `SEGURANCA-1.2.md`. A entrega inclui relatório e SHA256SUMS em artifacts. Nenhum commit, push ou publicação externa faz parte desta entrega.

## Arquitetura

- `src/Leitor/Core`: documentos, comandos, segmentação e timeline com somas prefixadas.
- `Services`: persistência SQLite, cofre, gravação atômica, temas, downloads, motores e reprodução.
- `ViewModels`: estado e comandos MVVM; `Presentation`: ícones/conversores/atalhos contextuais.
- `Themes`: cores, geometrias e estilos WPF; telas principal, ajuda, configurações e vozes.
- `runtime`: worker Python persistente, protocolo JSON por pipes, verificação e manifestos de dependências.
- `shared/SafeArchive.cs`: validação de extração também usada pelo instalador.
- `tests`: regressão integrada e medições; `installer`: distribuição por usuário e desinstalação registrada.

`ISpeechEngine` desacopla síntese de reprodução; textos são enviados como dados JSON, sem construir comandos de shell. A estrutura permanece modular para evoluções futuras.

## Limitações e histórico

Títulos/parágrafos usam heurísticas: linhas não vazias definem parágrafos e linhas curtas sem pontuação final podem ser títulos. Frases extensas são subdivididas preservando o original. Sincronização por frase/bloco, sem marcação por palavra. Naturalidade depende da voz; use a prévia. Memória e tempo dependem do documento/computador.

Pendentes: instalação/desinstalação em Windows limpo, assinatura digital, avaliação assistiva completa, múltiplos monitores/DPI e uso prolongado. Exportação WAV/MP3, IA/resumos, nuvem, sincronização e versão web permanecem fora do escopo.

- **1.2.1:** correção da barra, transporte com solicitação mais recente, preservação de pausa/progresso e recuperação de falhas.
- **1.2.0:** modernização completa, atalhos/ajuda, fortalecimento da segurança e otimização da timeline/temas.
- **1.1.0:** Piper/PowerShell corrigidos, tema de livro, organização das configurações, pontuação, reinício e progresso mais preciso.
- **1.0.0:** versão desktop inicial.

Veja [CHANGELOG.md](../CHANGELOG.md) para detalhes.
