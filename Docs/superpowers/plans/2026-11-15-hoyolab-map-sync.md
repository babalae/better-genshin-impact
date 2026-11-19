# HoYoLAB Map Sync Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Login na HoYoLAB via WebView2 e sincronizar o "desativar ponto" (botão direito) do minimapa com os pinos da conta.

**Architecture:** Três camadas novas, cada uma num arquivo: cliente HTTP autenticado (`HoYoLabMarkApiService`), sessão criptografada (`HoYoLabAccountService` + `HoYoLabSessionStore`), janela de login WebView2 (`HoYoLabLoginWindow`). `MaskWindowViewModel` passa a usar a conta como fonte das marcações quando fonte = HoYoLab e logado; sem login nada muda.

**Tech Stack:** .NET 10 WPF, WPF-UI 4, CommunityToolkit.Mvvm, WebView2, Newtonsoft.Json, DPAPI (`System.Security.Cryptography.ProtectedData`), xUnit.

**Spec:** `docs/superpowers/specs/2026-11-15-hoyolab-map-sync-design.md`

## Global Constraints

- `AGENTS.md`: C# em UTF-8 sem BOM; comentário (chinês) em todo tipo/membro novo; `_camelCase` privado; `?` em anuláveis; sem caminho completo de classe; não criar `BetterGenshinImpact/Assets`.
- Não executar o programa principal. Validação = `dotnet msbuild BetterGenshinImpact.sln -restore -p:Configuration=Debug -nologo -tl:off -clp:ErrorsOnly` (na raiz). Se falhar por arquivo travado pelo BetterGI aberto, parar e avisar.
- Testes: `dotnet test Test/BetterGenshinImpact.UnitTest/BetterGenshinImpact.UnitTest.csproj --filter <Classe> -nologo`.
- Base autenticada: `https://sg-public-api.hoyolab.com/common/map_user/ys_obc` (sem `-static`).
- Cabeçalhos: `Origin: https://act.hoyolab.com`, `Referer: https://act.hoyolab.com/`, `x-rpc-map_version: 4.5`, `x-rpc-lrsag:` vazio, `Cookie`.
- Corpo POST: `{"map_id":2,"point_id":<id>,"app_sn":"ys_obc","lang":"<lang>"}`.
- Cookies só para `*.hoyolab.com`; nunca logar valor de cookie; nunca pedir/guardar senha.
- Textos de UI/log novos com chave em chinês (`{i18n:T}`, `I18nService.Instance.Translate`, `I18nService.Key`); no fim rodar `.\Build\Scripts\i18n-sync.ps1` e preencher pt-BR e en.
- Commits em chinês terminando com `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

## Review Focus

1. Sessão expirada no meio do jogo (`retcode` -100 ou 10001) → logout automático, aviso no log, volta ao modo local sem perder o arquivo local. Teste em Task 1 (`ThrowsSessionExpired`) e lógica em Task 4.
2. Botão direito sem rede/erro de API → ponto volta ao estado anterior; arquivo local não é escrito em modo conta. Coberto por `MapMarkSyncState` em Task 4.
3. Cookie header nunca contém cookies de outro domínio e é nulo se não existe `ltoken*`. Teste em Task 2.
4. Arquivo de sessão corrompido/de outro usuário Windows → tratado como deslogado, sem crash. Teste em Task 2 (`Load_CorruptFile_ReturnsNull`).
5. "Enviar locais" com chaves malformadas ou de outras fontes → ignoradas. Teste em Task 4 (`ParseLocalPointIds`).

---

### Task 1: Cliente autenticado dos pinos

**Files:**
- Create: `BetterGenshinImpact/Service/HoYoLab/HoYoLabMarkApiService.cs`
- Create: `BetterGenshinImpact/Service/HoYoLab/HoYoLabSessionExpiredException.cs`
- Test: `Test/BetterGenshinImpact.UnitTest/ServiceTests/HoYoLabMarkApiServiceTests.cs`

**Interfaces:**
- Produces:
  - `HoYoLabMarkApiService(HttpClient httpClient)` (DI: construtor público adicional `HoYoLabMarkApiService(IHttpClientFactory f)` usando `HttpClientNames.GameApi`).
  - `Task<IReadOnlySet<long>> GetMarkedPointIdsAsync(string cookieHeader, int mapId, string lang, CancellationToken ct)`
  - `Task AddMarkAsync(string cookieHeader, int mapId, long pointId, string lang, CancellationToken ct)`
  - `Task DeleteMarkAsync(string cookieHeader, int mapId, long pointId, string lang, CancellationToken ct)`
  - `HoYoLabSessionExpiredException : Exception`; `HoYoLabApiException(int retcode, string message) : Exception` (mesmo arquivo da expired).
  - Endpoints: `GET /v1/map/point/mark_map_point_list?map_id=&app_sn=ys_obc&lang=`, `POST /v1/map/point/add_mark_map_point`, `POST /v1/map/point/new_del_mark_map_point`.

- [ ] **Step 1: Testes que falham** — stub `HttpMessageHandler` (padrão de `HttpSpeechProviderTests.Handler`):
  - `GetMarked_ParsesPointIds`: resposta `{"retcode":0,"data":{"list":[{"point_id":323},{"point_id":88512}]}}` → conjunto `{323, 88512}`; assert URL contém `mark_map_point_list?map_id=2&app_sn=ys_obc&lang=pt-pt`, host `sg-public-api.hoyolab.com`, cabeçalho `Cookie == "ltoken_v2=a; ltuid_v2=1"`, `x-rpc-map_version == "4.5"`, `Referer == "https://act.hoyolab.com/"`.
  - `AddMark_PostsExpectedBody`: assert método POST, path termina em `add_mark_map_point`, corpo JSON igual a `{"map_id":2,"point_id":88512,"app_sn":"ys_obc","lang":"pt-pt"}` (comparar via `JObject.DeepEquals`).
  - `DeleteMark_UsesNewDelEndpoint`: path termina em `new_del_mark_map_point`.
  - `ThrowsSessionExpired` (Theory -100, 10001): `{"retcode":<n>,"message":"x"}` → `HoYoLabSessionExpiredException`.
  - `ThrowsApiException_OnOtherRetcode`: retcode -1 → `HoYoLabApiException` com `Retcode == -1`.
- [ ] **Step 2:** Rodar `--filter HoYoLabMarkApiServiceTests` → FAIL (tipo não existe).
- [ ] **Step 3:** Implementar. Um método privado `SendAsync(HttpRequestMessage, ct) -> JObject` que aplica cabeçalhos, `EnsureSuccessStatusCode`, lê `retcode` e lança as exceções.
- [ ] **Step 4:** Rodar de novo → PASS.
- [ ] **Step 5:** Commit `HoYoLAB 地图：新增标记点接口客户端`.

### Task 2: Sessão criptografada e serviço de conta

**Files:**
- Create: `BetterGenshinImpact/Service/HoYoLab/HoYoLabSessionStore.cs`
- Create: `BetterGenshinImpact/Service/HoYoLab/HoYoLabAccountService.cs`
- Modify: `BetterGenshinImpact/App.xaml.cs:~502` (registrar `HoYoLabMarkApiService` e `HoYoLabAccountService` como singleton)
- Modify: `BetterGenshinImpact/BetterGenshinImpact.csproj` só se `ProtectedData` não compilar sem pacote (então `System.Security.Cryptography.ProtectedData`).
- Test: `Test/BetterGenshinImpact.UnitTest/ServiceTests/HoYoLabSessionTests.cs`

**Interfaces:**
- Consumes: nada.
- Produces:
  - `record HoYoLabCookie(string Name, string Value, string Domain)`
  - `static string? HoYoLabSessionStore.BuildCookieHeader(IEnumerable<HoYoLabCookie> cookies)` — mantém só `Domain` terminando em `hoyolab.com` (ignorando `.` inicial), formato `nome=valor; nome=valor`, retorna null se nenhum nome começa com `ltoken`.
  - `HoYoLabSessionStore(string filePath)`; `void Save(string cookieHeader)`; `string? Load()`; `void Delete()`. DPAPI `DataProtectionScope.CurrentUser`, UTF-8. Caminho padrão: `Global.Absolute(@"User/MapMask/hoyolab-session.bin")` (usar o helper de caminho que `MapMaskStateStorage` usa).
  - `HoYoLabAccountService(HoYoLabSessionStore store)`: `bool IsLoggedIn`, `string? Nickname`, `string? CookieHeader`, `void SetSession(string cookieHeader, string? nickname)`, `void Logout()`, `event EventHandler? SessionChanged`. Apelido guardado junto (formato do arquivo: JSON `{"cookie":"...","nickname":"..."}` antes de criptografar).
- [ ] **Step 1: Testes que falham**
  - `BuildCookieHeader_FiltersDomains`: cookies `ltoken_v2@.hoyolab.com`, `ltuid_v2@hoyolab.com`, `x@google.com` → `"ltoken_v2=a; ltuid_v2=1"`.
  - `BuildCookieHeader_NoLtoken_ReturnsNull`.
  - `Store_RoundTrip`: arquivo temp; Save/Load iguais; bytes do arquivo não contêm a string `ltoken`.
  - `Load_CorruptFile_ReturnsNull`: arquivo com bytes aleatórios → null, sem exceção.
  - `Account_Logout_DeletesFileAndRaisesEvent`.
- [ ] **Step 2:** Rodar `--filter HoYoLabSessionTests` → FAIL.
- [ ] **Step 3:** Implementar.
- [ ] **Step 4:** Rodar → PASS; build da solução → exit 0.
- [ ] **Step 5:** Commit `HoYoLAB 地图：登录会话加密保存`.

### Task 3: Perfil WebView2 e janela de login

**Files:**
- Modify: `BetterGenshinImpact/Infrastructure/WebView/WebViewProfile.cs` (novo `WebViewProfiles.HoYoLab`, adicionado em `All`)
- Create: `BetterGenshinImpact/View/Windows/HoYoLabLoginWindow.xaml` + `.xaml.cs`

**Interfaces:**
- Consumes: `HoYoLabSessionStore.BuildCookieHeader`, `HoYoLabAccountService.SetSession`.
- Produces: `static Task<bool> HoYoLabLoginWindow.ShowLoginAsync(Window? owner, string lang)` — true se logou. `static Task ClearCookiesAsync()` para o logout limpar o perfil (abre ambiente/perfil sem janela; se complexo, pode ser feito na próxima vez que a janela abrir — registrar a escolha em comentário).
- Perfil: `new(WebViewEnvironments.App, "HoYoLab", I18nService.Key("软件内网页 · HoYoLAB 登录"))`, `Description = I18nService.Key("HoYoLAB 地图登录状态（Cookie）")`, `ClearsAllData = true`.

- [ ] **Step 1:** Perfil novo. Janela `ui:FluentWindow` com fundo de tema (copiar padrão de `JsScriptHtmlSettingsWindow`: `WebView.InitializeAsync(provider, WebViewProfiles.HoYoLab, new WebViewOptions{...})`, `IsPasswordAutosaveEnabled=false`), navegar `https://act.hoyolab.com/ys/app/interactive-map/index.html?lang={lang}`; sem filtro de requisições (o login usa vários domínios).
- [ ] **Step 2:** Detecção: em `NavigationCompleted` e num `DispatcherTimer` de 1 s, `core.CookieManager.GetCookiesAsync("https://act.hoyolab.com")` → mapear para `HoYoLabCookie` → `BuildCookieHeader`; se não-nulo, parar timer, buscar apelido (`GET https://bbs-api-os.hoyolab.com/community/painter/wapi/user/full`, `data.user_info.nickname`, com o cookie; falha → null), `SetSession`, fechar com resultado true. Se `WebViewRuntime.IsAvailable` falso → mensagem `ThemedMessageBox` "未安装 Microsoft Edge WebView2 运行时".
- [ ] **Step 3:** Build da solução → exit 0. (Sem teste unitário: UI; a lógica testável já está em Task 2.)
- [ ] **Step 4:** Commit `HoYoLAB 地图：WebView2 登录窗口`.

