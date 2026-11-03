# DLSSG Mod Manager

**Ler em:** [English](../../README.md) · [Italiano](README.it.md) · [简体中文](README.zh-Hans.md) · [हिन्दी](README.hi.md) · [Español](README.es.md) · [Français](README.fr.md) · [العربية](README.ar.md) · [বাংলা](README.bn.md) · **Português** · [Русский](README.ru.md) · [اردو](README.ur.md)

Um aplicativo de desktop para Windows que acompanha o [DLSSG for SM86](https://github.com/sdli1995/dlssg_for_sm86): verifica o PC e o jogo, instala um pacote verificado do mod e mantém um histórico de alterações que permite recuperar o estado anterior.

## Créditos

- **Mod original:** [DLSSG for SM86](https://github.com/sdli1995/dlssg_for_sm86), de [sdli1995](https://github.com/sdli1995) e dos colaboradores do projeto.
- **Adaptação para RTX 20 / SM75:** [Coldwood1026](https://github.com/Coldwood1026), citado nos [avisos do projeto original](https://github.com/sdli1995/dlssg_for_sm86/blob/main/THIRD_PARTY_NOTICES.txt).
- **Este gerenciador:** uma interface independente de instalação e diagnóstico. O crédito pelo mod original continua com seus autores; o gerenciador não se apropria do trabalho deles nem afirma ter aprovação oficial.

![Prévia do aplicativo](../screenshot.png)

## Funcionalidades

- Ao iniciar, verifica o **pacote completo da versão oficial mais recente**, antes de analisar o hardware e o jogo. Baixa automaticamente um novo pacote e informa quando o cache verificado já está atualizado.
- Compara o ID da versão, o ID do arquivo e o SHA-256, incluindo arquivos substituídos sem mudar o número da versão. O estado offline é diferenciado de “versão mais recente”.
- Verifica a arquitetura e o driver NVIDIA, o Windows x64 e a configuração do agendamento de GPU. O agendamento só é confirmado como ativo quando o log do runtime do jogo informa isso.
- Aceita uma pasta de jogo ou seu EXE de renderização e encontra os jogos instalados nas bibliotecas Steam. Identifica os executáveis de renderização Unreal e permite resolver seleções ambíguas.
- Examina a arquitetura x64, a geração do runtime DLSS-G nativo, indícios de DirectX 12, indicadores conhecidos de anti-cheat, carregadores existentes, processos do jogo e espaço disponível. A presença de arquivos não certifica que um jogo funcionará.
- Mostra o mecanismo, multiplicador, otimização, predefinição, substituição de SR, configurações de fluxo/HUD do XeSS, carregador e opções de log. **Os padrões recomendados são DLSS, 2×, otimização com saída exata e substituição de SR desativada (Off).**
- Instala ou atualiza somente após revisão e confirmação. Prepara os arquivos verificados, faz backup do estado anterior, desativa os carregadores antigos antes de alterar os arquivos do runtime e ativa o novo carregador por último.
- Oferece desativação e restauração, protege os arquivos modificados fora do gerenciador e registra um diário de recuperação para alterações interrompidas. Os backups são mantidos, incluindo arquivos extras deslocados.
- Lê a última sessão de jogo registrada. Para confirmar a geração real de quadros, exige a criação bem-sucedida da função NGX 11 e sua primeira avaliação.
- Exporta um relatório de diagnóstico local com os caminhos do usuário e do jogo ocultados. Não exige contas nem coleta telemetria.

## Download e uso

Baixe a [prévia pública v0.1.0](https://github.com/VenomDrake/DLSSG-MANAGER-RTX-3000-SERIES/releases/tag/v0.1.0): [ZIP portátil](https://github.com/VenomDrake/DLSSG-MANAGER-RTX-3000-SERIES/releases/download/v0.1.0/DLSSGModManager-Portable-win-x64.zip) · [instalador do Windows](https://github.com/VenomDrake/DLSSG-MANAGER-RTX-3000-SERIES/releases/download/v0.1.0/DLSSGModManager-Setup.msi). Consulte [todas as versões](https://github.com/VenomDrake/DLSSG-MANAGER-RTX-3000-SERIES/releases) para encontrar versões posteriores.

Use `DLSSGModManager.exe` do pacote portátil ou execute `DLSSGModManager-Setup.msi`, que instala o programa para o usuário atual. Os binários publicados não exigem uma instalação separada de .NET, Python ou CUDA Toolkit.

1. Abra o gerenciador e deixe-o verificar ou baixar o pacote oficial do mod.
2. Escolha um jogo Steam, uma pasta de jogo ou seu EXE de renderização.
3. Revise todas as verificações. Um anti-cheat detectado, um runtime não compatível, um carregador desconhecido em conflito ou o jogo em execução impedem a instalação.
4. Revise as opções e comece com **2×**. Escolha **Instalar mod** ou **Atualizar mod** e confirme o destino.
5. Inicie o jogo e ative **DLSS Frame Generation** nas configurações gráficas. Alguns jogos exigem uma reinicialização.
6. Escolha **Ler registro do jogo** para verificar se o mod foi carregado, se a função foi criada e se a geração realmente começou.

O gerenciador não altera HAGS nem os drivers automaticamente. Ele abre as configurações gráficas do Windows ou a página de drivers da NVIDIA e fornece instruções. Reinicie o Windows depois de ativar HAGS.

## Compatibilidade e limitações

Esta primeira versão é destinada ao Windows 10/11 x64 e às GPUs RTX 20/30 compatíveis com o projeto original. O procedimento de instalação atualmente verificado corresponde à versão **0.4.1** do mod, com driver R580 ou mais recente e runtime 310.9.16. O gerenciador também baixa versões oficiais futuras, mas um procedimento desconhecido é marcado como não compatível até que suas verificações sejam atualizadas; um novo número de versão não comprova que os requisitos sejam compatíveis.

O jogo deve incluir DLSS Frame Generation nativo e ser executado com DirectX 12. O procedimento atual oferece suporte aos runtimes 310.x; gerações antigas são recusadas. Multiplicadores mais altos dependem da integração do jogo. O XeSS é limitado a 4×; DLSS 6× exige uma integração que o suporte.

Os indicadores conhecidos de anti-cheat bloqueiam a instalação. A ausência deles não comprova que um jogo não tenha anti-cheat. Não use mods injetados em jogos online protegidos por anti-cheat.

Instalações manuais existentes que correspondam à versão verificada atual podem ser gerenciadas após a verificação dos hashes das DLLs. Um mod manual antigo ou desconhecido sem um registro do gerenciador é tratado como conflito, em vez de ser sobrescrito. As instalações feitas posteriormente pelo gerenciador podem ser atualizadas e restauradas usando seu inventário e seus backups.

A primeira verificação em um log é um teste inicial, não uma garantia de estabilidade em sessões longas nem de duplicação dos FPS renderizados. Os quadros gerados exibidos e a latência dos comandos são medidas diferentes.

O gerenciador e o MSI não são assinados nesta compilação inicial. Nenhum certificado de assinatura é importado. O pacote original 0.4.1 usa um certificado de teste autoassinado cujo nome difere daquele indicado em seu README; a origem do pacote é verificada pelo resumo criptográfico do arquivo no GitHub, e não pela confiança do Windows no editor.

## Idiomas

O inglês é o idioma de origem; também estão disponíveis italiano, chinês simplificado, hindi, espanhol, francês, árabe, bengali, português, russo e urdu. O aplicativo segue um idioma de interface do Windows compatível e permite alterá-lo. Árabe e urdu usam um layout da direita para a esquerda. Os 11 arquivos de recursos têm as mesmas chaves; a revisão das traduções pela comunidade é bem-vinda. Valores de configuração avançados e detalhes de exceções de diagnóstico podem manter seus nomes técnicos originais.

## Dados locais e recuperação

- Cache verificado de pacotes e preferência de idioma: `%LOCALAPPDATA%\DLSSGModManager`.
- Inventário do jogo: `.dlssg-manager.json` ao lado do EXE de renderização.
- Backups: `.dlssg-manager-backups` ao lado desse EXE. Mantenha essa pasta para preservar o histórico de restauração.
- O arquivo `.dlssg-manager.transaction.json` indica uma operação interrompida; a próxima instalação, restauração ou desativação a recupera antes de continuar e exige que o jogo esteja fechado.

Use **Restaurar estado anterior** repetidamente para voltar pelas atualizações gerenciadas. Restaurar a primeira instalação gerenciada retorna ao estado original, incluindo uma instalação manual incorporada ao gerenciador. Desativar apenas move o carregador gerenciado para fora de seu nome de DLL ativo. O gerenciador não remove arquivos desconhecidos do usuário nem desfaz silenciosamente alterações externas nos arquivos gerenciados.

Desinstalar o gerenciador pelo Windows remove o aplicativo, não os mods instalados nos jogos. Restaure os jogos afetados antes de desinstalá-lo se também quiser remover esses mods. Os backups dos jogos e o cache baixado são mantidos.

## Compilação e testes

Instale o SDK do .NET 10 no Windows x64 e execute:

```powershell
.\scripts\test.ps1
.\scripts\build.ps1
```

O script de compilação publica um aplicativo autossuficiente em um único arquivo, um ZIP portátil, um MSI e somas de verificação SHA-256 em `artifacts`. Se necessário, WiX 4.0.6 é instalado como ferramenta de compilação em `tools`. A saída não inclui DLLs baixadas do mod.

Os testes usam pacotes sintéticos e uma DLL de teste x64 inofensiva com metadados de versão. Não exigem jogos, hardware NVIDIA nem conexão de rede. Há um teste opcional da versão online com `dotnet run --project tests/ReleaseTests -- --live`.

Para abrir uma prévia da interface em modo somente leitura sobre um jogo local:

```powershell
dotnet run --project src/DlssgManager.Desktop -- --read-only --game 'D:\Games\Example'
```

`--data-dir <path>` isola o cache e as preferências do gerenciador para testes. `--read-only` desativa alterações nos arquivos do jogo.

## Publicar seu fork

Crie um repositório público no GitHub e envie esta árvore de código-fonte. O workflow do Windows incluído compila e testa pull requests e envios à branch principal. Uma tag como `v0.1.0` também publica os binários como uma versão do GitHub usando o token do Actions do repositório. Nenhum token é salvo no código-fonte ou exigido dos usuários finais.

Consulte [CONTRIBUTING.md](../../CONTRIBUTING.md), [SECURITY.md](../../SECURITY.md) e [THIRD_PARTY_NOTICES.md](../../THIRD_PARTY_NOTICES.md). Este gerenciador independente é distribuído sob licença MIT; o material do mod original mantém seus próprios termos.
