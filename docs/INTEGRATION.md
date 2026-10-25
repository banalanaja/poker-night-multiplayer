# Integração com o remaster

## Identificação verificada localmente

- Steam App ID: 3897800; build instalada: 25257439.
- Executável PE x64 `CelebrityPoker.exe`; engine Telltale/Skunkape.
- Build interna: `2026.09.09 11:34 100 92-RC`.
- Bytecode dos scripts: Lua 5.2 (`LuaR`), números de 4 bytes e tamanho de string serializado de 4 bytes.
  Isso descreve o formato do bytecode; não comprova a ABI das funções C no executável.
- Arquivos `.ttarch2` e descritores de recursos; scripts extraídos e descompilados apenas em `.local/`.

## Pontos encontrados por inspeção dos scripts

| Responsabilidade | Interface encontrada | Uso no adaptador |
|---|---|---|
| Cinco lugares | `GameTable:Initialize`, `GetPlayers` | Ordem Max, Strongbad, Heavy, Tycho, Player |
| Baralho | `Deck:Shuffle`, `Draw`; `GameTable:GetDeck` | Somente no anfitrião; nunca enviar o baralho ou a semente |
| Distribuição | `GameTable:DoRoundState`, `state:Deal` | Somente no anfitrião; convidado não executa torneio |
| Ação humana | `HumanPlayer:EvaluateAndAct`, `QueueAction` | Suspender decisão original em lugares remotos |
| Ação dos bots | `AIPlayer:EvaluateAndAct` | Preservar nos lugares sem dono; adaptar o lugar original |
| Turnos | `GameTable:DoRoundState`, `Player:EndTurn` | Criar IDs de mão/turno e consumir uma ação uma única vez |
| Apostas | `Player:Call`, `Raise`; `GameTable:GetCallValue` | Conversão implementada em `applyAction` |
| Potes e resultados | `PotManager`, `GameTable:ResolvePot` | Reutilizar regras originais; testes reais ainda pendentes |
| Cartas privadas | `Player:GetHand().holeCards` | Estado local do anfitrião, projetado por destinatário |
| Renderização | `CardRenderer_Render`, `Player:ShowHoleCardUI` | Cartas locais e HUD implementados; réplica em validação |
| Estado global | `GameObject:GetTable()` | Acesso somente após a cena estar pronta |

**Detalhe importante:** no protocolo, `amount` de um raise é a quantidade de fichas paga
agora pelo jogador. A API `Player:Raise` recebe o incremento sobre o valor de call da mesa.
O adaptador subtrai `toCall` antes de chamar a API; passar o valor bruto aumenta a aposta
indevidamente. Essa conversão tem teste Lua próprio.

O loop original resolve o fold após receber a ação. O adaptador retorna FOLD sem chamar
`Fold` antecipadamente, evitando atualizar a mão/pote duas vezes.

## Bootstrap e ação comprovados

O pacote adicional intercepta `_system.lua`, chama o bootstrap original extraído localmente
e inicia `Runtime.lua` com `ThreadStart`. Esse mecanismo executa na thread Lua da engine e
dispensa injeção nativa para a prova atual. O instalador compila os scripts com LuaR 5.2
antes de empacotá-los; texto Lua nesse ponto causou inicialização sem concluir.

O descritor usa prioridade de dados 1000, superior ao recurso original de prioridade 0.
O pacote possui scripts próprios e uma cópia do bootstrap obtida da instalação do usuário.
Os dois arquivos gerados ficam fora da distribuição Git. Descritores e arquivos de recursos
de teste devem ficar fora da pasta do jogo; o cache do instalador usa extensão `.bin`.

A prova real leu a mesa, recebeu um call do lugar Player, reduziu seu stack de 10000 para
9800, registrou contribuição 200 e avançou o turno. A evidência sem cartas está em
`evidence/runtime-m0.json`. REA/Ghidra concluiu a análise completa após ampliar o prazo;
os resultados permanecem privados em `.local/`.

## Transporte e réplica

`FileGameRuntime` troca JSON limitado por arquivos locais. O anfitrião captura `host-frame`,
que permanece local; `GameTransport` envia mesa pública e uma mão privada por conexão.
`GameClient` combina as mensagens pela sequência e entrega uma visão ao Lua convidado.
Não existe RPC para executar Lua arbitrário. Comandos carregam sessão, mão e turno.

Um proxy de entrada humana controla os botões sem substituir os objetos que executam regras.
O anfitrião intercepta a avaliação dos lugares humanos; os demais continuam com a IA original.
O convidado suspende o torneio e aplica fichas, contribuições, cartas públicas e sua mão.
Durante a distribuição, mãos de apenas uma carta não são projetadas para a rede.

Persistência é sobreposta em memória; gravações, conquistas e debug que imprime cartas são
suprimidos na sessão. A réplica completa, animações e uma mão em duas máquinas ainda exigem
validação. `runtimeHooksVerified=true` registra a prova da integração Lua, não assinaturas C.
A DLL continua opcional, sem injetor ou hooks nativos ativos.
