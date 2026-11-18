# Revisão da apresentação no GitHub

Data: 17/11/2026. Escopo exclusivo de documentação/apresentação da versão 1.2.1. Nenhum código, dependência do aplicativo, mecanismo de segurança, versão ou pacote de distribuição foi alterado. Nenhum commit ou envio ao GitHub foi realizado.

## Evidências usadas

- Versão e tecnologias: `src/Leitor/Leitor.csproj`, referências e estrutura dos serviços.
- Atalhos: `Presentation/Shortcuts.cs`, handlers de MainWindow/SettingsWindow/HelpWindow e SeekSlider. A tabela inclui edição nativa e os comandos locais da barra.
- Segurança: Vault, Storage, AtomicFiles, VoiceDownloads e os relatórios já existentes. Não houve novos testes criptográficos ou alterações de proteção nesta tarefa.
- 1.1.0: relatório recuperado do commit cbf2ef8 e preservado em VALIDACAO-1.1.0.md, com 19 testes.
- 1.2.0: VALIDACAO-1.2.0.md, com 25 testes.
- 1.2.1: VALIDACAO.md e DESEMPENHO-1.2.1.md, com 35 cenários distintos e distinção entre testes locais, rede, sintéticos e físicos.
- 1.0.0: 11 testes é o histórico informado pelo autor no pedido; não foi localizado relatório versionado para confirmar independentemente essa execução. Essa procedência está explícita no README.
- Licença: texto GPL v3 em LICENSE e declaração GPL-3.0-or-later em THIRD-PARTY-NOTICES.md; as licenças das dependências/modelos continuam separadas.

## Links e disponibilidade

Validação de README.md, GUIA-DO-USUARIO.md e images/README.md:

- 58 referências locais/âncoras verificadas: nenhum destino ausente.
- 13 badges estáticos Shields.io: HTTP 200; sem badge de CI/cobertura ou resultado automático inexistente.
- Perfil GitHub e página de Releases: HTTP 200.
- API oficial de releases do repositório: coleção vazia no momento da consulta. O README não oferece links fictícios para instaladores e explica que artifacts é gerado localmente/ignorado pelo Git.
- LinkedIn: HTTP 999, bloqueio da consulta automatizada. A URL autorizada foi preservada exatamente; conteúdo/disponibilidade do perfil exigem conferência manual. Não houve login nem tentativa de contornar a restrição.
- E-mail: URI mailto com o endereço autorizado; nenhuma mensagem foi enviada. Não foi verificada a existência da caixa postal.

A disponibilidade externa pode mudar depois da consulta. Os dados de contato são exclusivamente os fornecidos pelo autor.

## Validação visual

Markdown renderizado localmente para inspeção; sintaxe Mermaid analisada e diagrama renderizado sem erro. Todos os 21 elementos de imagem usados no README carregaram na prévia (13 badges, banner e sete capturas exibidas). Uma oitava captura de leitura clara também foi preservada na galeria de arquivos. A prévia local não substitui a renderização final pelo GitHub, mas o documento usa Markdown/HTML compatíveis, sem estilos ou scripts embutidos no README.

As oito PNGs são renderizações do conteúdo das telas WPF reais, usando a DLL publicada 1.2.1 e dados fictícios em um diretório novo isolado. Não são screenshots da área de trabalho ou mockups gerados por IA. Foram revisados fundos, recortes e margens. O player ficou parado e não houve síntese/reprodução. O gerenciador de vozes consultou apenas a ficha pública de Faber. Método detalhado em images/README.md.

O banner SVG é um recurso editorial vetorial com a paleta/ícone de livro do aplicativo. Foi inspecionado na prévia do README; não representa uma tela do produto.

## Arquivos da entrega

- README.md reestruturado para apresentação do projeto.
- GUIA-DO-USUARIO.md criado a partir do conteúdo técnico anterior, preservando os procedimentos e limites.
- VALIDACAO-1.1.0.md preserva o relatório histórico existente no Git.
- images/ contém o banner, oito PNGs e a descrição da procedência.
- Este registro documenta as verificações e limitações da tarefa.

Não foram reexecutados os 35 cenários de regressão nem os benchmarks: os números apresentados são os resultados já registrados. Não foram gerados novos instaladores ou alterados artefatos da versão 1.2.1. Os relatórios técnicos existentes e o CHANGELOG permanecem preservados.
## Atualização 1.3.0 — 17/11/2026

O registro acima corresponde à apresentação da 1.2.1. A entrega 1.3.0 também modifica o aplicativo, atualiza capturas reais com pastas fictícias, registra 47 cenários e acrescenta arquitetura/migração/desempenho. Informações autorizadas do desenvolvedor e documentos históricos foram mantidos. Nenhum commit ou publicação foi realizado.
