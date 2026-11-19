# Sincronizar marcações do minimapa com a conta HoYoLAB

Data: 2026-11-15 · Branch: `feat/i18n-pt-br`

## Intenção

O usuário joga em pt-BR e usa o minimapa sobreposto (mapa grande do jogo) com a fonte de pontos **HoYoLAB**.
Hoje, clicar com o botão direito num ponto o "desativa" (marca como já coletado), mas isso fica só num arquivo
local. Ele quer que essa marcação seja a mesma do **mapa interativo da HoYoLAB** (o "pino" do site):

- marcar dentro do jogo → fica salvo na conta e aparece marcado no site/celular;
- marcar no site/celular → aparece desativado no minimapa;
- ver no minimapa tudo o que já pegou (a conta já tem 457 pontos marcados).

## Objetivos

1. Login na HoYoLAB a partir do BetterGI usando a página oficial de login dentro de um WebView2 (abordagem A).
2. Com login e fonte = HoYoLAB: o conjunto de pontos desativados vem da conta.
3. Botão direito num ponto → marca/desmarca na conta (`add_mark_map_point` / `new_del_mark_map_point`).
4. Botão para enviar à conta as marcações locais antigas da fonte HoYoLAB.
5. Sair da conta (apaga a sessão guardada).

## Não-objetivos

- Sincronizar as fontes "米游社" (Mihoyo CN) e "空荧酒馆" — IDs de ponto diferentes, contas diferentes.
- Detectar automaticamente que o item foi coletado (OCR/pickup). A marcação continua manual (botão direito).
- Pontos que renascem (`fresh_end_time`): todos os 457 atuais têm `fresh_end_time` = 2042, ou seja, permanentes.
  Ignorar esse campo por enquanto (comentário no código).
- Taxa de exploração, recompensas de compartilhamento e outras APIs do site.

## Anti-objetivos (seria falha mesmo funcionando)

- Pedir ou guardar a senha da HoYoLAB no BetterGI.
- Enviar os cookies para qualquer domínio que não seja `*.hoyolab.com`, ou escrever valores de cookie no log.
- Quebrar o uso sem login: sem login, tudo continua exatamente como hoje (arquivo local).
- Travar a interface esperando a rede.

## Como funciona hoje (código existente)

- Fonte de pontos: `MapMaskConfig.MapPointApiProvider` (`MihoyoMap`/`KongyingTavern`/`HoYoLab`) e
  `MapMaskConfig.HoYoLabLanguage` (`en-us`/`pt-pt`/`es-es`).
- API pública (sem login): `Service/HoYoLabMapApiService.cs` — label tree, point list, point info, em
  `sg-public-api-static.hoyolab.com`, com `Referer: https://act.hoyolab.com/` e `x-rpc-map_version: 4.5`.
- Marcação local: `ViewModel/MaskWindowViewModel.cs` — `OnPointRightClick` → `ToggleMapPointHidden`;
  `_hiddenMapPointIds` com chave `HoYoLab:{lang}:{pointId}`; persistido por `MapMaskStateStorage`
  em `User/MapMask/{dataSourceKey}.json` (`HoYoLab_{lang}`). Também existem `HideAllMapPoints`/`ShowAllMapPoints`.
- UI das opções do mapa: `View/MaskWindow.xaml` (~linha 1120), ComboBoxes de fonte e idioma.
- WebView2: `Infrastructure/WebView/` — `IWebViewEnvironmentProvider`, `WebViewProfiles` (perfis com cookies
  isolados, aparecem na janela de armazenamento).

## API da HoYoLAB (capturada do HAR do usuário)

Base: `https://sg-public-api.hoyolab.com/common/map_user/ys_obc` (note: **sem** `-static`).
Cabeçalhos usados pelo site: `Origin: https://act.hoyolab.com`, `Referer: https://act.hoyolab.com/`,
`x-rpc-map_version: 4.5`, `x-rpc-lrsag:` (vazio), mais os cookies da sessão. Nenhuma assinatura DS.

