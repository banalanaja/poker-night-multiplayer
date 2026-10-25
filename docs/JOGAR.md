# Jogar com um amigo

Esta é uma **prévia de testes**, ainda sem validação de uma partida entre dois jogos
completos por VPN. Não há base para prometer 90% de chance de tudo funcionar.

## Instalar

1. Cada pessoa precisa ter o jogo na Steam, na build suportada.
2. Baixe o ZIP Windows da versão de teste e extraia a pasta inteira, preferencialmente
   fora da pasta do jogo. Abra `PokerNightMultiplayer.exe`.
3. A pasta do jogo é detectada automaticamente; se necessário, use **Escolher pasta**.
4. Com o jogo fechado, clique em **Instalar / atualizar mod** e aguarde.

O aplicativo não exige SDK, terminal, Visual Studio nem instalação separada do .NET.
Na primeira instalação ele baixa a versão fixa do TTG Tools e verifica seu SHA-256.
Os scripts necessários são gerados a partir da instalação de cada participante.
Não envie os pacotes gerados ou os arquivos do jogo ao seu amigo.

## Criar e entrar

1. Conectem os dois computadores à mesma rede Radmin VPN ou Tailscale, ou usem LAN.
2. O anfitrião escolhe **Criar sala**, seu nome, personagem, IP da VPN e total de pessoas.
3. Clique em **Criar sala** e em **Copiar endereço**; envie o endereço ao amigo.
4. O amigo escolhe **Entrar na sala**, cola o endereço e escolhe outro personagem.
5. O amigo clica em **Entrar e ficar pronto**. Quando todos estiverem prontos, o jogo
   abre pela Steam. No jogo: **Clique para começar → Play Game**.

Mantenham o aplicativo aberto. Lugares restantes usam bots. A porta padrão é TCP 27841;
se o Windows pedir acesso à rede, permitam o aplicativo na rede usada pela VPN/LAN.
Não é necessário desativar o firewall. Sem VPN, o mod não configura roteador/NAT.

## Se algo falhar

Em **cada computador que apresentou problema**, clique em **Exportar diagnóstico**.
Salve o ZIP e envie o arquivo inteiro. Seu amigo pode enviá-lo para você, e você pode
anexar aqui no chat junto com o seu ZIP.

Descreva também o que vocês estavam fazendo, quem era anfitrião, quais personagens
escolheram e se usavam Radmin, Tailscale ou LAN. O ZIP contém versão, hash do executável,
eventos e erros estruturados, estágio da integração e números de mão/turno. Ele exclui
cartas, baralho, sementes, saves, nomes, IPs, caminhos pessoais e logs brutos.
Nada é enviado automaticamente pela internet.

## Encerrar ou remover

Use **Encerrar sessão** e feche o jogo. Para remover, feche o jogo e clique em
**Remover mod**. Os arquivos originais são preservados; o instalador remove apenas
os dois arquivos do mod cujos hashes correspondem ao manifesto.

Ferramenta externa: [TTG Tools 1.16.1 e código-fonte GPL-3.0](https://github.com/HeitorSpectre/TTG-Tools/releases/tag/v.1.16.1).
O download dela ocorre na máquina do usuário. O código próprio do mod é MIT.
