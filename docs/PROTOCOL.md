# Protocolo v2

A versão 2 acrescenta a ponte de partida e o turno associado aos erros de ação.
Clientes da versão 1 são recusados no handshake.

TCP por IP e porta escolhidos. Cada mensagem é JSON UTF-8 com envelope `{type,payload}`,
precedida por comprimento de 4 bytes big-endian. Limite: 65536 bytes; profundidade JSON: 32.
Frames incompletos, vazios, enums numéricos/desconhecidos e campos desconhecidos são rejeitados.

O diagnóstico de lobby implementa `hello`, `welcome`, `selectSeat`, `ready`, `lobby` e `error`.
O transporte do jogo acrescenta `publicTable`, `privateHand` e `action`.
Não há comandos para executar código.
Até quatro convidados se conectam ao anfitrião; handshake tem prazo de 10 segundos.

## Estado e autoridade

- `LobbyState`: sessão, versão, hash e ocupação/prontidão dos cinco lugares.
- `PublicTableState`: mão, turno, sequência, mesa, fichas e cartas já públicas.
- `PrivateHand`: sessão, mão, sequência, lugar e cartas exclusivas desse destinatário.
- `PlayerAction`: sessão, mão, turno, sequência monotônica, ação e quantidade de fichas.
- `HostTableFrame`: estado interno do anfitrião; nunca serializar no transporte de convidados.

O lugar de uma ação vem da conexão vinculada no lobby, não de um campo informado pelo
cliente. A autoridade rejeita ações inválidas e consome apenas uma ação por turno, mesmo
que duas mensagens tenham sequências diferentes. O engine continua responsável pela
legalidade final, fechamento de rodadas, potes paralelos e resultados.

Não há sincronização de RNG nem lockstep com baralho completo. A réplica opera
sem conhecer cartas adversárias ocultas. O anfitrião é confiável. A VPN fornece conectividade
e proteção de transporte conforme sua configuração; o mod não cria VPN nem abre portas no roteador.

## Desconexão

No diagnóstico, sair libera o lugar imediatamente. O estado de partida reserva o lugar até
o fim da mão, usa check se possível e fold caso contrário, e passa o lugar para bot na mão
seguinte. O driver executa essa política por IPC; a validação de desconexões no jogo real
ainda está pendente. Fechar o auxiliar anfitrião encerra as conexões dos convidados.
