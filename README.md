# Leitor 1.1

Aplicativo de leitura de textos em áudio para Windows 10/11 x64. C#, .NET 10 LTS, WPF e MVVM. Todos os textos e áudios são processados localmente. Gratuito, com código disponibilizado sob GPL-3.0-or-later; veja `LICENSE`.

## Usar

1. Abra `artifacts/Leitor-Instalador.exe` para instalar para seu usuário, ou extraia `artifacts/Leitor-portatil.zip` e abra `Leitor.exe`.
2. Digite ou cole o texto, informe título e etiquetas separadas por vírgula e clique em **Iniciar leitura**.
3. Acompanhe o destaque na aba **Acompanhar leitura**. Use iniciar/pausar/continuar, ±10 segundos e os grupos **¶ Parágrafos** e **▤ Títulos**. Selecione uma frase e pressione Enter para ir até ela; selecionar um trecho sozinho não muda o destaque da narração.
4. Os textos e o progresso são salvos automaticamente. Selecione um item da biblioteca e clique em **Abrir texto selecionado**. Pesquise título/conteúdo ou filtre por uma etiqueta exata.

O pacote inclui o runtime .NET: o usuário final não precisa instalar SDK ou configurar SQLite. `Iniciar.cmd` abre a versão publicada diretamente desta pasta. A versão portátil também guarda a biblioteca no perfil do Windows, não junto ao executável.

**Reiniciar leitura** cancela a geração atual, zera o progresso e começa novamente o texto original, inclusive depois de uma prévia de voz. Para interromper mantendo a posição, use **Pausar**; Esc continua disponível para cancelar e conservar o progresso.

## Configurações e temas

Use a engrenagem no canto superior direito. **Aparência** reúne tamanho do texto e o botão dinâmico de tema (lua: Tema escuro; sol: Tema claro). **Reprodução** reúne segundo plano e rolagem automática. **Privacidade e segurança** reúne PIN, solicitação ao abrir e proteção individual do texto. A lateral principal contém somente voz, prévia, vozes offline, modo, velocidade, volume, tom, pausas e transição.

O tema claro usa fundo de papel `#F5EFE4`, painéis `#FFFBF5`, texto `#302820`, bordas `#DED0BD`, botões principais `#79583D` e destaque `#F5DDA0`. O tema escuro anterior foi preservado. Todas as cores da interface principal estão em `src/Leitor/Themes/Light.xaml` e `Dark.xaml`; `ThemeService` alterna os dicionários sem reiniciar o aplicativo.

## Vozes neurais

Em **Vozes offline**, clique em **Instalar / reparar Piper local**. Isso instala Python e Piper em uma pasta própria do aplicativo, sem modificar o Python do sistema. Depois selecione uma voz, leia sua ficha de licença, aceite-a e baixe o modelo. Feche a janela e selecione a voz na tela principal. Modelos instalados têm preferência no primeiro uso; a escolha posterior é lembrada.

A versão 1.1 corrige a passagem de aspas do Windows PowerShell 5.1: a checagem agora executa `check_piper.py`, sem Python inline por `-c`. O marcador de instalação pronta só é gravado depois de `pip check` e da verificação dos imports/provedor CPU. As dependências estão fixadas em `runtime/piper-requirements.txt`. Para reparar uma instalação que apresentou o erro de parêntese, utilize novamente **Instalar / reparar Piper local**; não apague a biblioteca nem as vozes baixadas.

Downloads com falha transitória recebem até três tentativas. Se o servidor ficar 30 segundos sem enviar dados, a tentativa é interrompida e repetida; uma falha final mostra uma mensagem e preserva o modelo já instalado. A instalação inicial depende da disponibilidade do PyPI, Python.org e do repositório de modelos. Depois de instalado, o motor não usa a rede para narrar.

O catálogo inicial oferece Faber e Edresson em português brasileiro e LJ Speech em inglês americano. As vozes Windows instaladas são enumeradas automaticamente, incluindo masculinas e femininas quando disponíveis. A disponibilidade de português no motor Windows depende das vozes SAPI instaladas no sistema. O tom é habilitado somente no motor Windows; seu efeito depende do suporte da voz.

A instalação do Piper e os downloads de modelos exigem internet. Depois disso a leitura funciona offline. O gerenciador só consulta fontes de software e fichas de modelos; ele nunca recebe o texto do editor ou da biblioteca. A voz do Windows permite usar o aplicativo sem instalar Piper. Em caso de falha do Piper, uma mensagem informa o uso do motor Windows como alternativa.

