# NGINX como reverse proxy do CashFlow MVP

A [ADR-007](../../docs/architecture/adr/ADR-007-clients-api-gateway.md) define NGINX **como parte do MVP**, para servir a aplicação React e encaminhar requisições para Transactions e Consolidation. Um API Gateway dedicado e o BFF continuam sendo evoluções futuras.

## Arquivo

- [default.conf.template](default.conf.template): configuração inicial para a imagem oficial `nginx`. O entrypoint padrão expande variáveis de ambiente de arquivos em `/etc/nginx/templates/*.template`.

## Integração prevista

1. Compilar o React e disponibilizar o diretório de saída em `/usr/share/nginx/html` dentro do container NGINX.
2. Montar `default.conf.template` em `/etc/nginx/templates/default.conf.template`.
3. Definir `TRANSACTIONS_UPSTREAM=transactions:8080` e `CONSOLIDATION_UPSTREAM=consolidation:8080` (nomes/portas ilustrativos, não verificados).
4. Disponibilizar NGINX na porta interna `8080`, mapeando a porta externa de acordo com Docker Compose ou infraestrutura.
5. Validar `nginx -t` e testar `/nginx-health`, chamadas às duas rotas de API e deep links da SPA.
6. Ajustar os prefixos `/api/transactions/` e `/api/consolidation/` aos contratos HTTP reais antes de integrar. O template **preserva o prefixo** em vez de reescrever os caminhos.

## Segurança e ambiente

- Em produção, HTTPS e terminação TLS devem ser definidos no ambiente de implantação; esta configuração inicial expõe **HTTP interno** na porta 8080.
- As duas APIs .NET precisam validar access tokens JWT, emissor, audiência, assinatura, expiração e autorização por `merchant_id`. NGINX **não substitui esses controles**.
- Não expor as APIs internas publicamente, exceto quando testes controlados exigirem acesso.
- Keycloak realiza autenticação OIDC em fluxo separado. URLs públicas, callbacks e configuração do React serão definidos quando o frontend existir.
- `X-Forwarded-*` deve ser interpretado pelas APIs somente com redes/proxies confiáveis configurados. Evitar confiar cegamente em cabeçalhos externos.
- Este repositório contém principalmente documentação de arquitetura. **O template não representa uma implantação executada ou validada**: faltam os containers reais de React e .NET, as rotas finais e um arquivo de orquestração integrando os serviços.

## Testes de integração a implementar

- React servido pelo NGINX, incluindo navegação SPA com refresh.
- Proxy de registros e de consultas com preservação do cabeçalho Authorization.
- JWT inválido retorna 401 e falta de autorização retorna 403 na API, não somente no proxy.
- Falha de Consolidation não impede requisições de registro ao Transactions por meio do NGINX.
- Teste de carga de 50 RPS na rota de consulta através do NGINX e registro das latências/erros.

O arquivo é uma **base técnica inicial**, não uma configuração revisada de produção.
