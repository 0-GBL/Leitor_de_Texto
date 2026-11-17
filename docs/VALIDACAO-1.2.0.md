# Relatório de validação — Leitor 1.2.0

Data: 17/11/2026. Windows x64, .NET SDK 10.0.404, WPF, Python embutido 3.14.10, Piper 1.8.0. Trabalho realizado diretamente na arquitetura C#/MVVM existente, sem commit/push/publicação. Apenas bibliotecas temporárias e pastas .tools foram usadas nos ensaios; nenhum teste alterou documentos/configurações reais do usuário.

## Entrega e alterações

Interface redesenhada com marca/tagline, tema rápido, busca/biblioteca detalhada, estados vazios, editor/leitor confortável, painel de narração com ajustes recolhíveis e player com navegação agrupada. Mantidas as identidades bege/escura com estilos/ícones/cores centralizados e novo ícone próprio. Ajuda F1 agrupa todos os atalhos; campos preservam movimentação, seleção, colagem e Espaço nativos. Layout compacto, foco e nomes acessíveis, fonte ajustável e DPI PerMonitorV2.

Mantidas as funções anteriores: Windows/Piper, prévia, quatro modos, pausas/pontuação, troca de voz, reprodução progressiva, destaque independente, rolagem, ±10 s, títulos/parágrafos, autosave/retomada, busca/etiquetas, segundo plano e proteção opcional. Segurança revisada com espera persistente do PIN, alteração de PIN, bloqueio de sessão, AAD por documento, gravação atômica, ACL de novas pastas, tratamento de corrupção e downloads com hashes/extração segura. Detalhes e riscos em SEGURANCA-1.2.md.

## Testes executados

Execução final com downloads reais: **25 aprovados, 0 reprovados**. Execução local anterior: **24 aprovados, 0 reprovados**. Testes de áudio usam dispositivo real com volume zero. Os testes abaixo têm asserções de comportamento; não são apenas compilação.

| Área | Resultado |
| --- | --- |
| Original/offsets/CRLF/Unicode, abreviações, decimais, aspas/reticências | Aprovado; texto original preservado |
| Frase de 100 mil caracteres e cancelamento | Aprovado |
| 3,24 milhões de caracteres / 120 mil frases | Aprovado; 96 ms na regressão final, alocação 51,5 MB |
| Pausas Estudos e navegação separada por títulos/parágrafos | Aprovado |
| JSON/configurações antigos, segmentação histórica e payload protegido antigo | Aprovado |
| SQLite salvar/reabrir/excluir, etiquetas e progresso | Aprovado |
| SQL parametrizado/ID com conteúdo de injeção | Aprovado; entradas tratadas como dados |
| PIN correto/incorreto, espera entre instâncias e alteração preservando chave | Aprovado com relógio controlado |
| AES-GCM: nonces, adulteração, troca de identidade, sessão e ausência de texto simples | Aprovado |
| Documento inválido e recusa de remover a chave | Aprovado; demais documentos continuam carregáveis |
| Configurações fora da faixa e arquivo JSON inválido | Aprovado; cópia inválida preservada |
| ZIP com travessia, caminho absoluto e link; ZIP válido | Aprovado |
| Download interrompido, HTTP transitório e hash divergente | Aprovado; arquivo não verificado não publicado |
| Windows SAPI: WAV/cancelamento e dispositivo de saída | Aprovado; Maria, Daniel e Zira enumerados neste sistema |
| Reprodução: pausa/continuar/troca/cancelamento e reinício | Aprovado |
| Persistência perto de 01:12 | Aprovado; valor de 72,467 s restaurado com diferença lógica inferior a 1 ms |
| Fechamento WPF com progresso fracionário | Aprovado; 0,222 s preservados, tolerância 30 ms |
| WPF: ambos os temas/persistência, modos, seleção/destaque e reinício | Aprovado |
| Tema durante reprodução ativa e pausada | Aprovado; conteúdo/estado/posição preservados |
| Fonte normal, padding sem duplicação e layout compacto | Aprovado |
| Conclusão da leitura na biblioteca | Aprovado; estado concluído e progresso 100% persistidos |
| Atalhos contextuais, OemComma/ABNT2 e ajuda F1 roteada | Aprovado em testes de política/evento; sem captura global |
| Timeline de 120 mil segmentos com atualização parcial | Aprovado; resultados equivalentes à soma exata |
| Downloads reais Faber/Edresson, configuração e SHA-256 | Aprovado pelo serviço usado pela tela |
| Síntese neural local em processo persistente, ambos os modelos | Aprovado; carga/troca 579–954 ms, frase seguinte 95–103 ms |

### Piper e dependências

Instalação nova do runtime em estágio isolado concluída com Python 3.14.10, pip com hash, requirements com hashes, `pip check` e `check_piper.py`. O script conserva a correção do PowerShell 5.1: usa arquivo Python e não comando inline com aspas. Instalação/reparação anterior já havia demonstrado o erro original de parêntese causado pela passagem de aspas. Modelos/biblioteca ficam fora do runtime substituído.

Auditoria NuGet direta/transitiva e metadados das versões PyPI fixadas não indicaram vulnerabilidades conhecidas nas fontes consultadas em 17/11/2026; ausência de registro não prova ausência de falhas. Procedência, licenças, hashes e limites estão em SEGURANCA-1.2.md e THIRD-PARTY-NOTICES.md.

