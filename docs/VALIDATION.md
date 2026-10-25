# Validação realizada

Ambiente inicial: Windows x64, .NET SDK 10.0.113, MSVC 19.44 e Windows SDK 10.0.26100.

| Verificação | Resultado |
|---|---|
| Compilação .NET Release | Passou, sem avisos |
| Testes de protocolo/autoridade/lobby/ponte/aplicativo | 13 passaram, incluindo cinco conexões TCP, diagnóstico sem dados privados e pacote adulterado |
| Compilação DLL C++20 x64 | Passou |
| CTest de filas, limites, concorrência e thread | Passou |
| Adaptador Lua 5.2 com objetos de fronteira próprios | 8 passaram, incluindo distribuição parcial e revelação por jogador |
| Codec JSON Lua próprio | 5 passaram |
| Proteção Lua antes da inicialização original | 3 passaram: multiplayer ativo, solo e sessão encerrada |
| Aplicativo Windows independente do SDK | Publicado e aberto com runtime .NET incorporado |
| Instalação automática de ferramentas | Download com SHA-256 verificado, extração e instalação no jogo passaram |
| Exportação de diagnóstico pela interface | ZIP salvo e selecionado no Explorer; três arquivos permitidos |
| Lobby pela interface e convidado TCP | Dois lugares prontos, três bots e abertura pela Steam passaram |
| Abertura multiplayer com proteção antecipada | Menu alcançado; hash e timestamp de persistent.prop permaneceram iguais |
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

`evidence/desktop-hand.json` registra outra mão completa no motor original, com anfitrião
no aplicativo Windows e convidado TCP sem janela. O showdown revelou três lugares,
o pote terminou em zero e o total permaneceu em 50000 fichas. A saída do jogo encerrou
a sala. Isso ainda não valida duas réplicas visuais simultâneas nem duas máquinas.

`evidence/bootstrap-persistence.json` registra uma abertura posterior pela Steam até o
menu com a proteção antecipada do arquivo solo. O teste compara hash e timestamp;
não inclui o arquivo nem seus valores. Os três testes Lua adicionais verificam que a
proteção fica inativa no modo solo e após encerrar uma sessão.

Os testes do ZIP inserem cartas, baralho, sementes, nomes e mensagens privadas artificiais
nos arquivos locais e verificam a ausência desses dados em todas as entradas exportadas.
As evidências não certificam todos os personagens, eliminações ou isolamento completo
da progressão. O modo espectador solo não pode autorizar cartas ocultas durante apostas.
