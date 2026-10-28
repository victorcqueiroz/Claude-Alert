# Propostas visuais — Claro aprovado

## Navegação atual — 1.4.0

A direção atual é a [navegação lateral com sessões acima dos perfis e personalização aberta](NAVEGACAO.md). Essa revisão substitui a organização por abas da 1.3.0 e preserva a skin Claro escolhida anteriormente.

O histórico abaixo registra as primeiras propostas de aparência.

Aplicativo: Claude Alerta. O usuário aprovou **Claro**. A versão 1.1.0 aplica essa direção ao painel e ao aviso nativo, com os mesmos controles de monitoramento e duração de três segundos.

## Direções

- **Foco**: painel escuro e quente, com sessões à esquerda e preferências em uma coluna dedicada. A solicitação pendente recebe destaque e uma ação principal.
- **Claro**: aparência clara inspirada em aplicativos do Windows, lista horizontal de sessões e preferências visíveis abaixo. Favorece leitura rápida e familiaridade.
- **Compacto**: janela pequena de bandeja, com sessões e preferências em abas. Prioriza acesso rápido e pouco espaço ocupado.

Todas mostram os mesmos dados fictícios, preservam os avisos de três segundos, controles de som, pausa e lembretes. O botão de teste demonstra o recolhimento; não emite som nem controla o VS Code.

## Base de design

Foi utilizada a skill **UI/UX Pro Max**. Foram executadas três buscas `--design-system` e consultas de UX sobre notificações, foco, contraste, movimento reduzido e empilhamento.

As recomendações foram adaptadas a uma ferramenta de Windows: hierarquia curta, uma ação principal por região, tipografia Segoe UI, ícones lineares consistentes, texto junto aos estados, separação por espaço e divisores, e opções avançadas discretas. Padrões de landing page e fontes indicadas para outros setores não foram adotados.

Ícones do aplicativo: glifos vetoriais do Windows em Segoe Fluent Icons, com fallback em Segoe MDL2 Assets. Os códigos foram conferidos na [referência oficial da Microsoft](https://learn.microsoft.com/en-us/windows/apps/design/iconography/segoe-fluent-icons-font). O ícone do executável e da bandeja é uma renderização local do sino, sem redistribuir a fonte.

## Regras compartilhadas

- Aviso de três segundos, sem tomar o foco, com contagem e barra de tempo apenas durante o teste.
- Recolher não equivale a responder ao Claude.
- Sessões pendentes e trabalhando têm texto e ícone, além da cor.
- Preferências possuem rótulos, controles nativos e foco visível.
- Movimento reduzido remove a animação, preservando o recolhimento de três segundos.
- A composição se reorganiza em larguras pequenas.
- Direção aprovada: **Claro**, aplicada em WPF com os ícones vetoriais do Windows, textos e cores semânticas centralizadas em `ClaroTheme.xaml`.

As propostas exploraram os mesmos controles e estados de exemplo. A implementação da direção aprovada está em `Dashboard.xaml`, `AlertCard.xaml` e `ClaroTheme.xaml`. Capturas demonstrativas ficam em `docs/images/`, na raiz do repositório.