### Task 4: Sincronização no minimapa

**Files:**
- Create: `BetterGenshinImpact/GameTask/MapMask/MapMarkSync.cs` (lógica pura)
- Modify: `BetterGenshinImpact/ViewModel/MaskWindowViewModel.cs` (construtor `:226`, `ToggleMapPointHidden :1202`, `HideAll/ShowAll :1221-1252`, `ReplaceMapPoints :1254`, `SaveHiddenMapPointKeysToStorage :1325`)
- Modify: `BetterGenshinImpact/View/MaskWindow.xaml:~1133-1180`
- Test: `Test/BetterGenshinImpact.UnitTest/GameTaskTests/MapMaskTests/MapMarkSyncTests.cs`

**Interfaces:**
- Consumes: Task 1 (`GetMarkedPointIdsAsync`, `AddMarkAsync`, `DeleteMarkAsync`, exceções), Task 2 (`HoYoLabAccountService`), Task 3 (`ShowLoginAsync`).
- Produces (em `MapMarkSync`, estático):
  - `IEnumerable<long> ParseLocalPointIds(IEnumerable<string> keys)` — aceita só `HoYoLab:{lang}:{id}` com id `long`.
  - `IReadOnlyList<long> PendingUpload(IEnumerable<long> local, IReadOnlySet<long> account)` — distintos, ordenados, sem os da conta.
  - `string ToKey(string lang, long pointId)` → `HoYoLab:{lang}:{id}` (mesmo formato de `GetMapPointKey`).