## Modos e reprodução

- **Natural:** 1×; pausas de 0,25 s entre frases, 0,65 s entre parágrafos e 1 s entre tópicos.
- **Rápida:** 1,5×; pausas de 0,1 / 0,25 / 0,4 s.
- **Estudos:** 0,9×; pausas de 1 / 3 / 5 s.
- **Personalizada:** velocidade, tom e pausas determinados pelo usuário.

As pausas de parágrafo/tópico substituem a pausa de frase no mesmo limite, evitando somá-las. Mudanças de modo e velocidade entram na próxima frase. O volume muda durante a reprodução. A troca de voz pode ser imediata, ao final da frase ou ao final do parágrafo. A troca imediata regenera a frase atual e tenta conservar o deslocamento em segundos.

Vírgulas, dois-pontos e ponto e vírgula permanecem na mesma frase e são interpretados pelo motor de voz. Abreviações comuns e números decimais são preservados; reticências e pontuação seguida de aspas/parênteses fecham a frase corretamente. Cortes de segurança no meio de frases extensas não recebem a pausa configurada de fim de frase. O destaque usa um estado de narração separado da seleção e só avança quando o áudio daquela frase começa no dispositivo, não durante a geração antecipada. A verificação visual é atualizada a cada 50 ms durante a reprodução.

A duração e os saltos em trechos ainda não sintetizados são **estimados**. O cálculo passa a usar a duração real conforme cada frase é produzida. A faixa discreta abaixo do cursor representa trechos já processados na sessão; o áudio em memória é descartado após o uso. Não é uma promessa de cache permanente. O texto inteiro não precisa virar áudio para a leitura começar. Uma única frase seguinte é antecipada.

O salvamento agora usa o cursor de bytes do dispositivo de áudio, sem a subtração fixa de latência da versão 1.0. No fechamento, o dispositivo é pausado antes da captura final. São preservados deslocamento fracionário, tempo decorrido, posição no texto original e durações conhecidas das frases. A retomada restaura esses metadados antes de iniciar o áudio. O mostrador apresenta segundos inteiros; internamente os valores não são arredondados. O driver e a nova síntese neural podem introduzir pequenas variações; não se promete retomada da mesma amostra de áudio, pois não existe cache permanente.

As bibliotecas 1.0 são lidas sem recriar o SQLite nem limpar configurações. Para preservar os índices de documentos antigos que têm leitura em andamento, a divisão anterior continua sendo usada naquele documento até editar o conteúdo ou usar **Reiniciar leitura**. Documentos novos e leituras reiniciadas usam o tratamento atualizado de pontuação. Tempos históricos que a versão 1.0 não armazenava não podem ser reconstruídos exatamente; a precisão adicional passa a valer nos próximos salvamentos.

Reprodução em segundo plano vem ativada. Ao desativá-la, trocar de janela ou minimizar pausa a leitura; a retomada é manual. Fechar encerra o aplicativo e preserva o progresso. Não há ícone de bandeja nem execução depois de fechar.

## Privacidade e PIN

Proteção desligada por padrão. Em **⚙ → Privacidade e segurança → Ativar / desativar PIN**, crie um PIN com pelo menos seis dígitos, confirmando-o. Isso ativa o bloqueio ao abrir. Use **Proteger / desproteger este texto** na mesma categoria para criptografar individualmente um documento. Ativar o PIN, por si só, não criptografa todos os textos.

Nos documentos protegidos, título, conteúdo, etiquetas, data e progresso são criptografados com AES-256-GCM. A chave aleatória é envolvida por uma chave derivada do PIN com PBKDF2-SHA256 (600.000 iterações) e protegida adicionalmente com DPAPI para o usuário atual do Windows. A chave fica em memória enquanto a sessão está desbloqueada. A autenticação é exigida para abrir, excluir ou remover a proteção de um texto. Tentativas incorretas recebem espera progressiva durante a sessão.

Não há recuperação de PIN. A biblioteca criptografada depende também do perfil atual do Windows: copiar somente os arquivos para outro usuário/computador não permite abri-la. Guarde cópias dos seus originais importantes. Desativar o PIN pede autenticação e confirmação, descriptografa os documentos numa transação e remove o material de chave.