| Ação | Requisição | Resposta |
|---|---|---|
| Listar marcados | `GET /v1/map/point/mark_map_point_list?map_id=2&app_sn=ys_obc&lang=pt-pt` | `data.list[]` com `point_id`, `map_id`, `id`, `fresh_end_time`, `label_id` |
| Marcar | `POST /v1/map/point/add_mark_map_point` corpo `{"map_id":2,"point_id":88512,"app_sn":"ys_obc","lang":"pt-pt"}` | `{"retcode":0,"data":{"id":47251667}}` |
| Desmarcar | `POST /v1/map/point/new_del_mark_map_point` mesmo corpo | `{"retcode":0,"data":{}}` |

`map_id=2` é Teyvat; o código atual já usa o `MapId` da requisição — reutilizar o mesmo valor.

**Não confirmado:** o HAR veio sem cabeçalho `Cookie` (o navegador remove). Esperado: `ltoken_v2`, `ltuid_v2`,
`ltmid_v2` etc. Por isso a decisão abaixo é guardar **todos** os cookies de `.hoyolab.com`, sem depender de nomes.
O código de "não logado" também não foi capturado (esperado `retcode` -100 ou 10001); tratar qualquer `retcode`
diferente de 0 numa chamada autenticada como erro, e os conhecidos de login como "sessão expirada".

## Abordagens consideradas

- **A) Login oficial em WebView2 (escolhida pelo usuário).** Página oficial, inclui captcha/Google/código por
  e-mail; BetterGI só lê os cookies depois. Mais código, mas seguro e robusto.
- B) Colar cookie manualmente (F12). Simples de implementar, ruim de usar, repetir quando expira.
- C) Usuário/senha no BetterGI chamando `webLoginByPassword`. Descartada: senha é criptografada com RSA,
  tem captcha, e a senha passaria pelo programa.

## Design

### Login

- Novo perfil `WebViewProfiles.HoYoLab` no ambiente `WebViewEnvironments.App`, `ClearsAllData = true`,
  nome/descrição traduzíveis (`I18nService.Key`), incluído em `WebViewProfiles.All` (aparece na janela de armazenamento).
- Nova janela pequena de login (tema WPF-UI, fundo de tema) com um WebView2 nesse perfil, navegando para
  `https://act.hoyolab.com/ys/app/interactive-map/index.html?lang={lang}` (o site abre o login).
- A cada `NavigationCompleted` (e num timer leve, ~1 s, enquanto a janela estiver aberta) ler
  `CookieManager.GetCookiesAsync("https://act.hoyolab.com")`. Quando existir um cookie cujo nome começa com
  `ltoken`, considerar logado: guardar todos os cookies de domínio `hoyolab.com` e fechar a janela.
- Após login, buscar o apelido (`GET https://bbs-api-os.hoyolab.com/community/painter/wapi/user/full`,
  `data.user_info.nickname`) só para exibir "Conectado como X". Se falhar, mostrar só "Conectado".

### Sessão guardada

- Arquivo `User/MapMask/hoyolab-session.bin`: string `nome=valor; ...` criptografada com DPAPI
  (`ProtectedData`, escopo `CurrentUser`). Nunca em texto puro, nunca no `config.json`.
- Serviço novo `HoYoLabAccountService` (singleton): `IsLoggedIn`, `Nickname`, `SaveSession`, `Logout`,
  `GetCookieHeader()`, evento `SessionChanged`. Sair = apagar o arquivo + `CookieManager.DeleteAllCookies()` no perfil.

### Chamadas autenticadas

- Estender `HoYoLabMapApiService` (ou serviço irmão `HoYoLabMarkApiService`, a critério do builder) com
  `GetMarkedPointIdsAsync`, `AddMarkAsync(pointId)`, `DeleteMarkAsync(pointId)`, usando a base sem `-static`,
  os cabeçalhos acima e `Cookie` do `HoYoLabAccountService`.
