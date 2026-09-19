---
id: SPEC-identity-core-0001
title: Liveness sessions, per-tenant face index, biometric decision policy and signed assertions
status: draft
workload: support-library
owner: "@swepay/support-library"
created: 2026-09-18
updated: 2026-09-19
affects_repos: [native-biometrics-backend, native-kyc-backend]
libraries: []
global_standards: [GS-02, GS-05, GS-07, GS-09, GS-13]
tenant_impact: yes
regulated_data: yes
fapi_impact: no
adr_refs: []
---

# SPEC-identity-core-0001: Liveness sessions, per-tenant face index, biometric decision policy and signed assertions

## 1. Contexto & Problema · dono: `po`

Native Biometrics e Native KYC (e, mais adiante, um fluxo de 3DS) precisam das mesmas três
capacidades sobre Amazon Rekognition: (1) rodar uma sessão de prova de vida (face liveness),
(2) indexar/buscar rostos por tenant, e (3) transformar os scores brutos em uma decisão de
negócio (`Verified`/`Retry`/`Rejected`) segundo um limiar configurável. Sem uma lib compartilhada,
cada backend reimplementaria: nomeação de coleção Rekognition (com risco real de colisão/cross
-tenant se feita à mão), o parsing de status de liveness, e — o pior caso — o próprio limiar de
decisão, com potencial de inconsistência regulatória entre produtos. `native-biometrics-backend` e
`native-kyc-backend` existem hoje como repositórios vazios (bootstrap pendente); esta lib nasce
antes deles para que ambos partam consumindo o mesmo contrato desde o primeiro commit.

## 2. Goals & Non-Goals · dono: `po`

**Goals:**
- Abstrair `CreateFaceLivenessSession`/`GetFaceLivenessSessionResults` atrás de
  `ILivenessSessionService`, com `tenantId` explícito em toda chamada.
- Abstrair `IndexFaces`/`SearchFacesByImage`/`DeleteFaces`/`ListFaces` atrás de `IFaceIndex`, com
  nomeação de coleção centralizada (`FaceCollectionNaming`) e sem qualquer caminho para consulta
  cross-tenant.
- Fornecer `BiometricDecision.Evaluate`, um avaliador puro (sem I/O) que qualquer produto pode
  testar e revisar sem tocar Rekognition.
- Fornecer `IAssertionSigner`/`KmsAssertionSigner` para emitir uma assertion assinada (JWS
  ES256/RS256) que uma relying party valide offline, sem reimplementar o encoding JOSE em cada
  backend.

**Non-Goals:**
- Não expõe endpoint HTTP, fila ou handler Lambda — é biblioteca pura, consumida por
  `native-biometrics-backend`/`native-kyc-backend` via `RoutedApiGatewayFunction` deles.
- Não gerencia ciclo de vida de chave KMS (rotação, política) nem de coleção Rekognition além de
  criar/ler/remover — isso é responsabilidade operacional do serviço consumidor.
- Não persiste imagem em nenhum momento (nem em disco, nem em S3, nem em banco) — quem decide se
  e onde armazenar uma imagem de referência é o chamador.
- ~~Não implementa verificação de assertion~~ — **superado em 0.2.0** (2026-09-19):
  `IAssertionVerifier`/`JwksAssertionVerifier` chegaram nesta mesma spec (ver §3/§4/§10) assim
  que duas relying parties concretas (NativeGuard `SPEC-guard-0001` e Passly
  `SPEC-passkey-0002`) confirmaram, de forma independente, o mesmo gap (sem `iss`/`aud` na
  asserção). O verifier não é consumido por elas nesta mudança — ADR-0003 já as levou a manter
  cópias próprias do contrato JWS; fica disponível para quem nascer depois preferindo a lib.

## 3. Design Proposto · dono: `architect`

Quatro módulos independentes dentro de um único pacote (`Native.IdentityCore`), sem dependência
entre si além de `Policy` ser consumido conceitualmente pelo chamador junto com `Liveness`/
`FaceIndex` (a lib não os acopla em código):