Dados em `%LOCALAPPDATA%\Leitor`: `library.db`, `settings.json`, `vault.json` (somente com PIN), `voices\` e `piper\`. O SQLite é criado automaticamente. `secure_delete` é ativado para reduzir resíduos após edição/proteção/exclusão; isso não garante apagar versões já existentes em backups, snapshots ou blocos físicos de SSD. O aplicativo não protege contra malware executando na mesma conta nem contra inspeção da memória de uma sessão desbloqueada. Áudio gerado não é gravado em cache de disco.

## Teclado e acessibilidade

| Atalho | Ação |
| --- | --- |
| Ctrl+N | Novo texto |
| Ctrl+S | Salvar |
| Ctrl+Espaço | Reproduzir / pausar / continuar |
| Esc | Parar e cancelar geração |
| Ctrl+← / Ctrl+→ | Voltar / avançar 10 segundos |
| Enter na lista de frases | Ir até a frase selecionada |

Tab e Shift+Tab navegam pelos controles. Os controles principais têm nomes de automação, foco visível, temas claro/escuro e fonte ajustável. O leitor de tela depende do suporte do Windows/WPF; a validação completa com NVDA e Narrador ainda é necessária.

## Desenvolvimento

Execute em PowerShell a partir da pasta do projeto:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Build.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Build.ps1 -Package
```

O script instala o SDK .NET 10 em `.tools/dotnet` se necessário, restaura dependências em `.tools/packages`, compila e executa os testes. `-Package` produz a distribuição autossuficiente, ZIP portátil e instalador por usuário. Precisa de internet na primeira preparação e espaço para SDK, dependências e pacotes. SDK validado: 10.0.404. Pacotes NuGet têm versões fixas e arquivos de lock; use `dotnet restore --locked-mode` para conferir o grafo congelado.

Os testes de Piper são executados se houver runtime e voz no diretório de dados. Para ambiente isolado, defina `LEITOR_DATA_DIR` antes de iniciar o processo; essa variável redireciona a biblioteca, a proteção e os recursos de voz. O teste de reprodução usa volume zero, mas precisa de um dispositivo de áudio disponível.

O teste WPF usa bibliotecas temporárias próprias e abre/fecha janelas para conferir bindings, modos, temas, destaque e reinício. Defina `LEITOR_TEST_DOWNLOADS=1` para incluir downloads reais de Faber/Edresson pelo mesmo serviço usado na interface. Esses downloads devem ser direcionados a um `LEITOR_DATA_DIR` de testes. O instalador de recursos pode ser verificado separadamente com `powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/Install-Piper.ps1 -Destination <pasta isolada>`.

## Organização

- `src/Leitor/Core`: modelos, comandos, processamento e estimativas.
- `src/Leitor/Services`: SQLite, criptografia, configurações, síntese e reprodução.
- `src/Leitor/ViewModels`: estado e comandos da interface.
- `src/Leitor`: telas WPF, gerenciador de vozes e diálogo de PIN.
- `runtime/piper_worker.py`: processo persistente, protocolo JSON por pipes locais e WAV em memória.
- `tests/Leitor.Tests`: verificação automatizada sem serviço de testes externo.
- `installer`: instalador WPF por usuário, atalhos e desinstalação registrada no Windows.
- `docs/VALIDACAO.md`: resultados e limites da verificação.

`ISpeechEngine` separa síntese de reprodução. A exportação futura pode consumir áudio desse contrato sem acoplar o editor ao formato. Exportação WAV/MP3 e demais funções futuras do prompt não estão implementadas.

## Limitações atuais

Identificação de títulos, frases e parágrafos usa heurísticas: cada linha não vazia é tratada como parágrafo; linhas curtas sem pontuação final são candidatas a título. Blocos longos são subdivididos para controlar memória, sem alterar o original. A sincronização é por bloco/frase, não palavra a palavra. Textos com muitas abreviações, código ou listas podem precisar de pausas personalizadas. Não existe limite arbitrário de linhas, mas memória e velocidade dependem do tamanho do documento e do computador.

O instalador não tem assinatura Authenticode nem atualização automática. Nenhuma publicação externa foi feita. A naturalidade da voz é subjetiva: use a prévia para escolher. A distribuição é inicial e requer validação de uso diário, de tecnologias assistivas e de instalação numa máquina Windows limpa antes de ampla distribuição.

Para desinstalar, use **Aplicativos instalados → Leitor → Desinstalar**. A biblioteca e as vozes baixadas são preservadas. Feche o aplicativo antes de atualizar ou desinstalar.
