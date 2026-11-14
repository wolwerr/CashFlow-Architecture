# Diagramas de Sequência

> **Issue #6 · Milestone M2** | Interações da arquitetura proposta

Este documento reúne os **diagramas de sequência**. Eles detalham em que ordem os participantes interagem. Complementam os [Casos de Uso](use-cases.md), mostrando como os serviços interagem. Os cenários de resiliência serão detalhados na Issue #8.

## Autenticação e autorização por comerciante

Fluxo conceitual para autenticação, obtenção do token e verificação da permissão e do comerciante autorizado. Ainda não existe PNG para este novo fluxo.

[Fonte PlantUML](plantuml/sequence-auth.puml)

## Registro de lançamento e publicação assíncrona

A API valida o token, as permissões e o comerciante autorizado antes de gravar o lançamento. O registro é confirmado antes da publicação do evento.

![Registro e publicação assíncrona](images/sequence-register.png)

[Fonte PlantUML](plantuml/sequence-register.puml)

## Falha do consolidado

Mesmo com o consolidado indisponível, o serviço de lançamentos consegue registrar uma movimentação. A entrega pode ser retomada após a recuperação do consumidor.

![Falha do consolidado](images/sequence-failure.png)

[Fonte PlantUML](plantuml/sequence-failure.puml)

## Consulta do consolidado diário

A consulta verifica o JWT e as permissões de leitura e acessa a própria base do consolidado, filtrando por comerciante autorizado. Não precisa consultar o serviço de lançamentos.

![Consulta ao consolidado](images/sequence-query.png)

[Fonte PlantUML](plantuml/sequence-query.puml)

**Atenção:** os três PNGs de sequência ainda correspondem à versão anterior. Renderize novamente os `.puml` e substitua os PNGs em `images/` após revisar o layout. Para a sequência de autenticação, gere `sequence-auth.png` e então podemos adicioná-la visualmente ao Markdown.

**Nota:** os protocolos e as formas de integração são propostas de arquitetura, a confirmar nos ADRs. As regras exatas do cálculo de saldo e o tratamento de eventual atraso continuam pendentes na Issue #3.
