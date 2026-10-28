# Validação — 28/10/2026

## Versão 1.4.0 — navegação e perfis aprovados

- Publicação Windows x64 com runtime incluído concluída, sem erros nem avisos. Nenhum pacote de terceiros foi adicionado.
- **76 verificações do monitor e dos catálogos** aprovadas, incluindo compatibilidade com preferências anteriores, retenção, privacidade, correlação de eventos, políticas de avisos e integridade das traduções.
- **118 verificações da interface WPF** aprovadas. A árvore real não contém `TabControl`; o menu lateral tem os cinco destinos, e a tela inicial é Sessões.
- A geometria da janela confirma que Suas sessões precede os perfis. Os três cartões ficam expostos e a personalização inicia aberta, sem expansor. Clicar em cada perfil atualiza suas opções imediatamente, com nome e semântica de `RadioButton` acessíveis.
- Personalização e perfil ativo persistem ao alternar cartões. Silencioso mostra os controles desligados e desativados; Voltei retoma Normal. O idioma e as configurações continuam sendo recuperados ao reabrir.
- Com duas sessões de exemplo, a lista, os cartões e as regras cabem juntos na janela padrão. Alternar para Ocupado mantém a largura dos cartões e não cria rolagem nessa composição.
- As cinco áreas foram renderizadas em todos os seis idiomas na janela mínima de 900 × 700 unidades lógicas. A rolagem alcança as regras abertas em cada idioma, sem alterar a ordem dos blocos.
- Capturas reais comparadas à referência aprovada: menu lateral, superfícies claras, ações em azul, cartões selecionados, ordem das sessões e personalização aberta. A referência está em `design/referencia-sessoes-aprovada.png`; capturas reais, em `docs/images/`.
- O aviso continuou sem solicitar ativação e se recolheu em cerca de **3,07 segundos**. A troca de idioma durante a exibição não reiniciou sua duração; uma confirmação correspondente fechou o aviso antes do prazo.
- Resultados: `artifacts/tests/b105da3345f74aeeb25983165206d026/core/results.json` e `ui/results.json`. Evidências isoladas completas não são versionadas.

Os testes usam eventos fictícios, o produtor real de hooks e renderização das próprias janelas. Não comprovam som audível, leitura por todos os leitores de tela, todas as combinações de DPI/monitores, sobreposição a jogos ou a entrega de eventos em cada instalação do Claude. Em janelas menores, o conteúdo de Sessões requer rolagem; os controles permanecem expostos e acessíveis.

## Versão 1.3.0 — Atenção, Ritmo e Projetos

- Publicação Windows x64 com runtime incluído concluída, sem erros nem avisos na compilação final. A restauração oficial do NuGet foi concluída; não foram adicionados pacotes de terceiros.
- **76 verificações do monitor e dos catálogos** aprovadas. Cobrem solicitações paralelas, resultados ambíguos, confirmação por ID, deduplicação persistente, eventos atrasados, adiamentos, expiração de 24 horas, restauração, recuperação de arquivos inválidos e preservação do período do resumo ao alternar perfis.
- O executável foi iniciado em modo hook com JSON fictício pelo stdin: encerrou com código zero e sem saída de decisão; a fila recebeu somente metadados e descartou comandos e transcrições do teste. Um mutex de diagnóstico impediu a abertura automática do monitor durante esse teste isolado.
- A política de avisos foi verificada com relógio controlado: intervalos progressivos de 2, 5 e 15 minutos; limite de três lembretes; adiamento explícito com repetições desativadas; perfil Silencioso; retomada programada e regras independentes por projeto.
- Preferências anteriores conservam idioma, som, popup e intervalo. Apelidos, projetos fixados e prazo de retorno sobrevivem à reabertura. Os seis catálogos têm chaves completas e parâmetros de mensagens compatíveis.
- **94 verificações da interface WPF** aprovadas. Incluem fila e recentes, adiamento pela interface, perguntas paralelas, perfis, resumo, apelidos, fixação, silêncio individual, diagnóstico, bandeja e recuperação dos dados.
- As três abas foram renderizadas em todos os seis idiomas. O seletor, as ações essenciais e os botões foram conferidos dentro da janela mínima de 900 × 740 unidades lógicas. Conteúdo extenso em Ritmo e Projetos usa rolagem vertical.
- O aviso permaneceu sem solicitar ativação, sem item na barra de tarefas e com recolhimento automático em cerca de **3,07 segundos**. A troca de idioma durante sua exibição não reiniciou o prazo. Um resultado correspondente fechou um aviso pendente antes do prazo.
- O retorno automático foi verificado durante a execução e na abertura após o prazo vencer com o aplicativo fechado. O perfil Normal restaurado foi salvo no disco.
- Resultados: `artifacts/tests/b45f9267072d42ad943b4b86c015cbb5/core/results.json` e `ui/results.json`. Capturas aprovadas de demonstração estão em `docs/images/`; demais evidências isoladas não são versionadas.

Esses testes exercitam o monitor, o produtor real de hooks e a interface com eventos fictícios. Não comprovam o funcionamento de cada instalação do Claude, a seleção de uma conversa específica do VS Code, som audível, leitura por todos os leitores de tela, sobreposição a jogos ou todas as combinações de monitores/escalas. O diagnóstico de arquivos não substitui a chegada de um evento real do Claude.

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
