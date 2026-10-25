# Validação realizada

Ambiente inicial: Windows x64, .NET SDK 10.0.113, MSVC 19.44 e Windows SDK 10.0.26100.

| Verificação | Resultado |
|---|---|
| Compilação .NET Release | Passou, sem avisos |
| Testes de protocolo/autoridade/lobby/ponte | 10 passaram, incluindo conexão TCP entre runtimes isolados |
| Compilação DLL C++20 x64 | Passou |
| CTest de filas, limites, concorrência e thread | Passou |
| Adaptador Lua 5.2 com objetos de fronteira próprios | 8 passaram, incluindo distribuição parcial e revelação por jogador |
| Codec JSON Lua próprio | 5 passaram |
| Fingerprint do executável instalado | Confere com o perfil |
| Bootstrap compilado e thread Lua no jogo | Passou |
| Universal-Modder scan | Executado, relatório privado em `.local/` |
| REA/Ghidra num PE nativo de verificação | Passou; ver manifesto de ferramentas |
| REA/Ghidra no jogo completo | Concluído após ampliar o limite inicial |
| Leitura e aplicação de ação em partida real | Call: stack 10000 → 9800, contribuição 200 e turno avançou |
| Mão completa no motor original com convidado TCP sem janela | Passou: preflop até resultado, pote 0, total 50000 fichas |
| Entrada pelo botão nativo do anfitrião | Call aceito pelo runtime e rodada avançou |
| Réplica visual, executada em seguida na mesma máquina | Heavy identificado, duas cartas próprias, cinco comunitárias e fichas corretas |
| Instalação, remoção e reinstalação | Passaram; arquivo alterado recusado e originais preservados |
| Mão completa entre duas máquinas | Pendente |
| Testes Radmin/Tailscale em duas máquinas | Pendentes |

Os testes cobrem frames fragmentados/truncados/grandes, incompatibilidade de build/protocolo,
disputa simultânea de lugar, prontidão e autoridade do anfitrião, replay e consumo único do
turno, apostas inválidas e projeção privada. Testes Lua cobrem a conversão da aposta para a
API do engine e cartas de jogadores que deram fold.

O contexto restrito inicialmente impediu abrir sockets; o teste TCP passou quando executado
com permissão de rede/processo. Isso é uma restrição do ambiente de teste, não uma falha do mod.

Não houve patch do executável. O instalador adiciona seu próprio descritor e pacote;
os originais permanecem intactos. O cache usa extensão `.bin` para não registrar recursos
extras quando o projeto está dentro da pasta do jogo. A ponte TCP foi testada com dois
runtimes de arquivos isolados; esse teste usa estados próprios e não comprova uma mão real.
Não usar esses resultados para anunciar compatibilidade multiplayer antes dos testes reais pendentes.

`evidence/runtime-tcp-hand.json` registra a conclusão de uma mão real com dois lugares
controlados, bots nos restantes e ações chegando por TCP. O convidado nesse teste foi o
aplicativo auxiliar, sem outra janela do jogo. Isso comprova o caminho da autoridade e
resolução original, mas não substitui M1 com dois jogadores e duas réplicas visuais.

As evidências registram os testes feitos, não uma certificação de cada alteração posterior.
A revelação individual no showdown, o tratamento da eliminação do lugar original e o
bloqueio do fast-forward solo foram ajustados depois dessa mão; exigem mais testes no motor.
O modo espectador solo não pode autorizar a transmissão de cartas ocultas durante apostas.
