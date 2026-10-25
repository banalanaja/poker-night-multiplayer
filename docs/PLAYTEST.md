# Teste de desenvolvimento

Esta versão ainda precisa concluir uma mão entre duas máquinas. Use instalações próprias
da mesma build, Steam ativa e o perfil fornecido. Não misture uma cópia do jogo com outra:
guarde cópias de teste fora da pasta da instalação, pois o carregador pode encontrar seus recursos.

## Preparar cada máquina

Compile com `./build.ps1`. Feche o jogo e execute, na raiz do repositório:

```powershell
./tools/Install-GameScripts.ps1 -GameDirectory 'C:\Jogos\Poker Night' -TtgDirectory 'C:\Ferramentas\TTG Tools'
```

O diretório de runtime padrão é `.local/runtime` no repositório. O instalador extrai somente
o bootstrap necessário da instalação própria, compila os scripts do mod e cria um arquivo
de recursos e um descritor adicionais. Nada do jogo original deve ser enviado ao GitHub.
Uma atualização do executável é recusada até existir um perfil validado.

## Abrir uma sala

No anfitrião, escolha o endereço local da LAN/VPN e a quantidade de humanos, de 2 a 5:

```powershell
dotnet src/PokerNight.Launcher/bin/Release/net10.0/PokerNight.Launcher.dll play-host 100.64.0.10 27841 Ana Player 2 'C:\Jogos\Poker Night' profiles/steam-25257439.json .local/runtime
```

No convidado, use o mesmo IP/porta e outro lugar:

```powershell
dotnet src/PokerNight.Launcher/bin/Release/net10.0/PokerNight.Launcher.dll play-join 100.64.0.10 27841 Bruno Heavy 'C:\Jogos\Poker Night' profiles/steam-25257439.json .local/runtime
```

Lugares aceitos: `Max`, `StrongBad`, `Heavy`, `Tycho`, `Player`. A CLI escolhe o lugar e
marca prontidão; o anfitrião autoriza o início quando o número solicitado estiver pronto.
Depois de configurar a sessão, abra o jogo pela Steam e avance pelo título. Se permanecer
no menu, clique em Play Game. Use os botões nativos de call/check, fold e raise na sua vez.
O aplicativo auxiliar deve permanecer aberto durante a sessão.

TCP não tem servidor central, criptografia própria nem abertura automática de portas.
Use uma LAN confiável ou VPN configurada para os participantes. A compatibilidade real
com Radmin VPN/Tailscale ainda precisa de teste em duas máquinas.

## Encerrar e voltar ao solo

```powershell
dotnet src/PokerNight.Launcher/bin/Release/net10.0/PokerNight.Launcher.dll stop .local/runtime
```

Feche o jogo após encerrar a sessão. Para voltar ao modo original, remova
`.local/runtime/session-config.json` com o jogo fechado, ou desinstale:

```powershell
./tools/Install-GameScripts.ps1 -GameDirectory 'C:\Jogos\Poker Night' -Uninstall
```

O manifesto deve permanecer no mesmo runtime usado na instalação. A remoção não altera
os arquivos originais. O runtime separa a persistência em memória e bloqueia gravações e
conquistas nas sessões do mod; não reutilize uma sessão multiplayer para continuar um save solo.

## Diagnóstico sem cartas

`status.json` registra apenas estágio, sessão e flags de funcionamento. Os auxiliares não
imprimem mãos privadas. `host-frame.json` é IPC efêmero do anfitrião e contém as mãos completas;
não é um log para compartilhar. Convidados recebem `view.json` com mesa pública e somente
sua mão. Não publique `.local/`, pacotes gerados, dumps ou arquivos da instalação.
