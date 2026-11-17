# Imagens da apresentação

Imagens geradas em 17/11/2026 a partir da distribuição local **Leitor 1.2.1**.

## Procedência

As PNGs capturam o conteúdo cliente das telas WPF reais, instanciadas a partir de `artifacts/Leitor-portatil/Leitor.dll`, sem alterar o código do aplicativo. Um processo separado usou `LEITOR_DATA_DIR` em uma pasta temporária dentro de `.tools/`, criou três documentos fictícios e renderizou as árvores visuais com `RenderTargetBitmap` a 96 DPI. Fundo e margens são os das próprias janelas. Não são fotografias da área de trabalho nem mockups desenhados de uma interface inexistente.

As janelas não solicitaram ativação e ficaram fora da área visível. Não foi aberta a biblioteca pessoal, não houve reprodução de áudio, instalação de runtime ou download de modelo. O gerenciador consultou somente a ficha pública da voz Faber. Nenhum PIN ou dado pessoal aparece. Datas, títulos, etiquetas e progresso pertencem à demonstração; o player está parado.

| Arquivo | Conteúdo |
| --- | --- |
| `banner.svg` | Identidade vetorial editorial, baseada na paleta clara e na geometria de livro do aplicativo; não é captura de tela. |
| `interface-clara.png` | Interface completa no tema de livro, com editor/biblioteca/narração. |
| `leitura-clara.png` | Aba Acompanhar e posição selecionada no tema claro. |
| `interface-escura.png` | A mesma tela no tema escuro real. |
| `biblioteca.png` | Conteúdo do painel real de biblioteca, capturado separadamente. |
| `reprodutor.png` | Conteúdo do player real, capturado separadamente. |
| `atalhos.png` | Janela de ajuda F1; categorias adicionais são acessadas por rolagem. |
| `configuracoes.png` | Janela de configurações; a área inferior pode exigir rolagem. |
| `vozes-neurais.png` | Gerenciador de vozes com a ficha oficial de Faber, sem aceitar licença ou iniciar download. |

O enquadramento exclui a barra de título nativa do Windows. Conteúdo fora da área visível de uma lista/ScrollViewer não foi reconstruído nem inventado. A renderização depende das fontes/escala do ambiente. Essas imagens demonstram aparência, não certificam acessibilidade, naturalidade ou interações físicas. Não houve geração de imagens por IA para representar telas.

O banner e as capturas integram a documentação do projeto; termos do código e das dependências estão em [LICENSE](../../LICENSE) e [THIRD-PARTY-NOTICES.md](../../THIRD-PARTY-NOTICES.md).