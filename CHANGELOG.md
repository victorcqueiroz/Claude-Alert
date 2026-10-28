# Histórico de versões

## 1.3.0 — 2026-10-28

### Adicionado

- Aba Atenção com fila por solicitação, sessões, recentes e contexto do projeto/ferramenta.
- Adiamento individual de 5, 15 ou 30 minutos, preservado após reiniciar.
- Histórico local com retenção de 24 horas e até 500 atividades.
- Perfis Normal, Ocupado e Silencioso, retorno programado e resumo da ausência.
- Lembretes progressivos com intervalos de 2, 5 e 15 minutos e limite de três repetições por solicitação.
- Aba Projetos com apelidos, fixação, silêncio, categorias e intervalos independentes.
- Diagnóstico de configuração/escrita da fila e indicação do último evento recebido.
- Pacote Windows com runtime incluído e arquivo SHA-256.

### Alterado

- “Já vi” substituído por adiamento explícito, preservando solicitações ainda sem resposta.
- Preferências globais recolhidas em um menu discreto.
- Conclusões e erros separados das solicitações pendentes.
- Correlação conservadora de ferramentas paralelas; resultados ambíguos não apagam permissões.
- Estados recuperados identificados como aguardando confirmação por novo evento.
- Os seis idiomas cobrem as novas telas. Tema Claro e avisos de três segundos preservados.

### Compatibilidade

- Preferências 1.2.0 mantêm idioma, som, popup e intervalo escolhido.
- Nenhuma mudança no protocolo de entrada dos hooks ou no caminho dos dados locais.
- Novas instalações usam lembretes progressivos; a atualização não muda automaticamente um intervalo legado.

## 1.2.0 — 2026-10-27

- Seleção persistente de português, inglês, espanhol, alemão, francês e chinês simplificado.
- Tradução imediata do painel, aviso, bandeja e mensagens, preservando pendências.

## 1.1.0 — 2026-10-27

- Tema Claro aprovado e interface WPF.
- Aviso de três segundos com recolhimento automático, sem solicitar foco.
- Monitor local de hooks do Claude Code, bandeja e preferências de avisos.
