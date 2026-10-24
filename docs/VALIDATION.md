# Validação realizada

Ambiente inicial: Windows x64, .NET SDK 10.0.113, MSVC 19.44 e Windows SDK 10.0.26100.

| Verificação | Resultado |
|---|---|
| Compilação .NET Release | Passou, sem avisos |
| Testes de protocolo/autoridade/lobby | 9 passaram, incluindo conexão TCP real em loopback |
| Compilação DLL C++20 x64 | Passou |
| CTest de filas, limites, concorrência e thread | Passou |
| Adaptador Lua 5.2 com objetos de fronteira próprios | 6 passaram |
| Fingerprint do executável instalado | Confere com o perfil |
| Bloqueio de `host` sem integração verificada | Recusou iniciar, como esperado |
| Universal-Modder scan | Executado, relatório privado em `.local/` |
| REA/Ghidra num PE nativo de verificação | Passou; ver manifesto de ferramentas |
| REA/Ghidra no jogo completo | Limite de 330 segundos atingido; não concluído |
| Leitura e aplicação de ação em partida real | Pendente |
| Mão completa entre duas máquinas | Pendente |
| Testes Radmin/Tailscale em duas máquinas | Pendentes |

Os testes cobrem frames fragmentados/truncados/grandes, incompatibilidade de build/protocolo,
disputa simultânea de lugar, prontidão e autoridade do anfitrião, replay e consumo único do
turno, apostas inválidas e projeção privada. Testes Lua cobrem a conversão da aposta para a
API do engine e cartas de jogadores que deram fold.

O contexto restrito inicialmente impediu abrir sockets; o teste TCP passou quando executado
com permissão de rede/processo. Isso é uma restrição do ambiente de teste, não uma falha do mod.

Não houve patch do executável. O único descritor de sondagem adicionado foi removido.
Não usar esses resultados para anunciar compatibilidade multiplayer antes dos testes reais pendentes.
