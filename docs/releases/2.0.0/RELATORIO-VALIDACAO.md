# Relatório do Leitor de Texto 2.0.0

19/11/2026 · Windows x64 · WPF / .NET 10

## Retorno à distribuição estável

- Referência de recuperação: ZIP de código da distribuição 2.0.0 refinada, conferido pelo manifesto SHA-256 existente antes da limpeza.
- A interface de clonagem, o worker Python, a preparação de referências, a configuração de compilação e os testes exclusivos do protótipo foram removidos. Os motores Piper e Windows e os relatórios históricos foram preservados.
- A versão 1.3.0 foi arquivada em `backups/versoes-anteriores/1.3.0/`. O ponto de recuperação dos arquivos modificados e removidos está em `backups/recovery-antes-da-limpeza-2.0.0/`. Compare seus arquivos com o manifesto antes de restaurar caminhos individuais. A distribuição anterior fica no diretório `previous` da execução de empacotamento em `.tools/release-staging/`.
- A suíte usa perfis fictícios e vozes já instaladas localmente. A verificação do instalador lê sua carga incorporada. Uma instalação completa em uma máquina limpa está fora desta validação.

## Refinamento da 2.0.0

- A grade usa cartões e escolhe as colunas pela área disponível. Nomes longos quebram em várias linhas. Pastas têm ícones dourados por padrão e mantêm a cor personalizada quando definida. Documentos usam um ícone de página ou de escudo.
- A árvore lateral pode ser recolhida e redimensionada. A largura e o estado são salvos no perfil. O topo contém o acesso à raiz, e o diretório atual aparece no título e com destaque no caminho clicável.
- A caixa de criação mostra o destino antes de salvar. Pesquisa e navegação continuam visíveis. Filtros, seleção de grade/lista, densidade e tamanho dos ícones ficam em **Opções**.
- Os três pacotes históricos da 1.3.0 foram movidos após conferência do executável e de SHA-256. A raiz de `artifacts/` contém apenas as pastas de versão. Os relatórios da 1.2.0 à 2.0.0 foram centralizados em `docs/releases/`, incluindo a conferência final histórica da 1.3.0.
- A geração publica em uma pasta de trabalho ignorada pelo Git. O script testa o instalador com `--verify-payload`, cria o manifesto SHA-256, troca a pasta de versão e confere os hashes no destino. A distribuição anterior fica em `.tools/release-staging/` como backup. Os diretórios históricos de compilação e os relatórios soltos foram retirados da distribuição e preservados em `.tools/`.
- O refinamento removeu o ícone antigo de subpasta e o botão redundante de abertura de seleção. Clique duplo, Enter e menu de contexto abrem o item. Nenhuma dependência nova foi acrescentada.

