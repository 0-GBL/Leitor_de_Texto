# Alterações 1.1.0

- Correção reproduzida no Windows PowerShell 5.1: aspas perdidas no comando Python inline provocavam `SyntaxError: '(' was never closed`. Substituído por um script de checagem, com `pip check`, dependências fixadas e mensagens UTF-8 corretas.
- Instalação/reparação e download testados em diretório com espaços. O serviço de downloads é o mesmo usado pela interface e pelos testes; falhas antes da publicação não substituem modelos existentes. Cancelar uma operação permite tentar novamente na mesma janela.
- Downloads têm limite de 30 segundos sem dados e até três tentativas para falhas transitórias. Essa correção foi validada com falha HTTP simulada e downloads reais.
- Mantida a divisão progressiva em blocos. Melhorados finais de frase com reticências/aspas, valores decimais e abreviações; sem pausa artificial em cortes internos de frases extensas.
- Destaque da frase desacoplado da seleção, com início vinculado à reprodução efetiva. A geração antecipada não move o destaque.
- Botão Reiniciar leitura começa o documento original desde zero, mantendo iniciar/pausar/continuar e ±10 segundos. Esc cancela a geração e conserva a posição.
- Navegação em grupos Parágrafos e Títulos, com identificação distinta, nomes acessíveis e dicas. No fim da navegação, a ausência de destino não reinicia a frase atual.
- Cursor de tempo obtido do dispositivo de áudio. Fechamento pausa a saída antes de capturar o deslocamento final; metadados de tempo e durações são mantidos no payload SQLite já existente.
- Compatibilidade com documentos/configurações 1.0, inclusive divisão antiga para retomar leituras em andamento. Nenhuma recriação do banco é necessária.
- Configurações transferidas para a engrenagem: Aparência, Reprodução e Privacidade e segurança. Lateral dedicada à narração.
- Tema claro inspirado em livro e tema escuro preservado, com botão dinâmico lua/sol e cores centralizadas em dicionários XAML.
- Corrigido erro anterior de fechamento recursivo quando não havia texto a salvar.
- Instalador, executável e informações de versão atualizados para 1.1.0. Biblioteca, cofre, modelos e configurações permanecem nos diretórios originais.

Exportação e funcionalidades futuras permanecem fora desta versão. Consulte `VALIDACAO-1.1.0.md` para resultados e limites dos testes.
