# Poker Night Multiplayer

Mod em desenvolvimento para **Poker Night at the Inventory Remastered**, Windows x64.
Objetivo: poker por IP direto (LAN, Radmin VPN ou Tailscale), com 2–5 pessoas escolhendo
Max, Strong Bad, Heavy, Tycho ou o lugar original do jogador.

**Estado atual: prévia de testes, ainda sem validação entre dois jogos completos por VPN.**
O bootstrap Lua foi carregado no jogo real: a captura da mesa e uma ação de call foram
validadas, com desconto de fichas e avanço do turno. O transporte TCP agora conecta esse
runtime ao aplicativo auxiliar e projeta uma mão privada por destinatário.
A mão completa também passou no motor original com um convidado TCP sem janela.
A réplica e o HUD estão em validação. Uma mão entre duas máquinas ainda precisa ser comprovada.

## Instalar e jogar sem terminal

Baixe o [pacote Windows de testes](https://github.com/banalanaja/poker-night-multiplayer/releases/tag/v0.2.0-preview.2),
extraia e abra `PokerNightMultiplayer.exe`. O aplicativo detecta o jogo, instala os scripts,
cria/entra na sala e abre a Steam. Não é necessário instalar .NET ou ferramentas de desenvolvimento.
Veja o [guia para jogar com um amigo](docs/JOGAR.md).

Se houver erro, cada participante clica em **Exportar diagnóstico** e envia o ZIP inteiro.
Ele inclui contexto técnico e erros estruturados, sem cartas, baralho, saves, IPs, nomes ou
arquivos originais. [Conteúdo dos diagnósticos](docs/DIAGNOSTICS.md).

## O que existe

- Lobby com seleção exclusiva de personagem, prontidão, compatibilidade de versão e limite de cinco lugares.
- Modelos separados para mesa pública, mão privada e ações; nenhuma mensagem pública inclui baralho ou sementes.
- Rejeição de ações fora da vez, repetidas, de outra sessão/mão/turno e com valores inválidos.
- Adaptador Lua próprio para capturar a mesa, converter ações para a API do jogo e renderizar as cartas locais.
- Primitivas C++20 com filas limitadas e testes de concorrência e identificação da thread do jogo.
- Runtime Lua cooperativo na thread do jogo, IPC local tipado e réplica passiva dos convidados.
- Instalador que gera o pacote a partir da instalação do usuário, sem distribuir scripts originais.
- Aplicativo Windows com instalação assistida, lobby, abertura da Steam e diagnóstico em um ZIP.
- Perfil da build Steam `25257439`, com SHA-256 e integração **Lua** verificada; sem endereços nativos presumidos.
- Ferramentas de investigação instaladas localmente e ignoradas pelo Git; [manifesto](tools/tool-manifest.json).

## Compilar e testar

Requer .NET SDK 10, CMake 3.25+ e Visual Studio 2022 com ferramentas C++ x64 para a DLL.

```powershell
.\build.ps1 -Native
```

O aplicativo gráfico fica em `src/PokerNight.Desktop/bin/Release/net10.0-windows/`;
a CLI de desenvolvimento fica em `src/PokerNight.Launcher/bin/Release/net10.0/`.
O script mantém os caches .NET/NuGet em `.local/` e não atualiza ferramentas globais.
Os testes Lua podem ser executados com Lua 5.2, a partir da raiz:

```powershell
lua tests/lua/adapter_test.lua
lua tests/lua/json_test.lua
lua tests/lua/boot_guard_test.lua
```

Eles usam objetos de fronteira próprios; não contêm scripts originais do jogo.
Passar esses testes **não comprova uma alpha jogável**.

Para gerar o ZIP Windows com runtime incluído: `./tools/Publish-Windows.ps1`.

## Inspecionar compatibilidade

```powershell
dotnet run --project src/PokerNight.Launcher -- inspect 'CAMINHO_DO_JOGO' profiles/steam-25257439.json
```

Esse comando lê o executável e compara o SHA-256, sem iniciar o jogo ou aplicar hooks.
Uma atualização do jogo exige nova investigação e validação do perfil.

## Testar somente o lobby

Este diagnóstico não distribui cartas nem inicia a mesa. Execute o anfitrião numa janela
e o convidado em outra, usando o mesmo SHA-256 verificado por `inspect`:

```powershell
dotnet run --project src/PokerNight.Launcher -- lobby-host 127.0.0.1 27841 Anfitriao Max SHA256_DO_EXECUTAVEL
dotnet run --project src/PokerNight.Launcher -- lobby-join 127.0.0.1 27841 Convidado Heavy SHA256_DO_EXECUTAVEL
```

Para diagnosticar duas máquinas, selecione o IP da interface VPN no anfitrião e use esse
mesmo endereço no convidado. Ambos precisam da mesma build. A porta é TCP e configurável;
não é necessário desativar o firewall. Validação por VPN em duas máquinas ainda está pendente.

## Teste com o jogo

Consulte [instalação e comandos de teste](docs/PLAYTEST.md). O instalador exige TTG Tools
1.16.1 e o compilador LuaR compatível incluído nele. O pacote é criado localmente e adiciona
dois arquivos ao diretório `Archives`; a remoção confere seus hashes antes de apagá-los.

Os comandos `play-host` e `play-join` usam o jogo original como motor, não um simulador.
Eles são ferramentas de desenvolvimento; não representam uma alpha multiplayer validada.

## Funcionamento da partida

O anfitrião executa as regras e bots do jogo. Convidados recebem a mesa pública e
somente suas próprias cartas. A réplica conserva a câmera tradicional. A interface indica
personagem local, jogador da vez e ações disponíveis. O anfitrião é confiável e pode
tecnicamente conhecer o estado completo.

Personagens livres usarão bots. Desconectados terão check/fold até terminar a mão, seguido
de bot; entrada e reconexão ocorrerão no lobby. Sem migração de anfitrião na alpha.
Conquistas, recompensas Steam e gravação de progresso solo ficam fora da primeira versão.

Veja os [pontos de integração já encontrados](docs/INTEGRATION.md), o
[protocolo](docs/PROTOCOL.md), a [validação realizada](docs/VALIDATION.md) e os
[marcos pendentes](docs/ROADMAP.md).

## Conteúdo distribuído

MIT para o código próprio. O projeto não distribui jogo, executáveis de terceiros,
scripts extraídos, texturas, modelos, saves, dumps nem credenciais. Cada participante
precisa de sua própria instalação. Ferramentas externas mantêm suas licenças:
[REA](https://github.com/morluto/rea),
[Universal-Modder](https://github.com/rehan-remade/universal-modder),
[Ghidra](https://github.com/NationalSecurityAgency/ghidra) e
[TTG Tools](https://github.com/HeitorSpectre/TTG-Tools).

[Hand Teller](https://github.com/qequ/poker-night-hand-teller) foi consultado como referência
de investigação do remaster; sua documentação de heap não substitui a validação desta build.
Não há vínculo oficial com Skunkape Games, Telltale ou os titulares dos personagens.
