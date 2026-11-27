# Histórico de versões

## 2.1.0 — 26/11/2026

- Validação da voz antes de codificar, escolha de voz por exportação, diagnósticos por etapa e ilustração adaptável no painel lateral da biblioteca.
- Exportação individual do documento aberto em MP3 mono de 96 kbps, com voz Windows ou Piper, pausas, progresso por trechos e cancelamento.
- Autorização por PIN para documentos protegidos e publicação do arquivo final após a codificação, com proteção contra sobrescrita.
- [Relatório de validação](docs/releases/2.1.0/RELATORIO-VALIDACAO.md).

## 2.0.0 — 19/11/2026

- Retirada a clonagem experimental e a análise de referência usada pelo protótipo. A distribuição estável usa Piper e as vozes do Windows.

- Refinamento da biblioteca com cartões responsivos, ícones dourados personalizáveis, árvore recolhível e localização no diálogo de criação. Distribuição por versão e validação em `docs/releases/`.
- Biblioteca em tela própria, com conteúdo do diretório, caminho clicável, voltar/avançar, busca por pastas e documentos e histórico local de textos recentes.
- Cópia transacional de documentos e árvores de pastas. Menus de contexto e arraste com Ctrl para copiar, além do arraste para mover.
- Preferências de lista/grade, densidade, tamanho dos ícones e cores de pastas. A leitura tem modo foco com destaque e controles de narração.
- A atualização a partir da 1.3 mantém o esquema SQLite, o PIN, os documentos e o progresso. A migração de bancos mais antigos continua criando backup antes de gravar.
- Versões portátil e instalável para Windows x64. [Relatório da versão](docs/releases/2.0.0/RELATORIO-VALIDACAO.md).

## 1.3.0 — 17/11/2026

- Biblioteca com pastas/subpastas, contagens, expansão, menus de contexto, mover por seletor ou arraste, prevenção de ciclos e exclusão com promoção de conteúdo ou confirmação de remoção total.
- Visualizações, ordenações, busca global/subárvore combinada com etiquetas, localização dos resultados e preferências persistidas. Painel redimensionável e atalhos contextuais na ajuda F1.
- Catálogo SQLite, índices, transações e páginas virtualizadas de 100 resumos. Corpos carregados sob demanda, consultas canceláveis em segundo plano e atualização econômica do autosave.
- Estatísticas incrementais de caracteres/palavras/linhas lógicas e estimativa conforme velocidade/pausas.
- Migração esquema 1 → 2 com backup consistente prévio e idempotência, preservando payloads/PIN/cofre. Associação de protegidos cifrada; nomes de pastas continuam públicos localmente. Algoritmos e motores de voz preservados.
- 47 cenários aprovados, incluindo os 35 anteriores, WPF e escala de 140 a 10 mil documentos fictícios; relatórios de arquitetura, migração, desempenho e capturas atualizados.

## 1.2.1 — 17/11/2026

- Corrigida a barra: clique direto, arraste com prévia, tempo real/estimado identificado, teclado/acessibilidade, cancelamento e confirmação única sem disputa com o temporizador.
- Transporte preserva pausa/parada e aplica somente o pedido mais recente, agrupando entradas rápidas em 40 ms. Destino não sintetizado é priorizado; busca da timeline passa a O(log n), construção a O(n).
- Captura de posição antes do reset do dispositivo; navegação dentro de uma pausa não retrocede ao continuar. Metadados futuros de duração são invalidados ao mudar voz/velocidade/tom.
- Troca de documentos interrompe/salva o cursor final. Falha de fechamento mantém editor e autosave utilizáveis; pausa/salvamento na suspensão e mensagens operacionais mais claras.
- Modelo Piper inválido usa Windows durante a sessão, evitando novas tentativas em cada frase; reparação libera nova tentativa.
- Ajustes de listas, abreviações e pausas em títulos longos, sem alterar os quatro modos ou o original. Destaque configurável independentemente da rolagem; identidade visual 1.2.0 preservada.
- Regressão ampliada, cenários de 1.000–50.000 palavras, comparação com 1.2.0, ensaio repetido de navegação e revisão de dependências. Formatos criptográficos/SQLite preservados.
## 1.2.0 — 17/11/2026

### Experiência e acessibilidade
- Cabeçalho com marca/tagline, privacidade local, novo/colar, tema rápido, ajuda e configurações.
- Biblioteca com etiquetas, data, progresso, proteção/conclusão e estados vazios/busca sem resultados.
- Editor mais confortável, leitor com indicador textual de narração, painel lateral focado em voz e player reorganizado.
- Ícone próprio, geometrias vetoriais consistentes, controles arredondados, foco visível e paletas centralizadas. Tema de livro e identidade escura preservados.
- Central F1 e atalhos contextuais; edição nativa, Espaço, seleção e colagem preservados. Layout compacto e manifesto PerMonitorV2.

### Segurança e compatibilidade
- Espera progressiva do PIN persistida com DPAPI; alteração do PIN sem trocar a chave dos textos e bloqueio explícito da sessão.
- AES-GCM/PBKDF2/DPAPI preservados; novos payloads autenticam identidade, mantendo leitura dos formatos antigos.
- Gravação atômica de cofre/configurações, ACL em novas pastas, configuração inválida preservada, isolamento de documentos corrompidos e recusa de remoção da chave quando há integridade inválida.
- Python opcional atualizado para 3.14.10; hashes fixados de Python, pip, wheels e modelos/configurações. Instalação em estágio, checagem de dependências e extração protegida contra Zip Slip/links.
- Auditoria de dependências, avisos e transparência de proteção atualizados. Bibliotecas reais não utilizadas nos testes.

### Desempenho e qualidade
- Timeline com somas prefixadas, evitando varrer todo o documento no temporizador.
- Recursos de tema reutilizados; descoberta de vozes fora da thread da interface.
- Testes de segurança, atalhos, arquivos antigos, temas durante leitura, conclusão, layout e regressão de áudio. Comparação com a 1.1 e relatório de limites.
- Distribuição Windows x64 autossuficiente, instalador por usuário, código-fonte correspondente, relatório e SHA-256.

## 1.1.0 — 17/11/2026
- Corrigida passagem de aspas na instalação Piper/PowerShell 5.1 com checagem Python em arquivo.
- Tratamento de pontuação e destaque da frase separado da seleção.
- Reiniciar leitura, grupos de navegação por parágrafos/títulos e configurações categorizadas.
- Tema claro inspirado em livro; tema escuro mantido.
- Captura de progresso pelo dispositivo de áudio, metadados opcionais de retomada e downloads com repetição.

## 1.0.0
- Aplicativo inicial C#/WPF/MVVM: leitura Windows/Piper, reprodução progressiva, quatro modos, biblioteca SQLite, busca/etiquetas e proteção individual opcional.
