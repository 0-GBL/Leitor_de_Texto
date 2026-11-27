# Dependências e licenças

Verificação realizada em 17/11/2026. O código deste projeto é oferecido sob GPL-3.0-or-later; o texto completo acompanha `LICENSE`. O aplicativo não incorpora modelos de voz no instalador: os downloads opcionais preservam a ficha oficial ao lado do modelo. Ao redistribuir binários, disponibilize também o código-fonte correspondente e os avisos das dependências. Este documento registra fontes, não substitui os respectivos textos legais.

| Componente | Versão usada | Licença / fonte oficial |
| --- | --- | --- |
| .NET / WPF | .NET 10, SDK 10.0.404 | MIT e avisos dos componentes: https://github.com/dotnet/runtime/blob/main/LICENSE.TXT ; https://github.com/dotnet/wpf/blob/main/LICENSE.TXT |
| Microsoft.Data.Sqlite | 10.0.15 | MIT: https://www.nuget.org/packages/Microsoft.Data.Sqlite/10.0.15 |
| SQLite | via SQLitePCLRaw | Domínio público: https://www.sqlite.org/copyright.html |
| SQLitePCLRaw | versões registradas no lock NuGet | Apache-2.0: https://github.com/ericsink/SQLitePCL.raw/blob/master/LICENSE.TXT |
| System.Speech | 10.0.0 | MIT: https://www.nuget.org/packages/System.Speech/10.0.0 |
| NAudio.WinMM, NAudio.Core e NAudio.Wasapi | 2.2.1 | MIT: https://github.com/naudio/NAudio/blob/master/license.txt |
| Python | 3.14.10 embutido, opcional | PSF e avisos incluídos no runtime: https://docs.python.org/3.14/license.html |
| Piper TTS | 1.8.0, opcional | GPL-3.0-or-later: https://github.com/OHF-Voice/piper1-gpl/blob/v1.8.0/COPYING ; política: https://github.com/OHF-Voice/piper1-gpl/blob/main/docs/LICENSING.md |
| ONNX Runtime | 1.32.0, dependência do Piper testada | MIT e avisos: https://github.com/microsoft/onnxruntime/blob/main/LICENSE |
| eSpeak NG / componentes do Piper | incluídos no wheel Piper | GPL e avisos do pacote: https://github.com/espeak-ng/espeak-ng/blob/master/COPYING |

O instalador de recursos baixa pacotes do PyPI com as versões e hashes SHA-256 fixados em `runtime/piper-requirements.txt`, incluindo as dependências transitivas do Piper. O ambiente testado também está registrado em `docs/piper-tested-requirements.txt`. A aplicação em C# usa arquivos de lock NuGet. Ao redistribuir um ambiente Python pré-instalado, preserve todos os arquivos LICENSE, COPYING e dist-info e confira suas obrigações.

## Modelos do catálogo

- **pt_BR-faber-medium:** a ficha identifica o dataset como CC0, com ajuste a partir de hifi_tts_92/Cori Samuel. Requer Piper ≥1.5. Fonte completa: https://huggingface.co/rhasspy/piper-voices/blob/main/pt/pt_BR/faber/medium/MODEL_CARD . A indicação de licença do dataset não é uma licença universal concedida pelo Piper sobre qualquer modelo; confira a ficha atual e a procedência antes de redistribuí-lo.
- **pt_BR-edresson-low:** ficha indica dataset CC BY 4.0, de Edresson/TTS-Portuguese-Corpus, com ajuste da voz Ryan. Preserve atribuição a Edresson/TTS-Portuguese-Corpus e à ficha do modelo: https://huggingface.co/rhasspy/piper-voices/blob/main/pt/pt_BR/edresson/low/MODEL_CARD ; https://creativecommons.org/licenses/by/4.0/ .
- **en_US-ljspeech-medium:** a ficha identifica o dataset LJ Speech como domínio público, gravações de uma voz feminina, treinamento por Bryce Beattie. Fontes: https://huggingface.co/rhasspy/piper-voices/blob/main/en/en_US/ljspeech/medium/MODEL_CARD ; https://keithito.com/LJ-Speech-Dataset/ . A ficha oficial é apresentada antes do download e preservada junto ao modelo.

Vozes nativas do Windows são recursos do sistema e não são redistribuídas por este projeto. O gênero e a disponibilidade dependem dos metadados e vozes instaladas. O catálogo não presume autorização de uso comercial de modelos com termos ambíguos.
