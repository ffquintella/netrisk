# Deploy

Diagrama de implantação e comunicação entre componentes.

```mermaid
flowchart LR

  %% Agrupamentos
  subgraph UserEnv[Estações Usuários]
    Desktop[GUIClient]
    Browser[Browser -> WebSite]
  end

  subgraph NetRisk_API[Servidor API]
    API[API Kestrel HTTPS]
    AdminCLI[ConsoleClient]
    PluginsDir[API Plugins]
    CertAPI[Cert PFX]
    AppCfgAPI[AppSettings Secrets]
  end

  subgraph NetRisk_Web[Servidor Web]
    SITE[WebSite Kestrel HTTPS]
    CertWEB[Cert PFX]
    AppCfgWEB[AppSettings Secrets]
  end

  subgraph Jobs[Background Jobs]
    Hangfire[Hangfire Server]
  end

  DB[MySQL]

  %% Fluxos externos
  Desktop -->|HTTPS JWT FaceID opcional| API
  Browser -->|HTTPS| SITE
  SITE -->|HTTP REST interno| API

  %% CLI agora local ao servidor API
  AdminCLI --> API

  %% Acesso a dados
  API -->|EF Core| DB
  Hangfire -->|EF Core| DB

  %% Recursos locais
  API --- PluginsDir
  API --- CertAPI
  SITE --- CertWEB
  API --- AppCfgAPI
  SITE --- AppCfgWEB
```

Notas:
- ConsoleClient (AdminCLI) movido para o servidor de API (deploy lado a lado com a API).
- Fluxo direto anterior AdminCLI -> DB substituído por AdminCLI -> API (operações passam pela API).
