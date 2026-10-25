# Ferramentas locais de modding

Instaladas em `.local/tools/`, fora do conteúdo distribuível do mod. Versões, URLs e SHA-256 reais estão em `tool-manifest.json`. Nenhum arquivo original do jogo foi alterado. Não foi executado setup global, atualização global do Node ou habilitação do fal MCP.

| Ferramenta | Local relativo a `.local/tools/` | Verificação |
|---|---|---|
| Node 22.23.4 x64 | `node-v22.23.4-win-x64/node.exe` | `--version` passou; ZIP confere com SHA-256 oficial |
| REA 6.7.0 | `rea/node_modules/rea-agents/scripts/rea.mjs` | CLI, doctor e análise real Ghidra passaram |
| Ghidra 12.1.4 | `ghidra_12.1.4_PUBLIC/` | ZIP confere com digest oficial; provider Windows experimental |
| Universal-Modder 0.2.0 | `universal-modder-venv/Scripts/um.exe` | `--version` e `scan --help` passaram; fonte fixada no commit c1dff21663665dd17af289744a301b66e369605e |
| TTG Tools 1.16.1 | `ttg-tools-1.16.1/TTG Tools.exe` | ZIP confere com digest oficial; empacotamento usado pelo instalador local |

O Universal-Modder fornece CLI e referências de engenharia reversa nativa. Unity, BepInEx, MelonLoader e ferramentas IL2CPP não foram instalados: este jogo usa engine nativa Telltale. As funções de arte/fal não foram configuradas.

## Uso

Abra o executável TTG Tools pelo caminho acima. Os auxiliares Lua estão em `ttg-tools-1.16.1/LencTools/`. Trabalhe com cópias/extratos locais; não coloque arquivos do jogo no Git. A primeira abertura do TTG pode criar configurações no perfil do usuário e pastas Input/Output próximas ao programa.

Para o CLI Universal-Modder, a partir da raiz do projeto:

```powershell
& '.\.local\tools\universal-modder-venv\Scripts\um.exe' --version
& '.\.local\tools\universal-modder-venv\Scripts\um.exe' scan 'CAMINHO_DA_INSTALACAO' --json
```

REA foi registrado somente em `.codex/config.toml` deste projeto confiável. Reinicie/reconecte o cliente para descobrir o servidor. O arquivo usa caminhos absolutos locais e deve continuar ignorado pelo Git. Nenhum `rea setup` foi executado. A ausência de registros globais e skills pessoais no doctor é intencional.

No Windows, os caminhos desta instalação contêm `(x86)`. O launcher de Ghidra falhou quando recebeu os caminhos longos. A configuração usa os nomes curtos NTFS existentes e as opções Node `--preserve-symlinks --preserve-symlinks-main` para que também os scripts bridge mantenham caminhos sem parênteses. Não foram criados SUBST, links ou aliases de unidade. Se mover o projeto, gere novamente os caminhos curtos reais e atualize a configuração local.

O JDK existente é `C:\Program Files\Eclipse Adoptium\jdk-21.0.6.7-hotspot`; sua chamada direta de `bin/java.exe -version` passou. O PATH do servidor coloca esse JDK antes dos shims, sem modificar o PATH global.

## Integridade e limites

REA foi adquirido do registry npm, versão exata 6.7.0, com SHA-512 SRI conferido antes da instalação. Dependências foram instaladas com scripts automáticos npm desativados; Frida e outros providers opcionais não foram validados. O lockfile local registra a resolução. Universal-Modder foi construído com Hatchling num venv Python 3.12.10 após revisão de pyproject/CLI; NumPy 2.5.4, Pillow 13.0.0 e PyYAML 6.0.3.

Os ZIPs TTG/Ghidra conferem com os digests da API oficial de releases GitHub. Node confere com o arquivo oficial HTTPS SHASUMS; a assinatura destacada desse arquivo não foi validada. O ZIP Universal-Modder tem SHA-256 local registrado e URL fixada no commit; o upstream não publica digest para esse arquivo.

O REA passou no doctor e em análise real de seu executável auxiliar Ghidra decompile.exe (PE x64). O SHA-256 da fonte permaneceu inalterado. Isso valida inventário/análise estática nesse alvo, não compatibilidade de hooks ou análise completa do jogo; veja `verification.reaRealAnalysis` no manifesto. A execução restrita do agente pode impedir criação de snapshots privados com DACL; o teste nesse contexto retornou EACCES. Não reduza os controles do REA para contornar isso.

A análise do executável completo do jogo também terminou, com o prazo de inicialização
do Ghidra ampliado para 1200 segundos. O relatório permanece privado em `.local/`.
Isso não valida assinaturas nativas nem substitui as provas da integração Lua.

`Install-GameScripts.ps1` compila `TtgPackageBridge.cs`, um adaptador próprio que chama a
instalação externa por reflexão. O adaptador identifica a configuração apropriada usando
os dados da ferramenta instalada; nenhuma chave ou implementação de terceiros é distribuída.
O pacote é construído em um arquivo temporário fora da árvore de recursos do jogo.

Logs de diagnóstico ficam em `.local/tools/rea-doctor-ghidra.json` e `.local/tools/rea-inspect-*.json`. Eles não contêm credenciais e não precisam ser distribuídos. O manifesto e este README são os únicos registros de ferramentas destinados ao Git.

Fontes: [configuração MCP por projeto](https://learn.chatgpt.com/docs/extend/mcp?surface=cli), [REA Windows no tag instalado](https://github.com/morluto/rea/blob/rea-agents-6.7.0/docs/windows-ghidra-p0.md), [Node oficial](https://nodejs.org/en/download/archive/v22.23.4), [TTG Tools](https://github.com/HeitorSpectre/TTG-Tools/releases/tag/v.1.16.1).
