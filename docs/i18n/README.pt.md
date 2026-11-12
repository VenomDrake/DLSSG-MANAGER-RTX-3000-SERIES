# DLSSG Mod Manager

**Ler em:** [English](../../README.md) · [Italiano](README.it.md) · [简体中文](README.zh-Hans.md) · [हिन्दी](README.hi.md) · [Español](README.es.md) · [Français](README.fr.md) · [العربية](README.ar.md) · [বাংলা](README.bn.md) · **Português** · [Русский](README.ru.md) · [اردو](README.ur.md)

Um aplicativo de desktop para Windows que acompanha o [DLSSG for SM86](https://github.com/sdli1995/dlssg_for_sm86) e [Apex Neural Rendering](https://github.com/apex-compute/ANR): verifica o PC e o jogo, instala um pacote verificado do mod e mantém um histórico de alterações que permite recuperar o estado anterior.

## Créditos

- **Mod original:** [DLSSG for SM86](https://github.com/sdli1995/dlssg_for_sm86), de [sdli1995](https://github.com/sdli1995) e dos colaboradores do projeto.
- **Adaptação para RTX 20 / SM75:** [Coldwood1026](https://github.com/Coldwood1026), citado nos [avisos do projeto original](https://github.com/sdli1995/dlssg_for_sm86/blob/main/THIRD_PARTY_NOTICES.txt).
- **Este gerenciador:** uma interface independente de instalação e diagnóstico. O crédito pelo mod original continua com seus autores; o gerenciador não se apropria do trabalho deles nem afirma ter aprovação oficial.

## Integração ANR — gerenciador estável 0.4.1

[Baixe a versão estável v0.4.1](https://github.com/VenomDrake/DLSSG-MANAGER-RTX-3000-SERIES/releases/tag/v0.4.1): [ZIP portátil](https://github.com/VenomDrake/DLSSG-MANAGER-RTX-3000-SERIES/releases/download/v0.4.1/DLSSGModManager-Portable-win-x64.zip) · [instalador do Windows](https://github.com/VenomDrake/DLSSG-MANAGER-RTX-3000-SERIES/releases/download/v0.4.1/DLSSGModManager-Setup.msi). O gerenciador é uma versão estável. **O ANR continua sendo a beta FG-V.B15 do projeto original**; o rótulo Beta refere-se à tecnologia, não a esta versão do gerenciador.

- Escolha **Tecnologia de geração de quadros**: SM86 original (NVIDIA DLSS-G / XeSS-FG) ou **Apex Neural Rendering · Beta**. O gerenciador aceita ANR em NVIDIA **GTX 16 e RTX 20–50**; **GTX 10 / Pascal não é compatível**. O jogo deve oferecer DLSS Frame Generation nativo por **Direct3D 12 ou Vulkan**.
- O gerenciador escolhe **Full** sem OptiScaler ou **Addon** para uma instalação OptiScaler compatível. Os pacotes são baixados diretamente da versão oficial e verificados por SHA-256; os binários e modelos ANR e OptiScaler **não acompanham o gerenciador**. A receita ANR revisada é **FG-V.B15**; futuras receitas desconhecidas ficam bloqueadas. O gerenciador consulta o repositório oficial do ANR ao iniciar quando ANR está selecionado e sempre que você escolhe ANR. Ele baixa uma versão nova automaticamente ou informa que a versão em cache já é a mais recente. Cada jogo só é atualizado quando você clica na ação de atualização, após as verificações e o backup.
- Feche o jogo antes de instalar ou trocar. O gerenciador verifica conflitos, registra arquivos gerenciados e cria backups recuperáveis antes das alterações. Troca de provedor, desativação e restauração compartilham diário e histórico; arquivos desconhecidos ou alterados externamente são protegidos. Addon preserva o carregador OptiScaler existente. Use **Restaurar estado anterior** para desfazer uma troca.
- Comece offline em **2×**. ANR possui ajustes próprios de caminho de cálculo, luminância HDR e mudanças de cena, além de seu log; as opções exclusivas de SM86 não se aplicam. Os multiplicadores dependem do jogo. A confirmação no log é um teste inicial, não garante estabilidade prolongada. Não use nenhum provedor em jogos online protegidos por anti-cheat.

Ative **NVIDIA DLSS Frame Generation** no jogo. O seletor **2×** cinza ou bloqueado pode ser normal se esse for o único multiplicador disponível; isso não comprova que a geração esteja ativa. Jogue um pouco e leia o log em **Diagnóstico**. Para ANR, confira as avaliações de geração bem-sucedidas e os erros, não apenas “ANR carregado”. Um log parcial ou grande demais não permite confirmar a sessão inteira. Se continuar pendente, feche o jogo e leia o log novamente.

**Créditos ANR:** [Apex Compute / Apex Neural Rendering](https://github.com/apex-compute/ANR); [OptiScaler](https://github.com/optiscaler/OptiScaler) e seus colaboradores. OptiScaler usa GPL-3.0; código, binários e modelos ANR mantêm os termos de seus autores. O gerenciador não altera essas licenças. Veja a [pergunta sobre licença](https://github.com/apex-compute/ANR/issues/9).

![Prévia do aplicativo](../screenshot.png)

![Configurações do mod ANR](../settings-anr.png)

## Funcionalidades

- Organiza o gerenciamento de jogos, as configurações do mod e o diagnóstico em três páginas claras, mantendo o fundo azul-escuro e os detalhes originais em verde-menta.
- Ao iniciar, verifica o **pacote completo da versão oficial mais recente**, antes de analisar o hardware e o jogo. Baixa automaticamente um novo pacote e informa quando o cache verificado já está atualizado.
- Compara o ID da versão, o ID do arquivo e o SHA-256, incluindo arquivos substituídos sem mudar o número da versão. O estado offline é diferenciado de “versão mais recente”.
- Verifica a arquitetura e o driver NVIDIA, o Windows x64 e a configuração do agendamento de GPU. O agendamento só é confirmado como ativo quando o log do runtime do jogo informa isso.
- Aceita uma pasta de jogo ou seu EXE de renderização e encontra os jogos instalados nas bibliotecas Steam. Identifica os executáveis de renderização Unreal e permite resolver seleções ambíguas.
- Examina a arquitetura x64, a geração do runtime DLSS-G nativo, indícios de DirectX 12, indicadores conhecidos de anti-cheat, carregadores existentes, processos do jogo e espaço disponível. A presença de arquivos não certifica que um jogo funcionará.
- Mostra o mecanismo, multiplicador, otimização, predefinição, substituição de SR, configurações de fluxo/HUD do XeSS, carregador e opções de log. **Os padrões recomendados são DLSS, 2×, otimização com saída exata e substituição de SR desativada (Off).**
- Instala ou atualiza somente após revisão e confirmação. Prepara os arquivos verificados, faz backup do estado anterior, desativa os carregadores antigos antes de alterar os arquivos do runtime e ativa o novo carregador por último.
- Oferece desativação e restauração, protege os arquivos modificados fora do gerenciador e registra um diário de recuperação para alterações interrompidas. Os backups são mantidos, incluindo arquivos extras deslocados.
- **SM86:** Lê a última sessão de jogo registrada. Para confirmar a geração real de quadros, exige a criação bem-sucedida da função NGX 11 e sua primeira avaliação.
- O diagnóstico do ANR distingue o carregamento da geração real e mostra as contagens de avaliações e erros de geração de uma sessão registrada completa.
- Exporta um relatório de diagnóstico local com os caminhos do usuário e do jogo ocultados. Não exige contas nem coleta telemetria.

## Como navegar

- **Gerenciamento de jogos:** escolha um jogo, veja o resultado de compatibilidade e use a ação principal de instalação ou atualização na parte inferior.
- **Configurações do mod:** veja todas as opções disponíveis, com os valores recomendados já selecionados.
- **Diagnóstico:** consulte todas as verificações e o registro do jogo, exporte um relatório, desative o mod ou restaure o estado anterior.

## Download e uso

[Baixe a versão estável v0.4.1](https://github.com/VenomDrake/DLSSG-MANAGER-RTX-3000-SERIES/releases/tag/v0.4.1): [ZIP portátil](https://github.com/VenomDrake/DLSSG-MANAGER-RTX-3000-SERIES/releases/download/v0.4.1/DLSSGModManager-Portable-win-x64.zip) · [instalador do Windows](https://github.com/VenomDrake/DLSSG-MANAGER-RTX-3000-SERIES/releases/download/v0.4.1/DLSSGModManager-Setup.msi).

Use `DLSSGModManager.exe` do pacote portátil ou execute `DLSSGModManager-Setup.msi`, que instala o programa para o usuário atual. Os binários publicados não exigem uma instalação separada de .NET, Python ou CUDA Toolkit.

> **Jogos online: risco de banimento**
>
> O uso deste mod altera os arquivos do jogo. Recomendamos fortemente usá-lo somente em jogos offline para um jogador. Os sistemas antitrapaça ou as regras do jogo podem considerá-lo uma modificação não autorizada e aplicar um banimento. Não é fornecido nenhum meio de contornar essas regras nem proteção contra banimentos.

1. Abra o gerenciador e deixe-o verificar ou baixar o pacote oficial do mod.
2. Em **Gerenciamento de jogos**, escolha um jogo Steam, uma pasta de jogo ou seu EXE de renderização.
3. Leia o painel de resultados e abra **Diagnóstico** para ver todas as verificações. Um anti-cheat detectado, um runtime não compatível, um carregador desconhecido em conflito ou o jogo em execução impedem a instalação.
4. Revise todas as opções em **Configurações do mod** e comece com **2×**. Escolha **Instalar mod** ou **Atualizar mod** na barra de ações inferior e confirme o destino.
5. Inicie o jogo e ative **DLSS Frame Generation** nas configurações gráficas. Alguns jogos exigem uma reinicialização.
6. Em **Diagnóstico**, escolha **Ler registro do jogo** para verificar se o mod foi carregado, se a função foi criada e se a geração realmente começou.

O gerenciador não altera HAGS nem os drivers automaticamente. Ele abre as configurações gráficas do Windows ou a página de drivers da NVIDIA e fornece instruções. Reinicie o Windows depois de ativar HAGS.

## Atualizar o gerenciador

O gerenciador verifica silenciosamente suas próprias versões do GitHub em segundo plano. Um pequeno aviso aparece somente quando há uma versão estável mais recente do gerenciador; rascunhos e pré-lançamentos são ignorados.

Tanto a versão estável **0.3.1** quanto **0.4.0 beta** podem atualizar para a estável **0.4.1** por **Atualizar e reiniciar**. A atualização aparece no canal estável, sem adesão às betas.

Escolha **Atualizar e reiniciar** quando for conveniente, após terminar a operação atual. O gerenciador seleciona automaticamente o ZIP portátil ou o MSI adequado à instalação atual, baixa o pacote, verifica seu SHA-256 com o resumo criptográfico do arquivo da versão oficial do GitHub, fecha, instala a atualização e abre novamente. Os jogos, as configurações e os backups são mantidos. As próximas atualizações não exigem visitar o GitHub nem substituir arquivos manualmente.

**A migração de 0.1.0 para 0.2.0 exige uma única atualização manual**, pois a versão 0.1.0 não contém o atualizador. Feche a versão 0.1.0 e execute o novo MSI ou extraia o novo pacote portátil completo na pasta do aplicativo portátil, usando o mesmo tipo de pacote de antes. A partir da versão 0.2.0, as próximas atualizações do gerenciador podem ser instaladas no aplicativo.

**Correção única para instalações MSI 0.2.0/0.2.1:** a atualização MSI pelo aplicativo nessas versões pode falhar com o erro **1639** do Windows Installer ou abrir a janela de ajuda dele. Feche o gerenciador, baixe e execute **uma vez o MSI mais recente (0.2.2 ou posterior)** da versão indicada acima, mantendo a mesma pasta de instalação. Instalar o MSI 0.2.1 não corrige essa falha. Depois de instalar a versão 0.2.2, as próximas atualizações pelo aplicativo usarão o atualizador corrigido. As instalações portáteis não são afetadas por esse problema de MSI.

As atualizações do gerenciador e dos mods são independentes. Atualizar o gerenciador não altera nenhuma instalação nos jogos. Os procedimentos revisados são **SM86 0.4.1** e **ANR FG-V.B15**.

## Compatibilidade e limitações

O gerenciador funciona no **Windows 10/11 x64**. **SM86** destina-se às GPUs **RTX 20/30** compatíveis; o procedimento revisado corresponde ao mod **0.4.1**, com driver **R580 ou mais recente** e runtime **310.9.16**. **ANR** aceita **GTX 16 e RTX 20–50**, exige DLSS Frame Generation nativo por **Direct3D 12 ou Vulkan** e usa o procedimento revisado **FG-V.B15**. **GTX 10 / Pascal não é compatível.** Baixar uma versão nova do projeto original não aprova seus requisitos: procedimentos desconhecidos continuam bloqueados até a atualização do gerenciador.

**SM86:** O jogo deve incluir DLSS Frame Generation nativo e ser executado com DirectX 12. O procedimento atual oferece suporte aos runtimes 310.x; gerações antigas são recusadas. Multiplicadores mais altos dependem da integração do jogo. O XeSS é limitado a 4×; DLSS 6× exige uma integração que o suporte.

Os indicadores conhecidos de anti-cheat bloqueiam a instalação. A ausência deles não comprova que um jogo não tenha anti-cheat. Não use mods injetados em jogos online protegidos por anti-cheat.

**SM86:** Instalações manuais existentes que correspondam à versão verificada atual podem ser gerenciadas após a verificação dos hashes das DLLs. Um mod manual antigo ou desconhecido sem um registro do gerenciador é tratado como conflito, em vez de ser sobrescrito. As instalações feitas posteriormente pelo gerenciador podem ser atualizadas e restauradas usando seu inventário e seus backups. **ANR:** Uma instalação manual do ANR não gerenciada é tratada como conflito.

A primeira verificação em um log é um teste inicial, não uma garantia de estabilidade em sessões longas nem de duplicação dos FPS renderizados. Os quadros gerados exibidos e a latência dos comandos são medidas diferentes.

O gerenciador e o MSI não são assinados nesta compilação. Nenhum certificado de assinatura é importado. O pacote original 0.4.1 usa um certificado de teste autoassinado cujo nome difere daquele indicado em seu README; a origem do pacote é verificada pelo resumo criptográfico do arquivo no GitHub, e não pela confiança do Windows no editor.

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

Crie um repositório público no GitHub e envie esta árvore de código-fonte. O workflow do Windows incluído compila e testa pull requests e envios à branch principal. Uma tag como `v0.4.1` também publica os binários como uma versão do GitHub usando o token do Actions do repositório. Nenhum token é salvo no código-fonte ou exigido dos usuários finais.

Consulte [CONTRIBUTING.md](../../CONTRIBUTING.md), [SECURITY.md](../../SECURITY.md) e [THIRD_PARTY_NOTICES.md](../../THIRD_PARTY_NOTICES.md). Este gerenciador independente é distribuído sob licença MIT; o material do mod original mantém seus próprios termos.
