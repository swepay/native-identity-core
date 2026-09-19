# Política de Segurança

Este repositório processa (em memória, sem persistência) dados biométricos e materiais de
assinatura criptográfica. Trate qualquer vulnerabilidade aqui com a mesma prioridade de um
componente regulado.

## Versões Suportadas

A versão mais recente publicada recebe correções de segurança. Versões anteriores recebem
patches apenas em casos de vulnerabilidade crítica avaliada caso a caso.

| Versão | Status |
| ------ | ------ |
| latest | :white_check_mark: |
| anteriores | :warning: avaliado caso a caso |

## Reportando uma Vulnerabilidade

**Não abra issues públicas para vulnerabilidades de segurança.**

Envie um e-mail para **security@swepay.com.br** com:

- Descrição clara da vulnerabilidade
- Passos reproduzíveis (PoC quando possível)
- Impacto estimado (severidade, superfície afetada — ex.: bypass de decisão biométrica,
  falsificação de assertion, vazamento de imagem)
- Versão ou commit afetado
- Sugestão de mitigação, se houver

Alternativa: [GitHub Security Advisory privado](https://docs.github.com/code-security/security-advisories/guidance-on-reporting-and-writing/privately-reporting-a-security-vulnerability) neste repositório.

## Compromisso de Resposta

- **Acknowledgment:** até 5 dias úteis da recepção do relatório.
- **Triage e plano de correção:** até 15 dias úteis.
- **Disclosure coordenado:** 90 dias entre notificação e divulgação pública, negociável conforme
  severidade e complexidade.

Créditos públicos ao pesquisador podem ser dados após o disclosure, se desejado.

## Escopo

Esta política cobre o código-fonte deste repositório e o artefato NuGet publicado a partir dele.
Infraestrutura operacional (contas AWS, chaves KMS reais, coleções Rekognition de produção) e
produtos consumidores (`native-biometrics-backend`, `native-kyc-backend`) estão fora de escopo
aqui — reporte-os no repositório correspondente.

## Fora de Escopo

- Denial of Service via esgotamento de recursos em ambientes de desenvolvimento local.
- Vulnerabilidades em dependências transitivas já reportadas em CVE database e com patch
  disponível — prefira contribuir com PR de upgrade.
- Engenharia social contra mantenedores.
