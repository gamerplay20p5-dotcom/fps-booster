# FPS Booster

Otimizador portátil para **EA SPORTS FC 26** e **Project Zomboid**, com detecção
de hardware, perfis por jogo e monitoração de memória durante a sessão.

**Windows 10/11 x64 · .NET Framework 4.8 · interface em português**

## Download

Baixe `FPS Booster.exe` na [versão 2.0.0](https://github.com/gamerplay20p5-dotcom/fps-booster/releases/tag/v2.0.0)
e coloque em uma pasta gravável. Não é necessário instalar. O programa solicita
permissão de administrador para o menu de otimização.

Esta é uma versão inicial de distribuição. Os ganhos de FPS ainda precisam ser
validados em gameplay; os testes automatizados verificam o comportamento do código.

## Usar

1. Abra `FPS Booster.exe` e selecione o jogo com **G**.
2. Confira o diagnóstico na opção **1**.
3. Com o jogo fechado, aplique o perfil pela opção **2**.
4. Inicie a sessão otimizada pela opção **12**.

O programa oferece ajustes temporários de energia e prioridade, limite de heap
Java para o Zomboid, configurações gráficas para o FC 26 e limpeza seletiva de
aplicativos de fundo escolhidos pelo usuário. Os backups permitem restaurar
configurações; a opção **R** registra uma sessão de referência para comparação.

O ajuste de CPU respeita os limites de fábrica: não faz overclock. Os relatórios
de memória não substituem a medição de FPS e frametime dentro do jogo.

Veja o [manual completo](LEIA-ME.md) para detalhes, limitações e restauração.

## Compilar e testar

No PowerShell, dentro da pasta do projeto:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1 -Test
```

O build gera `FPS Booster.exe` usando o compilador C# do .NET Framework, com
fallback para o Visual Studio Build Tools. Os testes usam arquivos temporários
e comandos de energia simulados, sem alterar configurações do Windows ou dos jogos.

```powershell
& '.\FPS Booster.exe' /diag
& '.\FPS Booster.exe' /selftest
```

Logs, backups pessoais e executáveis gerados não fazem parte do histórico Git.
O executável para download é distribuído nas Releases.
