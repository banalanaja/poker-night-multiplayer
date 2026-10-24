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
| Distribuição | `GameTable:DoRoundState`, `state:Deal` | Bloquear no futuro driver dos convidados |
| Ação humana | `HumanPlayer:EvaluateAndAct`, `QueueAction` | Suspender decisão original em lugares remotos |
| Ação dos bots | `AIPlayer:EvaluateAndAct` | Preservar nos lugares sem dono; adaptar o lugar original |
| Turnos | `GameTable:DoRoundState`, `Player:EndTurn` | Criar IDs de mão/turno e consumir uma ação uma única vez |
| Apostas | `Player:Call`, `Raise`; `GameTable:GetCallValue` | Conversão implementada em `applyAction` |
| Potes e resultados | `PotManager`, `GameTable:ResolvePot` | Reutilizar regras originais; testes reais ainda pendentes |
| Cartas privadas | `Player:GetHand().holeCards` | Estado local do anfitrião, projetado por destinatário |
| Renderização | `CardRenderer_Render`, `Player:ShowHoleCardUI` | Renderização local própria; HUD e réplica ainda pendentes |
| Estado global | `GameObject:GetTable()` | Acesso somente após a cena estar pronta |

**Detalhe importante:** no protocolo, `amount` de um raise é a quantidade de fichas paga
agora pelo jogador. A API `Player:Raise` recebe o incremento sobre o valor de call da mesa.
O adaptador subtrai `toCall` antes de chamar a API; passar o valor bruto aumenta a aposta
indevidamente. Essa conversão tem teste Lua próprio.

O loop original resolve o fold após receber a ação. O adaptador retorna FOLD sem chamar
`Fold` antecipadamente, evitando atualizar a mão/pote duas vezes.

## Ainda não comprovado em runtime

Não foram validados endereços, assinaturas de função, captura de `lua_State`, callback na
thread do jogo, bootstrap, hooks de entrada, driver passivo dos convidados, sincronização
de animações ou HUD completo. Não há injetor distribuído nem perfil com RVAs presumidos.

A primeira sondagem por descritor adicional não produziu confirmação. O arquivo temporário
foi removido. Execuções de diagnóstico abriram uma janela branca; o usuário confirmou que
o jogo funciona normalmente quando iniciado pela Steam. O controle do computador foi
interrompido por Escape, inclusive após autorização de retomada; não há evidência de uma
mão real controlada pelo mod. Isso não é evidência de incompatibilidade do jogo.

REA/Ghidra funcionou num executável nativo de verificação. A análise completa do jogo
atingiu o limite inicial de 330 segundos; isso também não prova incompatibilidade.

## Próxima prova necessária

1. Iniciar pela Steam com a pasta/ambiente corretos e confirmar a cena de poker.
2. Identificar e validar o callback da engine que permite operar o estado Lua na thread correta.
3. Capturar uma mesa real e aplicar exatamente uma ação legal no loop original.
4. Somente então ligar o driver de partida ao transporte e implementar a réplica passiva.
5. Suprimir logs originais que imprimem mãos completas e isolar persistência/conquistas antes de habilitar multiplayer.

`runtimeHooksVerified` deve continuar falso até a prova 3 estar documentada. Alterar esse
campo manualmente não implementa um loader nem torna o mod jogável.