```
Native.IdentityCore
├── Liveness/    ILivenessSessionService → RekognitionLivenessSessionService (IAmazonRekognition)
├── FaceIndex/   IFaceIndex → RekognitionFaceIndex (IAmazonRekognition) + FaceCollectionNaming
├── Policy/      BiometricPolicy + BiometricDecision (puro, sem AWS SDK na assinatura)
├── Assertions/  BiometricAssertion + IAssertionSigner → KmsAssertionSigner (IAmazonKeyManagementService)
│               + [0.2.0] IAssertionVerifier → JwksAssertionVerifier (HttpClient, sem AWS)
└── Serialization/ IdentityCoreJsonSerializerContext (source-gen, único ponto de JSON da lib)
                  + [0.2.0] AudienceJsonConverter (claim `aud`: string única ou array, RFC 7519 §4.1.3)
```

**Alternativas descartadas:**
- *Um único `IIdentityService` "canhão" cobrindo liveness+index+decisão+assertion.* Descartado
  porque um consumidor de KYC pode querer só `IFaceIndex` sem liveness (ex.: comparação contra
  documento já capturado por outro meio), e testar `BiometricDecision` isoladamente (sem
  Rekognition) é o cenário de teste mais valioso desta lib.
- *Decisão embutida no `IFaceIndex`/`ILivenessSessionService` (ex.: `SearchAsync` já retorna
  `Verified`/`Rejected`).* Descartado porque o limiar de decisão é por tenant/produto e pode
  mudar sem qualquer alteração no código de acesso ao Rekognition — acoplar os dois obrigaria
  reimplantar a lib inteira para um ajuste de política.
- *Verificação de assertion (`IAssertionVerifier`) nesta mesma spec.* Descartado inicialmente por
  escopo (0.1.0) — **revertido em 0.2.0**: duas relying parties concretas já existiam
  (`native-guard-backend`, `native-passkey-backend`), cada uma com sua própria cópia do
  verificador e do mesmo pedido de `iss`/`aud`; adicionar `IAssertionVerifier` nesta spec (em vez
  de uma spec nova) evita descrever o mesmo contrato JWS duas vezes.

