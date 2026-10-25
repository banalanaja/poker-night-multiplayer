# Marcos

## M0 — Projeto e investigação

- [x] Repositório de código separado da instalação original.
- [x] Ferramentas locais com versões, origem e integridade registradas.
- [x] Identificar build, formato Lua e interfaces de cartas/turnos/apostas/IA.
- [x] Compilar .NET e C++; testar protocolo, lobby TCP e adaptador Lua isolado.
- [x] Bootstrap e thread Lua verificados dentro do jogo.
- [x] Demonstrar leitura de mesa e uma ação real, sem duplicação.

## M1 — Duas pessoas jogando

- [x] Implementar transporte TCP e IPC tipado ao motor original, na thread Lua.
- [x] Substituir decisões de IA nos lugares ocupados; Heavy validado por TCP no motor real.
- [ ] Suspender distribuição, IA e resolução locais nos convidados.
- [x] Implementar réplica da mesa, cartas privadas, HUD e entradas locais.
- [ ] Validar todos esses caminhos entre duas instâncias reais.
- [ ] Isolar saves, conquistas, recompensas e logs de cartas.
- [ ] Completar uma mão entre duas máquinas; documentar evidência.

## M2 — Cinco lugares

- [ ] Validar qualquer personagem e até cinco humanos.
- [ ] Adaptar bot para o lugar original quando livre.
- [ ] Sincronizar eventos de ação, animações e resultados.
- [ ] Testar all-in, potes paralelos, empates, eliminação e desconexão.

## M3 — Distribuição da alpha

- [x] Instalação e desinstalação reversíveis verificadas; recusa de hash alterado também testada.
- [ ] Validar duas máquinas por Radmin VPN e Tailscale.
- [ ] Testar vazamento de cartas nos pacotes, na réplica e nos logs.
- [ ] Publicar pacote de alpha somente após M1 e validação relevante de M2.

Um lobby ou simulador isolado não satisfaz M1. Nenhum pacote jogável foi publicado nesta etapa.
