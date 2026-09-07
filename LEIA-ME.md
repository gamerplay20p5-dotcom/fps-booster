# FPS Booster 2.0 — FC 26 e Project Zomboid

Otimizador portátil para Windows 10/11 x64 com .NET Framework 4.8. O executável
se chama **FPS Booster.exe** e atende aos dois jogos.

Detecta CPU, núcleos, processadores lógicos, RAM utilizável, módulos de memória,
GPUs/drivers, SSD/HDD quando informado pelo Windows e alimentação por tomada/bateria.
Calcula um perfil inicial para cada máquina e jogo. Não é um benchmark automático:
a classificação de GPU é conservadora e os ganhos precisam ser medidos em gameplay.

## Começar

1. Abra `FPS Booster.exe`. O menu solicita elevação pelo Windows; `/diag` e
   `/selftest` funcionam sem administrador.
2. Use **G** para selecionar **EA SPORTS FC 26** ou **Project Zomboid**.
3. Use **1** para conferir a detecção. Se a instalação não aparecer, informe a
   pasta que contém o executável em **P**. Bibliotecas Steam são procuradas automaticamente.
4. Com o jogo fechado, use **2** para recalibrar, revisar e aplicar o perfil com backup.
5. Use **12** para iniciar a sessão otimizada. A energia e os limites de monitoração
   são recalculados a cada sessão; arquivos do jogo só mudam pela opção 2 ou 9.

O monitor abre pela Steam. Em instalações EA App/GOG ou com launcher alternativo,
abra pelo launcher enquanto ele aguarda. Se o jogo já estiver aberto, acompanha
a sessão existente. O prazo inicial é de três minutos; após detectar o jogo,
tolera até 15 segundos sem processos para permitir uma troca de launcher/processo.

## Menu

| Opção | Função |
|---|---|
| G / P | Selecionar jogo / informar instalação |
| 1 | Diagnóstico e calibração proposta para os dois jogos |
| 2 | Aplicar perfil de hardware ao jogo selecionado, com ele fechado |
| 3 | Consultar a política de CPU e energia |
| 4 | Reduzir manualmente memória residente de aplicativos escolhidos |
| 8 | Identificar consumidores de RAM para fechar manualmente |
| 9 | Perfis manuais do FC 26: 30 TRAVADO, FPS LIVRE, SOBREVIVÊNCIA |
| 11 | Verificar presença dos arquivos de mods do FC 26 |
| 12 | Sessão otimizada e monitor de memória |
| R | Sessão de referência: somente medição |
| 13 | Varredura de segurança herdada: somente relatório |
| 14 | Listar alterações e localização dos backups |
| 0 | Restaurar alterações registradas pela v2 neste PC/usuário |
| L | Restaurar backups da v1, com confirmação da máquina de origem |
| S | Sair |

```powershell
& '.\FPS Booster.exe' /diag
& '.\FPS Booster.exe' /jogo       # FC 26
& '.\FPS Booster.exe' /zomboid
& '.\FPS Booster.exe' /referencia # referência do FC 26; para Zomboid use G e R
& '.\FPS Booster.exe' /selftest
```

## CPU e energia

A sessão duplica o plano ativo, configura apenas os índices de CPU **na tomada**
e ativa a cópia temporária. Ajustes que o Windows/hardware rejeita são relatados.

| Hardware | Mínimo / máximo CPU | Turbo solicitado | EPP |
|---|---|---|---|
| Notebook, integrada/desconhecida ou alimentação incerta/bateria | 5% / 100% | Habilitado | 33 |
| Desktop com dedicada, FC 26 | 20% / 100% | Agressivo | 15 |
| Desktop com dedicada, Zomboid | 20% / 100% | Agressivo | 10 |

Esses valores são uma política inicial, não uma promessa de GHz. O mínimo de CPU
não fixa uma frequência física. O turbo continua sujeito à CPU, firmware,
temperatura e potência disponíveis. Em notebooks, forçar mínimo de 100% pode
consumir a margem térmica necessária ao jogo.