Esta lib não depende do shared kernel de aplicação (`NativeMediator`/`NativeLambdaRouter`/
`Native.FluentValidation`) — `ECOSYSTEM_LIBRARIES.md` já prevê essa isenção para bibliotecas
`native-*` que não são backends de aplicação ("segue as convenções, não depende do kernel de
app"). Nenhum desvio de ADR necessário.

## 4. Contrato de API / Mudanças · dono: `developer`

Superfície pública v0.1.0 (ver `.claude/agents/architect.md` para a lista completa por
namespace). Pontos centrais do contrato:

- `ILivenessSessionService.CreateSessionAsync(tenantId, options, ct)` → `LivenessSessionHandle`.
- `ILivenessSessionService.GetSessionResultAsync(tenantId, sessionId, ct)` → `LivenessSessionResult`
  (`Status`, `Confidence`, `ReferenceImage`, `AuditImages`).
- `IFaceIndex.EnsureCollectionAsync/IndexAsync/SearchAsync/DeleteAsync` — todos com `tenantId`
  como primeiro parâmetro.
- `FaceCollectionNaming.Build(product, environment, tenantId)` /
  `FaceCollectionNaming.TryParse(collectionId, out product, out environment, out tenantId)`.
- `BiometricDecision.Evaluate(policy, livenessConfidence, faceSimilarity, faceMatchFound, attemptNumber)`
  → `BiometricDecisionResult` (puro).
- `IAssertionSigner.SignAsync(BiometricAssertion, ct)` → `string` (JWS compacto).
- **[0.2.0]** `BiometricAssertion` ganha `Issuer` (`iss`) e `Audience` (`aud`, opcional —
  default `[TenantId]` quando ausente/vazio no momento da assinatura) e `CorrelationId` foi
  renomeado para `Jti` (mesmo claim de fio `jti`, sem mudança de wire format).
  `KmsAssertionSignerOptions` ganha `Issuer` (obrigatório, validado como URI absoluta na
  construção do `KmsAssertionSigner` — nunca um domínio fixo no código; vem de configuração do
  serviço consumidor). O assinante, não o chamador, é quem grava `iss` no payload.
- **[0.2.0]** `IAssertionVerifier.VerifyAsync(AssertionVerifierOptions, assertionJws, now, ct)` →
  `AssertionVerificationResult` — verifica JWKS/ES256, `iss`/`aud`/`purpose`/`decision`/
  `exp`/`iat` (com `ClockSkew`/`MaxAssertionAge`) e devolve o `BiometricAssertion` assinado
  (incluindo `Jti`, para o chamador implementar seu próprio guard de replay).

**Breaking changes:**
- `0.1.0` → `0.2.0`: `BiometricAssertion.CorrelationId` renomeado para `Jti`;
  `KmsAssertionSignerOptions` ganhou um terceiro parâmetro obrigatório (`Issuer`). Ambas
  documentadas no `CHANGELOG.md`. Biblioteca ainda pré-1.0 — a partir de `1.0.0`, qualquer
  alteração nas assinaturas passa a exigir major + `PublicAPI.Shipped.txt` (a introduzir em
  `1.0.0`) + seção "Breaking Changes" no `CHANGELOG.md`.

## 5. Modelo de Dados · dono: `developer`

N/A para código de aplicação (esta lib não tem camada de persistência). Único "dado modelado" é a
convenção de nomeação da coleção Rekognition: `swepay-{product}-{environment}-{tenantId}` —
`product`/`environment` casam com `[a-z0-9-]+`, `tenantId` é opaco (pode conter hífens/GUID);
limite de 255 caracteres (limite nativo do Rekognition). Ver `FaceCollectionNaming`.

## 6. Segurança & Compliance · dono: `security`

- **`iss`/`aud` (0.2.0):** `KmsAssertionSignerOptions.Issuer` é obrigatório e validado como URI
  absoluta na construção do signer — nunca um domínio fixo no código-fonte desta lib; é sempre
  configuração do serviço consumidor (variável de ambiente/CloudFormation por ambiente). O
  assinante ignora um `BiometricAssertion.Issuer` porventura preenchido pelo chamador — `iss` só
  pode vir da configuração do signer, nunca de um valor arbitrário do código de aplicação
  (evita "issuer confusion" se o chamador for comprometido ou tiver um bug). `aud` segue o
  default `[TenantId]` quando o chamador não fornece um valor explícito — quem quiser uma
  audiência diferente da própria tenant id (ex.: um id de RP específico) deve passá-la.
- **Replay não é responsabilidade desta lib (0.2.0):** `JwksAssertionVerifier` expõe `Jti` no
  `BiometricAssertion` retornado, mas não guarda estado — cabe ao chamador (ex.: um put
  condicional `attribute_not_exists(PK)` como NativeGuard/Passly já fazem) impedir reuso.
  Documentado no README para não ser assumido implicitamente.
- **Tenant (GS-05):** `tenantId` é parâmetro explícito em toda chamada de `ILivenessSessionService`/
  `IFaceIndex`; a coleção Rekognition é resolvida exclusivamente por `FaceCollectionNaming` — não
  existe caminho de código nesta lib que monte um `CollectionId` sem passar pelo tenant do
  chamador. Isolamento efetivo de acesso cross-tenant depende também de o serviço consumidor
  aplicar IAM/condition keys por coleção (fora do escopo desta lib).
- **LGPD/Bacen (GS-09 — `bacen-lgpd-checklist`):** dado biométrico é dado pessoal sensível (LGPD
  art. 5º, XI). Esta lib:
  - **nunca persiste** a imagem capturada — ela trafega em memória (`ReadOnlyMemory<byte>`) e é
    responsabilidade do chamador decidir se/onde armazenar (retenção é decisão do produto, não
    desta lib);
  - **nunca loga** score de confiança/similaridade, payload de assertion ou bytes de imagem —
    logs carregam apenas `tenantId`/`sessionId`/contagens, para correlação (GS-11) sem expor dado
    sensível;
  - emite `BiometricAssertion` com `TenantId`/`UserRef`/scores/decisão — o produto consumidor é
    quem define retenção e direito de exclusão desse registro (a lib não grava nada por conta
    própria, então "direito ao esquecimento" recai sobre o armazenamento do consumidor).
  - Base legal e retenção do dado biométrico em si (fora desta lib) são responsabilidade do
    produto que a consome (`native-biometrics-backend`/`native-kyc-backend`) — devem preencher o
    checklist completo na spec deles antes de ir a produção.
- **FAPI (GS-08):** não se aplica — esta lib não emite nem valida token OAuth/OIDC.

## 7. Erros — RFC 9457 · dono: `developer`

N/A diretamente (esta lib não expõe HTTP). Exceções tipadas para o chamador mapear:
- `FaceIndexException` — nenhuma face detectada ao indexar (carrega os `Reasons` do Rekognition).
- `NotSupportedException` — status de liveness desconhecido retornado pelo Rekognition (indica
  que a lib precisa de atualização, não um erro do chamador).
- `ArgumentException`/`ArgumentOutOfRangeException`/`ArgumentNullException` — validação de input
  (política inválida, assertion incompleta, segmento de coleção fora do padrão).

O serviço consumidor (`native-biometrics-backend`/`native-kyc-backend`) é responsável por mapear
essas exceções para `SwepayProblemDetails`/RFC 9457 na sua própria camada HTTP — não há stack
trace nem mensagem crua desta lib que deva vazar para uma resposta 5xx sem tradução.

## 8. Estratégia de Teste · dono: `qa`

xUnit + NSubstitute + Shouldly + Bogus, gate 85% line / 70% branch (`coverlet.runsettings`).
Cobertura entregue na primeira versão: 77 testes, ~93,7% line / ~83,3% branch. Com a mudança
0.2.0 (`iss`/`aud`/`Jti` + `IAssertionVerifier`): 108 testes, 92,9% line / 82,05% branch.
Destaques (0.1.0):
- `BiometricDecision` — matriz completa de cenários (verified, retry, rejected por
  `MaxAttempts`, cada `BiometricRejectionReason` isolado/combinado).
- `FaceCollectionNaming` — build válido/inválido, limite de 255 caracteres, round-trip `TryParse`.
- `RekognitionFaceIndex`/`RekognitionLivenessSessionService` — `IAmazonRekognition` mockado via
  NSubstitute; nenhuma chamada AWS real em teste unitário.
- `EcdsaSignatureConverter` — conversão DER→JOSE pura, incluindo padding DER e inteiro maior que
  o campo (erro esperado).
- `KmsAssertionSigner` — `IAmazonKeyManagementService` mockado; verifica ES256 (conversão
  aplicada) e RS256 (assinatura repassada sem alteração).
Destaques (0.2.0): `KmsAssertionSigner`/`BiometricAssertion` — emissão de `iss`/`aud`/`sub`/`jti`,
default de audiência (`[TenantId]`) vs. audiência explícita, `Issuer` inválido (não-URI-absoluta)
rejeitado na construção sem chamar KMS. `JwksAssertionVerifier` — par de chaves ES256 em memória +
`HttpMessageHandler` fake (nenhuma rede real): caminho feliz, assinatura inválida (payload
adulterado), `kid` desconhecido (com refresh forçado), `iss` divergente, `aud` fora da lista
esperada (string única e array), `purpose` divergente, `decision` não `Verified` (`Retry`/
`Rejected`), expirado (com e sem tolerância de `ClockSkew`), `iat` no futuro, mais velho que
`MaxAssertionAge`, JWS malformado (partes erradas, base64 inválido, `alg`≠ES256, `kid` ausente,
claim obrigatório ausente), JWKS indisponível/malformado, cache entre chamadas para a mesma URL.
Smoke pós-deploy (GS-07) não se aplica a esta lib isoladamente — corre no serviço consumidor
contra hml/prd quando `native-biometrics-backend`/`native-kyc-backend` existirem.

## 9. Rollout · dono: `sre`

Biblioteca, não serviço deployado — "rollout" é publicação SemVer:
- `0.1.0`: release inicial via tag `v0.1.0` em `main`, publicado no NuGet (fluxo do
  `.github/workflows/dotnet.yml`, `secrets.NUGET_API_KEY`).
- `0.2.0`: mesmo fluxo, tag `v0.2.0`, após o PR `feat/assertion-iss-aud-verifier` mergear em
  `develop` e depois `main` (GS-01). Breaking changes documentadas no `CHANGELOG.md` (pré-1.0).
- Sem flag de feature — mudança de comportamento (ex.: ajuste de limiar padrão de
  `BiometricPolicy`) é sempre explícita via novo parâmetro nomeado ou novo major.
- Consumo real (`native-biometrics-backend`/`native-kyc-backend`) valida a integração contra
  Rekognition/KMS de verdade em `hml` antes de qualquer produto ir a `prd` — essa validação vive
  na spec do serviço consumidor, não nesta.

## 10. Questões em Aberto · dono: autor

- ~~`IAssertionVerifier` fica para quando houver uma relying party concreta~~ — **resolvido em
  0.2.0**: duas relying parties concretas (NativeGuard, Passly) confirmaram independentemente o
  formato esperado (JWKS, RFC 7517) ao construir suas próprias cópias do verifier antes desta
  lib ter uma; `JwksAssertionVerifier` segue exatamente essa forma (fetch+cache de JWKS, `kid` no
  header, `ECDsa.VerifyData`). Nenhum dos dois repos foi migrado para consumir esta lib — ADR-0003
  já os levou a manter cópias próprias; a decisão de migrar (ou não) é deles, não desta spec.
- **Sem `iss`/`aud` era o gap real, não o formato de verificação.** `SPEC-guard-0001` §10 e
  `SPEC-passkey-0002` §10 apontaram o mesmo problema de forma independente: sem esses dois
  claims, a única amarração real contra "asserção de outro emissor/RP" era a configuração de
  `jwksUrl` por operador + `tenant_id` = id do tenant do consumidor + replay guard por `jti` — o
  que já era suficiente na prática, mas não era uma amarração criptográfica explícita. Resolvido
  nesta versão (`iss`/`aud` agora fazem parte do payload assinado e são validados pelo verifier).
- **`aud` ainda depende de configuração manual por par de produtos.** O default do assinante
  (`[TenantId]`) só funciona se o `tenantId` do produto emissor e o id que o RP usa como
  audiência esperada forem o mesmo valor — mesma pegadinha de mapeamento `tenant_id`↔`realmId`/
  `projectId` já registrada nas specs de NativeGuard/Passly. Quando não forem o mesmo valor, o
  chamador do signer deve passar `Audience` explicitamente; não há descoberta automática.
- `PublicAPI.Shipped.txt` ainda não existe (lib em `0.x`); adicionar antes de `1.0.0`.
- Se `native-biometrics-backend`/`native-kyc-backend` precisarem de suporte a ES384/ES512 além de
  ES256/RS256, `EcdsaSignatureConverter.DerToJose` já aceita `fieldSizeBytes` parametrizável — só
  falta expor o enum `AssertionSigningAlgorithm` correspondente quando o caso de uso aparecer.

---
**Checklist de saída (para `in-review`):** front-matter válido · todas as seções aplicáveis
preenchidas · disparos honrados (tenant/regulated) · desvios de shared kernel com ADR (N/A —
biblioteca `native-*` isenta do kernel de aplicação).
