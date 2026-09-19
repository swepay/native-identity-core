---
id: SPEC-identity-core-0001
title: Liveness sessions, per-tenant face index, biometric decision policy and signed assertions
status: draft
workload: support-library
owner: "@swepay/support-library"
created: 2026-09-18
updated: 2026-09-18
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
- Não implementa verificação de assertion (`IAssertionSigner` só assina); um `IAssertionVerifier`
  fica para uma spec futura quando houver uma relying party concreta.

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
└── Serialization/ IdentityCoreJsonSerializerContext (source-gen, único ponto de JSON da lib)
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
- *Verificação de assertion (`IAssertionVerifier`) nesta mesma spec.* Descartado por escopo — não
  há ainda uma relying party concreta para validar o formato real esperado; entra em spec própria
  quando `native-biometrics-backend`/`native-kyc-backend` tiverem consumidor definido.

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

**Breaking changes:** N/A (release inicial `0.1.0`). A partir de `1.0.0`, qualquer alteração nas
assinaturas acima passa a exigir major + `PublicAPI.Shipped.txt` (a introduzir em `1.0.0`) +
seção "Breaking Changes" no `CHANGELOG.md`.

## 5. Modelo de Dados · dono: `developer`

N/A para código de aplicação (esta lib não tem camada de persistência). Único "dado modelado" é a
convenção de nomeação da coleção Rekognition: `swepay-{product}-{environment}-{tenantId}` —
`product`/`environment` casam com `[a-z0-9-]+`, `tenantId` é opaco (pode conter hífens/GUID);
limite de 255 caracteres (limite nativo do Rekognition). Ver `FaceCollectionNaming`.

## 6. Segurança & Compliance · dono: `security`

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
Cobertura entregue nesta primeira versão: 77 testes, ~93,7% line / ~83,3% branch. Destaques:
- `BiometricDecision` — matriz completa de cenários (verified, retry, rejected por
  `MaxAttempts`, cada `BiometricRejectionReason` isolado/combinado).
- `FaceCollectionNaming` — build válido/inválido, limite de 255 caracteres, round-trip `TryParse`.
- `RekognitionFaceIndex`/`RekognitionLivenessSessionService` — `IAmazonRekognition` mockado via
  NSubstitute; nenhuma chamada AWS real em teste unitário.
- `EcdsaSignatureConverter` — conversão DER→JOSE pura, incluindo padding DER e inteiro maior que
  o campo (erro esperado).
- `KmsAssertionSigner` — `IAmazonKeyManagementService` mockado; verifica ES256 (conversão
  aplicada) e RS256 (assinatura repassada sem alteração).
Smoke pós-deploy (GS-07) não se aplica a esta lib isoladamente — corre no serviço consumidor
contra hml/prd quando `native-biometrics-backend`/`native-kyc-backend` existirem.

## 9. Rollout · dono: `sre`

Biblioteca, não serviço deployado — "rollout" é publicação SemVer:
- `0.1.0`: release inicial via tag `v0.1.0` em `main`, publicado no NuGet (fluxo do
  `.github/workflows/dotnet.yml`, `secrets.NUGET_API_KEY`).
- Sem flag de feature — mudança de comportamento (ex.: ajuste de limiar padrão de
  `BiometricPolicy`) é sempre explícita via novo parâmetro nomeado ou novo major.
- Consumo real (`native-biometrics-backend`/`native-kyc-backend`) valida a integração contra
  Rekognition/KMS de verdade em `hml` antes de qualquer produto ir a `prd` — essa validação vive
  na spec do serviço consumidor, não nesta.

## 10. Questões em Aberto · dono: autor

- `IAssertionVerifier` (contraparte de `IAssertionSigner`) fica para quando houver uma relying
  party concreta — qual formato de chave pública ela espera (JWKS? KMS `GetPublicKey`?) ainda não
  está definido.
- `PublicAPI.Shipped.txt` ainda não existe (lib em `0.x`); adicionar antes de `1.0.0`.
- Se `native-biometrics-backend`/`native-kyc-backend` precisarem de suporte a ES384/ES512 além de
  ES256/RS256, `EcdsaSignatureConverter.DerToJose` já aceita `fieldSizeBytes` parametrizável — só
  falta expor o enum `AssertionSigningAlgorithm` correspondente quando o caso de uso aparecer.

---
**Checklist de saída (para `in-review`):** front-matter válido · todas as seções aplicáveis
preenchidas · disparos honrados (tenant/regulated) · desvios de shared kernel com ADR (N/A —
biblioteca `native-*` isenta do kernel de aplicação).
