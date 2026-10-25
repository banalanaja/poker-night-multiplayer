# Diagnósticos para compartilhar

Seu amigo clica em **Exportar diagnóstico**, salva um ZIP e envia esse arquivo para você.
Você pode anexá-lo aqui junto com o ZIP da sua máquina. Não precisa procurar pastas,
copiar arquivos internos nem editar logs manualmente. Nada é enviado automaticamente.

O aplicativo abre a pasta com o arquivo selecionado depois de salvar.

## O que está no ZIP

| Arquivo | Conteúdo |
|---|---|
| `diagnostico.json` | Versão do mod/protocolo/Windows, arquitetura, hashes, presença dos arquivos do mod, porta, categoria da conexão, detecção de VPN, estado e estágio do runtime, números de mão/turno/sequência |
| `eventos.json` | Até 200 eventos: abertura, instalação, conexão, início/fim, falhas e códigos técnicos |
| `LEIA-ME.txt` | Instruções para enviar e descrever o problema |

Erros podem incluir tipo da exceção, HResult, código de socket/HTTP, etapa e código de saída do
instalador, e nomes dos métodos do próprio mod envolvidos. Mensagens brutas de exceção,
pilhas com caminhos locais e payloads recebidos pela rede não são copiados.

Não entram cartas (nem comunitárias), mãos adversárias, baralho, sementes, saves, nomes de
jogadores, IPs, caminhos pessoais, credenciais, executáveis ou pacotes do jogo.
O exportador cria os três arquivos a partir de campos permitidos; ele **não compacta a
pasta de runtime**. Testes inserem dados privados artificiais nos arquivos internos e
confirmam que esses dados não aparecem em nenhuma entrada do ZIP.

## Como descrever o problema

Junto dos ZIPs, informe quem criou a sala, quais personagens escolheram, se usavam
Radmin/Tailscale/LAN, em que etapa falhou e o que apareceu na tela. Se o problema só
ocorreu numa máquina, o ZIP dela é o mais importante; os dois ajudam a comparar versões.

## Eventos e estágios úteis

- `install_failed`: etapa de compilação/empacotamento/manifesto e código de saída.
- `session_failed`: conexão, versão, personagem ocupado ou comunicação encerrada.
- `runtime_failed`: erro detectado dentro do jogo.
- `input_proxy`, `host_hooks`, `replica_hooks`: etapa em que a integração começou a falhar.
- `table` / `input`: captura/réplica da mesa ou interface de ações.

Os eventos persistem entre aberturas para preservar falhas anteriores. Arquivos internos
como `host-frame.json` podem conter cartas completas e **não devem ser enviados**.
O botão de exportação já faz a seleção segura do que compartilhar.