As configurações de bateria são herdadas do plano original. A troca para bateria é
detectada durante a sessão e o bloqueio de suspensão é liberado. Core parking,
afinidade, timer global, PCIe e USB permanecem sob as configurações herdadas.

Os processos reconhecidos do jogo podem subir para **Acima do normal**; prioridades
já mais altas são preservadas. Ao sair ou pressionar Ctrl+C, o monitor devolve a
prioridade anterior e o plano de energia. Ctrl+C deixa o jogo aberto.

Se o programa for encerrado à força, a próxima abertura do menu tenta recuperar o
plano pelo registro `sessao-energia.txt`. Essa recuperação exige a mesma pasta do
programa, máquina e usuário. Se você escolher outro plano durante a sessão, sua
escolha é respeitada. A restauração imediata de prioridade não é garantida em
encerramentos forçados; ela deixa de existir quando o processo do jogo termina.

## Memória durante a sessão

O monitor amostra a cada **5 s no Zomboid** e **10 s no FC 26**. Usa RAM disponível
e margem de memória comprometida. Pressão exige três amostras baixas, com margem
de recuperação para evitar oscilações e intervalo mínimo de três minutos entre ações.

Você escolhe os aplicativos permitidos antes da sessão: navegadores, Spotify ou
Teams. ENTER deixa somente a monitoração. A redução de memória residente é limitada
a três processos por ação, com pelo menos 150 MB residentes e dez minutos entre
ajustes no mesmo processo. O aplicativo em primeiro plano e os processos com o
mesmo nome são poupados. Jogos, Java, launchers, Discord, OBS e serviços não fazem
parte da lista permitida.

Isso pode ceder páginas de aplicativos de fundo ao Windows, mas não resolve um
vazamento de memória nem libera objetos vivos do Java. Aplicativos ativos podem
recarregar essas páginas. Para liberação duradoura, feche os aplicativos desnecessários.

