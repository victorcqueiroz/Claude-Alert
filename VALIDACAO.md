# Validação — 27/10/2026

## Versão 1.2.0 — seleção de idiomas

- Compilação e publicação Windows x64 concluídas, sem erros de compilação. A consulta NuGet continua emitindo NU1900 por indisponibilidade de rede; não foram adicionados pacotes.
- **35 verificações do monitor e dos catálogos** aprovadas: estados, preservação dos hooks, compatibilidade com preferências antigas, normalização do idioma, seis catálogos completos e parâmetros de mensagens consistentes.
- **69 verificações da interface** aprovadas, incluindo os seis idiomas: troca imediata do painel, nome nativo no seletor, menu da bandeja, opções de lembrete, status das sessões, persistência sem alteração das outras opções, controles na janela mínima e aviso traduzido.
- O idioma francês foi salvo e restaurado em uma nova instância da janela com os mesmos dados isolados, confirmando a recuperação da escolha na abertura.
- Um aviso aberto em inglês foi alterado para alemão sem reiniciar sua duração. O fechamento ocorreu em aproximadamente **3,04 segundos** após a abertura.
- As capturas `idioma-pt-BR.png`, `idioma-en.png`, `idioma-es.png`, `idioma-de.png`, `idioma-fr.png` e `idioma-zh-Hans.png` foram geradas e inspecionadas. Os caracteres chineses foram renderizados com as fontes de sistema.
- Resultados desta execução: `artifacts/tests/d8f242572da947948386a1e67077ac9d/core/results.json` e `ui/results.json`. As capturas ficam no subdiretório `ui/` do mesmo resultado e não são versionadas.
- Português permanece o idioma padrão. Os nomes e caminhos de projetos não são traduzidos; as mudanças de idioma preservam os estados internos do monitor.

## Versão 1.1.0 — Claro aprovado

- Publicação Windows x64 com runtime próprio concluída, sem erros. A consulta NuGet continua emitindo NU1900 por indisponibilidade de rede; não foram adicionados pacotes de terceiros.
- Os 20 testes do monitor e da preservação dos hooks passaram.
- As 22 verificações da interface WPF passaram com armazenamento de teste isolado: estado vazio, sessões, reconhecimento de pendências, seis controles dentro da janela mínima, semântica de acessibilidade, menu de lembretes, persistência de preferências, pausa/retomada, aviso desativado, abertura e encerramento por eventos e fechamento na bandeja.
- O novo aviso fechou sem clique em aproximadamente **3,09 segundos**. Abrir o aviso não solicita ativação nem cria item na barra de tarefas.
- Foram renderizados e inspecionados o painel padrão, a janela mínima e o aviso: `test-artifacts/claro-1.1.0/claro-desktop.png`, `claro-minimo.png` e `claro-aviso.png`.
- O visual aprovado usa superfícies brancas, azul nas ações e ícones do Windows. A altura mínima foi ajustada a 660 unidades lógicas para manter o rodapé e os botões livres de sobreposição.
- Resultados completos: `test-artifacts/claro-1.1.0/core-results.json` e `ui-results.json`.
- Instalação concluída em `release/ClaudeAlerta`: todos os arquivos publicados conferem com a compilação. A versão 1.1.0 foi confirmada em execução e a árvore de acessibilidade mostrou o novo painel recebendo eventos de uma sessão real de Claude Code. A sessão temporária de verificação foi encerrada.

Os testes de interface usam eventos simulados no código real do monitor. Não comprovam som audível, leitura por todos os leitores de tela, sobreposição a jogos em tela cheia ou posicionamento em todas as combinações de monitores e escalas.

## Histórico — versão 1.0.0

- Publicação Windows x64 com runtime próprio concluída, sem erros de compilação. A consulta de vulnerabilidades do NuGet não conseguiu alcançar o serviço (NU1900); este projeto não declara pacotes de terceiros.
- 20 verificações automatizadas passaram. Resultado detalhado local em `test-artifacts/final-results.json`.
- O executável recebeu JSON por entrada padrão, gravou um evento sanitizado e retornou código 0.
- O painel exibiu a solicitação simulada como **Autorização necessária**.
- A instalação adicionou 10 handlers ao arquivo real de configurações; todos os comandos de hooks anteriores foram preservados. Backup criado ao lado de `settings.json`.
- Depois da ativação, o painel recebeu também eventos de uma sessão real de Claude e exibiu **Trabalhando**.
- O layout do painel foi inspecionado por captura de tela e árvore de acessibilidade usando a habilidade computer-use. A captura com um jogo em primeiro plano mostrou o jogo; não foi comprovada sobreposição do aviso sobre jogos em tela cheia. Não foi possível comprovar por ferramenta que o som foi ouvido.
- A autorização simulada foi encerrada com `SessionEnd`. O monitor permaneceu em execução.

Ainda requer uso real: conferir o aviso quando a sessão real pedir autorização/pergunta e quando encerrar uma resposta. A integração já está ativa; uma nova conversa ou reinicialização do VS Code pode ser necessária se a sessão não carregar os hooks.