Na validação deste refinamento, a suíte Release cobre 51 cenários. A regressão WPF verifica os diálogos de criação na raiz e em subpasta, a navegação, o nome longo, a virtualização da lista, a grade em 800 × 640 e 1280 × 800, o recolhimento e a recuperação da largura. A persistência das preferências ganhou um cenário próprio. Os testes usam perfis fictícios e vozes locais. As capturas atualizadas ficam [na demonstração do README](../../../README.md#demonstração).

A verificação do instalador cobre a carga incorporada. Instalação em uma máquina limpa, arraste físico, leitores de tela e múltiplos DPIs ficam fora desta rodada.

## Registro da entrega original da 2.0.0

## Comparação com a 1.3.0

| Área | 1.3.0 | 2.0.0 |
| --- | --- | --- |
| Interface | Biblioteca, editor e narração lado a lado | Biblioteca e leitura em telas próprias. Cabeçalho compacto, temas bege/escuro e ações secundárias em menus e expansores |
| Biblioteca | Árvore de pastas e lista de textos | Árvore lateral e conteúdo imediato do nível aberto. Caminho clicável, histórico, busca de pastas e documentos, lista e grade |
| Organização | Criação, movimentação, renomeação e exclusão | Cópia de textos e subárvores além das operações anteriores. Arraste para mover, Ctrl+arraste para copiar e opções equivalentes no menu |
| Leitura | Editor, aba Acompanhar e controles de narração | Modo foco com texto, destaque, transporte e ajustes de narração. Entrada e saída por F11. Recentes e retomada do progresso |
| Personalização | Tema, fonte, largura e preferências de filtros | Cores de pastas, tamanho de ícone, densidade e visualização persistentes |
| Armazenamento | Esquema SQLite 2, catálogo e cofre | Mesmo esquema e formatos. Cópia transacional com novas identidades, progresso preservado e proteção recifrada |

[Capturas atuais e instruções de uso](../../../README.md#demonstração) · [Visual anterior](../../images/interface-clara.png)

## Arquitetura e revisão

- A janela WPF alterna as áreas sobre o mesmo ViewModel e a mesma sessão de reprodução. A preparação do modo foco reaproveita a segmentação existente. A biblioteca usa o catálogo paginado, com lista virtualizada e uma grade opcional.
- As cópias registram pastas e documentos na mesma transação. A origem permanece independente e as cópias protegidas são cifradas com suas novas identidades. O cofre precisa estar desbloqueado para conferir a associação de documentos protegidos com uma subárvore. Uma falha aborta a cópia do banco inteiro.
- A leitura individual usa a mesma consulta para obter payload e atribuição de pasta. A leitura integral reaproveita a rotina de validação e decodificação e elimina a consulta adicional por documento comum. A resolução de caminhos reaproveita o mapa de pastas por página.
- O caminho de salvamento combina persistência do documento e catálogo na infraestrutura transacional existente. O novo histórico guarda até 30 IDs de documentos não protegidos nas preferências locais. Um texto é retirado desse histórico antes de ser protegido.
- Foram retirados o armazenamento duplicado de resumos, notificações de propriedades sem consumidores, a ponte `FindName` antiga, a apresentação anterior da biblioteca e o código redundante de leitura de payload. Os bindings e consumidores indiretos do benchmark foram ajustados.
- A dependência agregadora `NAudio` foi substituída por `NAudio.WinMM` 2.2.1, que traz `NAudio.Core`. Os pacotes ASIO, MIDI, WASAPI e WinForms deixaram de compor a distribuição. A saída permanece em WaveOut e a síntese continua em System.Speech ou Piper. SQLite, formatos de voz, PIN e migração foram mantidos.

### Alterações por arquivo

| Área | Arquivos modificados |
| --- | --- |
| Aplicação | `App.xaml`, `App.xaml.cs`, `MainWindow.xaml`, `MainWindow.xaml.cs`, `Core/Models.cs`, `Themes/Controls.xaml`, `Presentation/LibraryPanel.xaml`, `Presentation/LibraryPanel.xaml.cs`, `Presentation/OrganizationDialogs.cs`, `Presentation/Shortcuts.cs` |
| Estado e dados | `ViewModels/MainViewModel.cs`, `ViewModels/LibraryViewModel.cs`, `Services/Storage.cs`, `Services/LibraryOrganization.cs` |
| Distribuição | `Directory.Build.props`, `Leitor.csproj`, `app.manifest`, `installer/Leitor.Setup.csproj`, `installer/Program.cs`, `Iniciar.cmd`, `scripts/Build.ps1`, arquivos de lock do app e dos testes |
| Testes | `tests/Leitor.Tests/Program.cs`, `Program.Library.cs`, `Program.LibraryUI.cs`, `tests/Leitor.Bench/Program.cs` |
| Documentação | `README.md`, `CHANGELOG.md`, `THIRD-PARTY-NOTICES.md`, `docs/GUIA-DO-USUARIO.md`, `docs/images/README.md` |

Criados este relatório e três PNGs em `docs/images/`. A entrega gera ZIP portátil, instalador, ZIP do código e SHA-256 em `artifacts/2.0.0/`. Os arquivos históricos foram preservados.

### Linhas físicas

Base `97cf374`. Foram contadas linhas de arquivos versionados em C#, XAML, PowerShell, Python e CMD, incluindo espaços em branco e comentários. Imagens, documentos, XML de projeto, dependências e artefatos gerados ficaram fora da contagem.

| Escopo | 1.3.0 | 2.0.0 | Diferença |
| --- | ---: | ---: | ---: |
| Produto, runtime e distribuição | 2.968 | 3.723 | +755 |
| Testes e benchmark | 909 | 1.084 | +175 |
| Total | 3.877 | 4.807 | +930 |

## Validação

- **Release.** A suíte com acesso ao áudio do Windows passou em **50 cenários**, incluindo quatro novos para navegação, cópia, preferências e foco. O ensaio incluiu saída WaveOut e duas sínteses por voz local com Edresson e Faber, além do fallback Windows e de downloads simulados.
- **SQLite.** Migração de banco legado, backup consistente, preservação do payload, idempotência, proteção, rollback da cópia e hierarquia com mais de mil níveis. O perfil pessoal ficou fora dos testes.
- **WPF.** Inspeção de três capturas reais e testes com janelas configuradas em 800 × 640, 950 de largura e 1280 × 800. Validados ambos os temas, lista virtualizada, grade, ajuda, menus, barra acessível, destaque e reprodução durante a troca de tela.
- **Escala da biblioteca.** Com 10.000 textos e 200 pastas fictícias, a primeira página levou 6,5 ms e a busca de conteúdo, 781,6 ms. O ensaio completo desse tamanho consumiu 1.844 ms de CPU, alocou 102,0 MiB cumulativos e registrou 157,4 MiB de conjunto de trabalho. Alocação cumulativa inclui testes e consultas ao longo do ensaio.

### Medição comparável da primeira página

Mesmo executável de teste, processo separado por versão, 10.000 documentos fictícios com 2.048 caracteres, catálogos migrados, sem pastas ou proteção. Mediana de três listagens. Cada versão devolveu 100 resumos e reteve zero caracteres de corpo nos resultados.

| Medida | 1.3.0 | 2.0.0 |
| --- | ---: | ---: |
| Tempo mediano | 5,31 ms | 6,34 ms |
| Alocação média por listagem | 0,064 MiB | 0,065 MiB |

A amostra da 2.0.0 foi mais lenta nessa medição. A diferença de alocação ficou abaixo de 1 KiB por listagem. O ensaio de estabilidade de 30,09 s, com 50.000 palavras e áudio sintético, realizou 6.380 pedidos de navegação, usou 687,5 ms de CPU durante a sequência e terminou com conjunto de trabalho de 53,93 MiB, 0,36 MiB abaixo da medição inicial.

### Ocorrências e limites

- A execução restrita apresentou erro nativo em `waveOutClose`, interferência de um modificador de teclado e erro SQLite ao criar o backup sob um caminho temporário longo. A validação usou caminho temporário curto e acesso direto ao dispositivo e teclado do Windows. A falha nativa também apareceu no ensaio inicial da 1.3.0.
- A grade realiza o layout de todos os resultados da página e de suas pastas visíveis. Um diretório com muitas pastas pode custar mais na grade. O modo lista usa virtualização.
- A validação cobre o conteúdo e a abertura dos pacotes. Instalação e desinstalação completas em Windows limpo, arraste físico, múltiplos monitores/DPIs, Narrador/NVDA e sessões de várias horas ficam fora dos ensaios realizados. O instalador é distribuído sem assinatura Authenticode.
