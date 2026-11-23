# Biblioteca e arquitetura — 1.3.0

A 1.3.0 mantém C#/WPF/MVVM, SQLite e os motores de áudio existentes. Não acrescenta dependências de produção. A área central, os controles de narração e as paletas permanecem; a biblioteca ganha um componente próprio e redimensionamento entre 200 e 520 unidades WPF, limitado também pela largura central disponível.

## Organização e interface

`LibraryPanel` apresenta Todos os textos, Pastas e subpastas e Textos sem pasta. As pastas são ordenadas alfabeticamente; os documentos podem ser ordenados por título, criação, modificação ou progresso, nas duas direções. As contagens incluem descendentes. A árvore usa uma lista plana virtualizada de linhas expandidas; a indentação visual é limitada, sem limitar a profundidade armazenada.

Os menus de contexto permitem criar, renomear, mover e excluir. Arrastar documentos/pastas para outra pasta ou para Minha biblioteca usa o mesmo serviço de movimentação. O seletor de destino é a alternativa por teclado. Excluir uma pasta oferece cancelar, promover o conteúdo para o nível superior ou excluir tudo, com contagens e confirmação adicional. As contagens são conferidas novamente dentro da transação antes de excluir.

Os atalhos Ctrl+Shift+N, F2 e Ctrl+Shift+M atuam com foco na biblioteca, fora da edição. Setas verticais selecionam pastas; direita expande, esquerda recolhe ou seleciona o pai. F1 documenta os comandos. Seleção, expansões, visualização, escopo, ordenação e largura são preferências persistentes. A pesquisa e o filtro por etiqueta não são persistidos.

## Componentes

| Componente | Responsabilidade |
| --- | --- |
| `Services/LibraryOrganization.cs` | Parte de LibraryStore: migração, catálogo, consultas, hierarquia e transações. |
| `ViewModels/LibraryViewModel.cs` | Parte de MainViewModel: páginas, comandos, cancelamento, árvore e estatísticas. |
| `Presentation/LibraryPanel.xaml` | Biblioteca virtualizada e interações de teclado, contexto e arraste. |
| `Presentation/OrganizationDialogs.cs` | Nome, destino e escolha segura de exclusão. |
| `Core/TextStatistics.cs` | Contadores incrementais e estimativa de duração. |

O editor carrega o corpo de apenas um documento selecionado. As páginas contêm até 100 resumos e não guardam conteúdos ou durações de todos os textos. A troca de página não limita a quantidade total de documentos. As buscas têm debounce de 160 ms, cancelamento e descarte de resultados antigos; tarefas de banco não executam na thread WPF. As contagens e a hierarquia são carregadas separadamente. Durante o autosave de áudio, um resumo é atualizado quando a ordenação permite; novas consultas são feitas quando necessárias.

## SQLite e segurança

O esquema 2 mantém `documents(id, protected, payload)`. Acrescenta `folders(id, name, parent_id, created, modified)` e `document_catalog`, com chaves estrangeiras, datas, associação e resumo para documentos comuns. Índices atendem proteção/ID, pai, pasta, título, datas e progresso. A criação usa GUIDs. Consultas são parametrizadas; movimentação impede ciclos dentro de transação. Exclusão profunda percorre filhos antes dos pais, evitando depender do limite de recursão de cascatas do SQLite.

**Pastas são organização pública local:** nomes e hierarquia não são criptografados. Não use nomes sensíveis se isso for um risco. Para documentos protegidos, título, etiquetas, datas reais, progresso e associação à pasta permanecem somente no payload cifrado. O catálogo não persiste esses valores. A associação é autenticada no formato LVR2 já existente; AES-GCM, PBKDF2 e DPAPI não foram substituídos.

Com a sessão bloqueada, a vista global mostra entradas genéricas; documentos protegidos não entram em contagens de pasta, resultados de busca nem em Textos sem pasta, pois sua associação é desconhecida. Ao desbloquear, as associações e resumos são obtidos em memória, um payload por vez. Excluir uma pasta pode solicitar PIN mesmo se a contagem visível for zero, para verificar associações ocultas. Bloquear cancela consultas, limpa seleção/resumos protegidos, busca, etiquetas e contadores do texto protegido.

Não existe índice persistente de conteúdo protegido. Busca em conteúdo percorre dados em segundo plano; documentos cifrados exigem decifração transitória e sessão desbloqueada. Isso tem custo proporcional ao volume. Resultados autorizados são resumos; os corpos não ficam retidos pela listagem. Documentos comuns continuam sem criptografia, conforme a escolha individual do usuário.

## Estatísticas do editor

Caracteres são unidades UTF-16, consistentes com os offsets WPF (um emoji pode ocupar duas unidades). Palavras são sequências separadas por espaço em branco. Linhas lógicas contam quebras CR/LF, com CRLF contando uma quebra; texto vazio tem zero linhas, e quebra final acrescenta uma linha. Quebras visuais pela largura não contam.

Blocos de 4.096 caracteres permitem reprocessar apenas a região de pequenas edições, agregando seus contadores. Colagens/substituições grandes usam cálculo assíncrono com debounce de 180 ms e versão para descartar resultados antigos. A duração marcada por ≈ considera velocidade e pausas configuradas de frase e parágrafo; é aproximação textual, distinta da duração já medida pelo motor de voz.

Veja [migração](MIGRACAO-1.3.0.md), [validação](releases/1.3.0/RELATORIO-VALIDACAO.md) e [desempenho](DESEMPENHO-1.3.0.md).
