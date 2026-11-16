# Trilha 04 | Hardening do MVP local: evidências de carga

> Status: ferramenta de medição implementada, execução real e homologação pendentes.
> Nenhum teste de carga é executado automaticamente em PR ou contra Azure.

## Escopo deste PR

- Probe Python somente de leitura, sem dependências externas, para `GET /api/v1/consolidation/daily`.
- Usa JWT de usuário autorizado por merchant; não utiliza credencial de serviço nem desabilita autenticação.
- Carga configurável, por padrão **50 RPS por 300 s**, concorrência limitada, timeout de 5 s.
- Registra contagem de HTTP 2xx, demais status, falhas de rede (status 0), requisições não emitidas por saturação e p95 de respostas 2xx.
- Falha se taxa de falhas acima de 5%, p95 de respostas 2xx >= 500 ms ou nenhuma resposta 2xx.
- Testes unitários do probe rodam na CI. Testes reais de performance dependem de aplicação local e credenciais válidas.

## Executar no computador de desenvolvimento

1. Iniciar PostgreSQL, RabbitMQ, Keycloak e serviços com `docker compose up --build -d`.
2. Provisionar um usuário no realm CashFlow e um merchant com associação ADMIN ou OPERATOR válida.
3. Obter um **access token de teste** com audience aceita por Consolidation e validade suficiente para a medição. Nunca versionar, publicar em logs ou incluir o token no relatório.
4. Configurar as variáveis no terminal local e executar:

```bash
export CASHFLOW_LOAD_TOKEN="<access-token-local>"
python3 .github/scripts/local_load_probe.py \
  --merchant-id "00000000-0000-4000-8000-000000000001" \
  --date "2026-11-16" \
  --rate 50 --duration 300 --workers 64
unset CASHFLOW_LOAD_TOKEN
```

O UUID acima é apenas ilustrativo: substitua pelo merchant efetivamente provisionado. O script aceita apenas base URL `localhost`, `127.0.0.1` ou `::1` e não realiza POST. Não executar contra contas reais nem com dados financeiros pessoais. Se a sessão expirar no meio do teste, HTTP 401 será contabilizado como falha, e será necessário repetir com um token de teste válido por todo o intervalo.

Para testar o medidor sem executar carga prolongada:

```bash
python3 -m unittest discover -s .github/scripts -p 'test_local_load_probe.py'
```

## Evidências obrigatórias da execução manual

Registrar junto ao relatório JSON, em uma nota local:

- Commit, data/hora, SO, CPU, RAM e Docker/Compose usados.
- Quantidade de containers/replicas, recursos limitados, volume de dados e configuração do PostgreSQL/RabbitMQ.
- Merchant de teste, perfil de autorização, duração válida do token (sem registrar credenciais).
- Contagem de sucessos, falhas, respostas HTTP, p95, saturação da emissão e eventuais limitações ambientais.
- Conclusão **PASS/FAIL**, baseada nos resultados efetivos, e eventuais mudanças de configuração.

O arquivo `reports/load-local.json` contém endpoint, parâmetros e métricas, nunca o JWT. Evitar versionar relatórios com IDs internos ou detalhes do ambiente; para compartilhar resultados, remover dados sensíveis.

## Critérios ainda não medidos por este probe

- **SLO de projeção:** 95% dos lançamentos visíveis no Consolidation em até 30 s após criação. Exige correlação entre POST, evento/Outbox e GET, com chaves idempotentes novas.
- **Resiliência:** interromper e recuperar RabbitMQ, Consolidation, Keycloak e banco; provar replay sem duplicar saldo, revogação fail-closed e retorno seguro.
- **Auditoria e retenção:** verificar política de 365 dias, trilha de eventos, logs sem tokens nem dados sensíveis.
- **Observabilidade:** logs estruturados, métricas, correlação e alertas demonstrados.
- **Backup/restauração:** restauração local de bancos com evidência de integridade e critérios RPO/RTO aplicáveis.
- **Smoke E2E:** login/logout/refresh, múltiplos merchants e revogação real.
- **Infra cloud:** Azure/AKS, HA, DR e ambientes promovidos permanecem fora de escopo.

Somente após essas execuções e coleta de evidências a Trilha 04 poderá ser marcada como homologada. Um resultado verde da CI prova o funcionamento básico do código do medidor, não prova o SLO.
