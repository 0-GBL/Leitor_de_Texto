# Relatório de validação — Leitor de Texto 1.3.0

Data: 17/11/2026. Windows x64, .NET 10.0.404, Release. Todos os documentos e bancos de ensaio são fictícios e isolados. A biblioteca real não foi aberta, recriada, migrada ou submetida a testes destrutivos. Nenhum commit ou envio ao GitHub foi realizado nesta entrega.

## Resultado dos testes

**47 cenários distintos aprovados, zero reprovados na execução completa com downloads:** os 35 cenários da 1.2.1 e 12 novos. A suíte local exclui apenas o cenário opt-in de download real e contém 46. O download e a síntese real das vozes PT-BR Faber e Edresson foram exercitados; instalação e vozes locais continuam disponíveis. Execuções finais de empacotamento repetem a suíte local. Contagem de cenários não é percentual de cobertura; cada cenário agrupa várias assertivas.

O relatório anterior foi preservado em [VALIDACAO-1.2.1.md](VALIDACAO-1.2.1.md), além dos relatórios 1.1.0 e 1.2.0. O código de validação está em `tests/Leitor.Tests/Program.cs`, `Program.Library.cs` e `Program.LibraryUI.cs`.

| Novos cenários | Verificações |
| --- | --- |
| Migração 1 → 2 | Backup, payload/cofre sem alteração, PIN antigo, metadados, idempotência. |
| Hierarquia | Criação, renomeação literal, pais, mover e prevenção de ciclos. |
| Associação de documentos | Mover/retirar, autosave antigo não desfaz movimento, datas/progresso preservados. |
| Exclusão segura | Promoção de conteúdo, exclusão total, rollback por falha induzida, rejeição de contagem desatualizada. |
| Protegidos | Associação cifrada, catálogo sem metadados sensíveis, busca autorizada, bloqueio/PIN e promoção. |
| Consultas | Unicode, título/conteúdo, etiquetas, escopos, ordenações e páginas. |
| Profundidade | 1.100 subpastas em cadeia, percurso e exclusão sem estourar cascata. |
| Estatísticas | 250 edições aleatórias comparadas a contagem integral, CRLF/emoji e velocidade. |
| Preferências | Pasta, expansão, visualização, escopo, ordem e largura restaurados. |
| Atalhos | Novos comandos contextuais sem tomar teclas do editor. |
| Escala | 140/500/1.000/5.000/10.000 documentos, pastas, busca/mover enquanto áudio controlado toca. |
| Integração WPF | Diálogos reais de criar/renomear/mover, cancelamento da exclusão, árvore expandir/recolher, menus, painel redimensionado, contadores, busca durante áudio e limpeza ao bloquear. |

Os 35 cenários preservados cobrem segmentação/pontuação, Unicode/offsets, texto extenso/cancelamento, persistência, retomada, SAPI/Piper, downloads/hash, temporização, pausas/modos, PIN/chaves/compatibilidade, corrupção/integridade, extração segura, temas, atalhos, transporte/seek e falhas. Os motores de síntese e a lógica de transporte não foram modificados nesta atualização.

## Build, apresentação e distribuição

A versão dos projetos, manifesto e instalador é 1.3.0. A compilação Release foi concluída sem erros ou avisos após restauração das dependências. A captura usa árvores WPF reais da compilação 1.3.0, fora da área visível e sem ativação; textos, pastas e progresso são fictícios. [Procedência](images/README.md).

`scripts/Build.ps1 -Package` compila, executa a suíte local, publica portátil autossuficiente Windows x64, incorpora o ZIP no instalador e executa `--verify-payload`. O pacote de código inclui fontes/testes/documentação e exclui dados, chaves, bin/obj e modelos. A documentação, inclusive imagens, acompanha a distribuição. `SHA256SUMS.txt` registra os hashes do instalador, portátil e fonte. As conferências finais de entrega incluem conteúdo dos ZIPs, versão e build do fonte extraído; uma falha nessas etapas impede a entrega como validada.

## Migração e proteção

Os ensaios confirmaram cópia consistente antes da mudança e preservação dos bytes dos documentos antigos/cofre. O esquema acrescenta pastas e catálogo em transação; não troca algoritmos de proteção. Novos documentos continuam com PIN desligado por padrão. Pastas têm nomes/hierarquia em texto simples; associação e metadados de documentos protegidos ficam cifrados. Contagens bloqueadas omitem protegidos. Não houve migração da biblioteca real. [Procedimento e downgrade](MIGRACAO-1.3.0.md) · [Arquitetura](BIBLIOTECA-1.3.0.md).

## Desempenho observado

Na comparação de 10 mil documentos sintéticos de 2.048 caracteres, a carga antiga de todos os corpos levou 167,55 ms e alocou 64,222 MB; uma página nova de 100 resumos levou 5,87 ms e alocou 0,064 MB, sem corpos retidos. As operações devolvem quantidades diferentes por projeto. A primeira migração nessa amostra custou 1,28 s. Na suíte de escala, a busca de 10 mil levou cerca de 0,72 s em segundo plano; não há índice persistente de conteúdo protegido. Working set de 150–176 MB na suíte é do processo de testes, não um pico isolado da GUI/Python. [Método, tabelas e limites](DESEMPENHO-1.3.0.md).

## Limitações e testes pendentes

- Arraste/solte implementado, mas sem ensaio físico de mouse entre os itens. A alternativa por seletor de destino foi exercitada em WPF.
- Nomes acessíveis, foco e roteamento de teclas verificados; avaliação completa com Narrador/NVDA e ABNT2 físico pendente.
- Não realizados: Windows limpo, instalação/desinstalação na conta real, múltiplos DPI/monitores, desconexão física de dispositivo, suspensão física, falha real de disco/queda de energia e uso por várias horas.
- Áudio controlado simultâneo exercitado; sem medição acústica de jitter nem Piper sob todas as escalas.
- Primeira migração síncrona; conteúdo pesquisado por varredura assíncrona, com custo maior em textos/cifrados extensos. Estatísticas são aproximações; caracteres UTF-16 e linhas lógicas.
- Instalador sem assinatura Authenticode. PIN não tem recuperação e DPAPI depende do perfil Windows. Não há downgrade automático.

Esses limites não são apresentados como testes aprovados. A documentação histórica e os formatos de voz existentes foram preservados. Funcionalidades futuras de IA/importação/exportação/web/mobile/sincronização continuam fora desta versão.