A v2 removeu a limpeza global de working sets, standby, páginas modificadas e cache
de arquivos. Na v1, a etapa global podia atingir o jogo mesmo quando a etapa por
processo o excluía. Remover páginas residentes pode provocar page faults quando
elas voltam a ser acessadas. [Documentação de working sets da Microsoft](https://learn.microsoft.com/en-us/windows/win32/memory/working-set).

## Project Zomboid

A opção 2 modifica somente `-Xmx` no `vmArgs` principal de `ProjectZomboid64.json`.
Se um `-Xms` existente estiver acima de 1 GB, é reduzido para 1 GB para evitar uma
reserva inicial grande. Formatação, classpath, argumentos de mods/javaagent,
coletores de lixo e configurações específicas do Windows são preservados.

O limite considera a RAM utilizável, até metade dela, preservando margem de 4 GB
em máquinas com GPU dedicada estimada ou 5 GB com integrada/desconhecida. É arredondado
para baixo em blocos de 256 MB, com teto de 8 GB no perfil normal ou 12 GB para muitos
mods. Quando o orçamento não permite pelo menos 2 GB, o ajuste automático é recusado.

Exemplo: numa máquina com 15,71 GB utilizáveis, a proposta é **7936 MB de heap**.
Isso é um limite, não RAM reservada imediatamente, nem o consumo total do jogo:
texturas, memória nativa, mods e um servidor hospedado consomem memória adicional.
Feche aplicativos de fundo se a memória disponível estiver baixa antes de abrir.

O ajuste atende ao launcher padrão de 64 bits. JSON inválido, argumentos duplicados,
limites em outras seções e formatos desconhecidos são recusados. Launchers alternativos
e versões de 32 bits não têm o heap alterado. Atualizações/verificação da Steam podem
repor o arquivo: reaplique a opção 2 após conferir o novo conteúdo. O programa não
altera saves, mods, população de zumbis ou configurações de servidor.

## EA SPORTS FC 26

O perfil automático propõe 720p, escala 0,75 (ou 0,62 com pouca RAM) e teto de 30 FPS
para integrada/desconhecida. Com dedicada estimada e pelo menos 12 GB utilizáveis,
propõe 1080p e escala 1,0; o teto sobe a 60 FPS se houver oito processadores lógicos.
GPUs dedicadas antigas também podem exigir o perfil manual de 720p.

Os ajustes gráficos são gravados de forma coerente em:

- `%LOCALAPPDATA%\EA SPORTS FC 26\fcsetup.ini`
- `%LOCALAPPDATA%\EA SPORTS FC 26\settings\overrideAutodetect.lua`

Chaves desconhecidas são preservadas. O suporte às chaves Lua/INI herdadas depende
da versão do FC 26 e deve ser conferido dentro do jogo. A detecção não usa pastas
de outros anos do FC como alternativa. A v2 não desliga chat de voz nem impõe
somente-leitura; preserva atributos já existentes. A aplicação usa escrita atômica
por arquivo e tenta reverter o lote se uma escrita falhar.

## Backups e migração

Novos backups ficam em `backups\<identificador de máquina e usuário>\`. O manifesto
guarda o caminho completo, presença/ausência original e atributos dos arquivos.
O primeiro original é preservado em reaplicações. Se o backup faltar ou falhar,
a escrita é cancelada. Falhas de restauração permanecem registradas para nova tentativa.

Leve **executável e suas pastas de dados juntos** para conservar o histórico.
Outros PCs/usuários recebem conjuntos separados. Mantenha o programa numa pasta
gravável do usuário, não na pasta protegida do jogo.

Se você usava a v1, mantenha seus backups em `backups\estado-original.tsv`. Use **L** apenas
na máquina/usuário que os criou, preferencialmente **antes** de aplicar perfis v2.
Se houver alterações v2 pendentes, restaure-as pela opção 0 antes de restaurar o legado.
A v1 não registrava todos os atributos nem distinguia exclusões do Defender que já
existiam; esses dados não podem ser reconstruídos.

Se você usa o plano antigo `FIFA 26 BOOST`, ele permanece até você escolher restaurá-lo.
Como a v2 duplica o plano atual, ajustes antigos de parking, USB ou bateria também
podem ser herdados. A v2 não desfaz silenciosamente otimizações que você já usa.
As rotinas antigas de desativação de IA/serviços e exclusões do Defender ficaram
fora do menu de otimização v2; a restauração dessas alterações continua disponível.

## Comparar resultados

Use **R** antes de aplicar o perfil e **12** depois. Repita a mesma cena/save,
resolução, mods, alimentação e duração. Aquecimento, shader cache e aplicativos
abertos alteram o resultado: faça mais de uma passagem.

Cada sessão grava `logs\sessao-<jogo>-<referencia|boost>-<data>.csv` com RAM disponível,
memória comprometida/limite, memória residente/privada dos processos reconhecidos,
alimentação e quantidade de processos ajustados. O resumo mostra a menor RAM
disponível e os alertas. **CSV de RAM não comprova ganho de FPS.** Use também FPS,
1% low e frametime medidos no jogo. O diagnóstico WMI de clock não mede turbo efetivo.

## Compilar e verificar

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1 -Test
```

O build compila para um arquivo temporário, executa testes quando `-Test` é passado
e só então substitui o executável. O anterior fica em `FPS Booster.previous.exe`.
Backups locais de executáveis antigos não são incluídos na distribuição pelo GitHub.

Os testes usam arquivos temporários e comandos de energia simulados: calibração
4–64 GB, notebook/desktop, GPU, heap Java, preservação de mods, mesclagem INI/Lua,
pressão de RAM, cooldown, backup/restauração, rollback de lote e recuperação de
energia. Não aplicam otimizações ao Windows nem aos jogos instalados.

Referência de energia: [PERFBOOSTMODE — Microsoft](https://learn.microsoft.com/en-us/windows-hardware/customize/power-settings/options-for-perf-state-engine-perfboostmode).
Os limites de heap são uma política deste programa, não uma recomendação universal
dos desenvolvedores do Zomboid nem uma garantia de desempenho.