- Erro de sessão expirada → `Logout` automático + log de aviso traduzível + volta ao modo local.

### Comportamento no minimapa (`MaskWindowViewModel`)

Ativo quando `MapPointApiProvider == HoYoLab` **e** `IsLoggedIn`:

- Ao carregar pontos (`ReplaceMapPoints`) e ao logar/trocar fonte: buscar a lista da conta em segundo plano;
  `_hiddenMapPointIds` passa a ser `{ "HoYoLab:{lang}:{point_id}" }` da conta e reaplica `ApplyMapPointHiddenStates`.
  Enquanto carrega, usar o arquivo local como estado provisório.
- Botão direito: atualiza a tela na hora (otimista) e chama marcar/desmarcar. Se falhar (erro de rede ou
  `retcode` ≠ 0), desfaz e loga. Sem fila offline (decisão nossa: simples; marcar exige conexão quando logado).
- **Arquivo local intocado enquanto logado** (decisão nossa): em modo conta, `SaveHiddenMapPointKeysToStorage`
  não grava. Assim as marcações só-locais sobrevivem até "Enviar locais", e ao sair da conta o usuário volta a ver
  o estado local antigo.
- `point_id` é o mesmo em en-us/pt-pt/es-es (verificado: ponto 88512 idêntico nos dois idiomas). A chave em memória
  continua `HoYoLab:{lang}:{id}` para não mexer no resto do código; trocar de idioma recarrega da conta.
- `HideAllMapPoints` / `ShowAllMapPoints`: **desativados em modo conta** (decisão do usuário). `CanExecute` falso
  e tooltip explicando que, com a conta conectada, a marcação é feita ponto a ponto. Evita apagar os 457 pinos
  com um clique e evita milhares de chamadas.
- Sem login (ou outra fonte): comportamento atual, inalterado.

### Enviar marcações locais

- Botão "Enviar marcações locais para a conta" (visível logado + fonte HoYoLAB): junta os `point_id` de todos os
  arquivos `HoYoLab_*` locais, tira os que a conta já tem, confirma com `ThemedMessageBox` mostrando a quantidade,
  envia em sequência com ~300 ms entre chamadas, e mostra o resultado (enviados/falhas) no log.

### UI

Na linha das opções do mapa (`MaskWindow.xaml`, ao lado do ComboBox de idioma, visível só com fonte HoYoLAB):

```
[HoYoLAB ▾] [Português (pt-pt) ▾] [Entrar na HoYoLAB]
[HoYoLAB ▾] [Português (pt-pt) ▾] Conectado como Fryhh · [⟳] [Enviar locais] [Sair]
```

`⟳` recarrega as marcações da conta (para ver o que foi marcado no celular sem reabrir).
Todos os textos via `{i18n:T}` / `I18nService.Instance.Translate`, chave em chinês, e rodar o sync de i18n.

## Restrições

- Seguir `AGENTS.md`: UTF-8 sem BOM, comentários em todo membro novo, MVVM com `[ObservableProperty]`/`[RelayCommand]`,
  registrar serviços em `App.xaml.cs`, `ThemedMessageBox`, não rodar o programa; validar só com o build.
- Não é possível testar a API sem o usuário: ele testa no jogo e manda prints/log.

## Deixado para o builder

- Estender `HoYoLabMapApiService` ou criar serviço separado.
- Reaproveitar `WebpageWindow` ou criar janela própria de login.
- Se a referência a `ProtectedData` exige pacote NuGet no `net10.0-windows` (adicionar se necessário).
- Detalhes do timer de leitura de cookies e do layout exato dos botões.

## Abordagens para "Ocultar/Mostrar todos" logado

1. Desativar em modo conta — **escolhida** (mais segura).
2. Aplicar na conta com confirmação e progresso — descartada: risco de bloqueio por excesso de chamadas.
3. Só visual, camada extra por cima da conta — descartada: estado misturado confunde.