- VM: `[ObservableProperty] bool IsHoYoLabAccountMode` (fonte HoYoLab && logado), `string HoYoLabAccountText` ("已登录：{0}" / "已登录"), comandos `LoginHoYoLabCommand`, `LogoutHoYoLabCommand`, `RefreshHoYoLabMarksCommand`, `UploadLocalMarksCommand`; `HideAllMapPoints`/`ShowAllMapPoints` com `CanExecute = !IsHoYoLabAccountMode`.

- [ ] **Step 1: Testes que falham**
  - `ParseLocalPointIds_IgnoresOtherSourcesAndMalformed`: `["HoYoLab:pt-pt:88512","HoYoLab:en-us:323","MihoyoMap:5","HoYoLab:pt-pt:abc","x"]` → `[88512, 323]`.
  - `PendingUpload_RemovesAccountAndDuplicates`: local `[5,3,3,9]`, conta `{9}` → `[3,5]`.
  - `ToKey_Format`: `ToKey("pt-pt",1)=="HoYoLab:pt-pt:1"`.
- [ ] **Step 2:** `--filter MapMarkSyncTests` → FAIL; implementar `MapMarkSync`; → PASS.
- [ ] **Step 3: VM — carregar da conta.** Injetar `HoYoLabMarkApiService` e `HoYoLabAccountService`. Após `ReplaceMapPoints` (estado local provisório) e em `SessionChanged`/troca de fonte ou idioma: se `IsHoYoLabAccountMode`, `GetMarkedPointIdsAsync` em segundo plano → na UI, `_hiddenMapPointIds` = `ToKey(lang,id)` de cada id → `ApplyMapPointHiddenStates()` + `SyncPointInfoPopupHiddenState()`. Usar um contador de geração para descartar respostas antigas se a fonte/idioma mudar no meio. `mapId` = mesmo valor usado no `PointListRequest` atual.
- [ ] **Step 4: VM — botão direito.** Em modo conta, `ToggleMapPointHidden` alterna na tela (otimista) e dispara Add/Delete; em erro reverte o ponto e loga `"HoYoLAB 标记同步失败：{Message}"`; em `HoYoLabSessionExpiredException` → `Logout()` + log `"HoYoLAB 登录已过期，已切换为本地标记"` + recarregar estado local. `SaveHiddenMapPointKeysToStorage` retorna sem gravar quando `IsHoYoLabAccountMode`. `long.TryParse(point.Id)` falho → ignora com log de debug.
- [ ] **Step 5: VM — enviar locais.** Ler `MapMaskStateStorage.Read($"HoYoLab_{lang}").HiddenMapPointKeys` para `en-us`, `pt-pt`, `es-es` → `ParseLocalPointIds` → `PendingUpload` contra o conjunto da conta; zero → log "没有需要上传的本地标记"; senão `ThemedMessageBox` confirmando "将上传 {0} 个本地标记到 HoYoLAB 账号，是否继续？"; envio sequencial com `Task.Delay(300)`; ao fim log "已上传 {0} 个，失败 {1} 个" e recarregar da conta.
- [ ] **Step 6: XAML.** Na linha do ComboBox de idioma (visível com `IsHoYoLabProviderSelected`): deslogado → botão `{i18n:T 登录 HoYoLAB}`; logado → `TextBlock` `HoYoLabAccountText` (`Mode=OneWay`), botões ícone ⟳ (`{i18n:T 刷新账号标记}`), `{i18n:T 上传本地标记}`, `{i18n:T 退出登录}`. Botões Ocultar/Mostrar todos ganham tooltip quando desativados: `{i18n:T 已登录 HoYoLAB，请逐个标记点位}` (`ToolTipService.ShowOnDisabled=True`). Logout chama `HoYoLabLoginWindow.ClearCookiesAsync()` + `Logout()` + recarrega estado local.
- [ ] **Step 7:** Build da solução → exit 0; testes do filtro `HoYoLab|MapMarkSync` → PASS.
- [ ] **Step 8:** Commit `HoYoLAB 地图：右键标记与账号同步`.

### Task 5: Traduções

**Files:** `User/I18n/*.json` (via ferramenta).

- [ ] **Step 1:** `.\Build\Scripts\i18n-sync.ps1 -AddOnly`; preencher `pt-BR` e `en` para as chaves novas (ex.: 登录 HoYoLAB = "Entrar na HoYoLAB" / "Sign in to HoYoLAB"; 退出登录 = "Sair" / "Sign out"; 已登录：{0} = "Conectado como {0}" / "Signed in as {0}"; 上传本地标记 = "Enviar marcações locais" / "Upload local marks"; 刷新账号标记 = "Atualizar marcações da conta" / "Refresh account marks").
- [ ] **Step 2:** `.\Build\Scripts\i18n-sync.ps1 -AddOnly -Check` → sem pendências.
- [ ] **Step 3:** Commit `HoYoLAB 地图：新增文案翻译`.

### Task 6: Teste no jogo (usuário)

- [ ] Usuário: entrar na conta → minimapa mostra os 457 pontos desativados; botão direito num ponto → aparece no site; desmarcar no site → ⟳ → ponto volta; sair → volta ao estado local.