### Precisão e privacidade

O teste compara cursor lógico capturado/SQLite/restauração; não mede som físico nem garante mesma amostra. O driver e a ressíntese podem variar, e a retomada tem tolerância de 300 ms no início do dispositivo. Tempo da interface usa segundos inteiros, persistência usa frações. Duração em trechos não gerados permanece estimada. Áudio é descartado em memória, sem cache permanente.

O caminho de síntese é local e não contém HTTP; instalador/downloads não recebem conteúdo da biblioteca. Não houve desconexão física da rede ou teste de firewall. PIN de entrada e criptografia individual continuam claramente separados. Compatibilidade é de leitura dos formatos antigos; documentos protegidos gravados com AAD na 1.2 não são legíveis pela 1.1. Downgrade exige backup anterior. Nenhuma biblioteca foi recriada.

## Inspeção prática e problemas corrigidos

A habilidade computer-use foi aplicada à janela nativa e sua árvore de acessibilidade com textos sintéticos. Conferidos temas de livro e escuro, hierarquia, player, indicadores da biblioteca, nomes dos controles, foco da busca via Ctrl+F, busca sem resultados, abertura da ajuda via F1 e fechamento com Esc. A regressão WPF complementa a inspeção com eventos/bindings/reprodução reais.

Durante o desenvolvimento houve falhas intermediárias: um binding de Duration em VisualTransition bloqueava a abertura WPF; foi removido e substituído por duração literal de 90 ms. Também foram encontrados peso seminegrito herdado pelo editor e padding aplicado duas vezes no TextBox; ambos corrigidos e cobertos por asserções. O harness de benchmark inicialmente teve conflitos de resolução de assemblies/nativo SQLite; foi corrigido antes das medições válidas. Essas falhas não permanecem na suíte final.

## Comparação de desempenho com 1.1.0

Mesmo computador, Release, mesmas 120 mil frases/3,24 milhões de caracteres; DLL antiga preservada antes das mudanças. Segmentação: uma execução de aquecimento, cinco execuções e mediana; alocação média. Timeline: mil consultas de posição+duração ao fim do texto. Tema: cinquenta trocas de dicionários após aquecimento, sem incluir renderização completa ou gravação de configurações. Não são promessas de latência perceptível.

| Medida | 1.1.0 | 1.2.0 |
| --- | ---: | ---: |
| Segmentação, mediana | 107,31 ms | 95,79 ms |
| Alocação da segmentação | 51,53 MB | 51,53 MB |
| Mil consultas da timeline | 648,87 ms | 1,26 ms |
| Cinquenta trocas de recursos de tema | 4,66 ms | 0,088 ms |
| Alocação dessas trocas | 1.044 KB | 37,15 KB |
| Janela vazia pronta, mediana de 3 processos alternados | 710,30 ms | 743,41 ms |
| Working set da janela, mediana | 138,80 MB | 144,59 MB |
| CPU em 2 s ociosos, faixa de 3 processos | 15,63–171,88 ms | 46,88–171,88 ms |

Inicialização aumentou aproximadamente 33 ms (4,7%) e memória 5,8 MB (4,2%) com a nova interface. O custo das consultas extensas e a alocação de recursos de tema caíram substancialmente. CPU variou muito em ambas as versões; essa amostra curta não permite concluir ganho ou regressão sustentada. Medições de UI usam biblioteca vazia isolada, 2 s de estabilização e 2 s de amostragem, sem Piper carregado; não representam consumo máximo, perfil de bateria ou computadores diferentes. Tempos de início da narração dependem do modelo/voz/documento; as sínteses reais foram exercitadas, sem comparativo acústico formal contra a 1.1.

## Pacotes, verificação e pendências

Arquivos finais em artifacts: Leitor-Instalador.exe, Leitor-portatil.zip, Leitor-codigo-fonte.zip, RELATORIO-VALIDACAO-1.2.0.md e SHA256SUMS.txt. Distribuição win-x64 autossuficiente. `--verify-payload` do instalador valida presença dos arquivos essenciais, leitura integral da carga e caminhos seguros. O ZIP-fonte inclui shared, testes, runtime, scripts e documentação; exclui bin/obj, bancos, cofres e modelos locais. Os 60 arquivos foram inspecionados, os hashes conferidos e o código extraído compilado em Release win-x64 com RestoreLockedMode=true: zero avisos/erros após consulta NuGet autorizada. A versão portátil publicada foi aberta com biblioteca vazia isolada e encerrada normalmente. A carga interna do instalador também passou em --verify-payload.

Instalador **sem assinatura Authenticode**; pode haver aviso de SmartScreen/editor desconhecido. Não foi feita instalação/desinstalação sobre o perfil real ou numa máquina Windows limpa. Permanecem pendentes NVDA/Narrador completo, teclado ABNT2 físico, várias resoluções/DPI/monitores, uso prolongado, pentest independente e assinatura digital. A compatibilidade assistiva é parcial, baseada em navegação, foco e árvore acessível; não é certificação de acessibilidade.

Não implementados conforme escopo: exportação WAV/MP3, IA/resumos, simulados, nuvem/sincronização, serviços pagos e versão web. Heurísticas de títulos/frases e latência do dispositivo continuam limitações. Não há pendências conhecidas nos 25 cenários automatizados finais; os ensaios externos listados acima continuam necessários antes de distribuição ampla.