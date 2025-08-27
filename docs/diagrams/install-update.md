# Instalação e Atualização

Fluxo de instalacao, provisionamento do banco e atualizacao.

```mermaid
graph TD;
  %% Rider-safe: ASCII labels, no special shapes, one command per line, all ASCII
  A[Pre-requisitos] --> B[DotNet 8 and IDE];
  B --> C[Gerar certificados pfx dev prod];
  C --> D[Configurar appsettings e user-secrets];
  %% API Database ConnectionString HTTPS cert
  D --> E[Iniciar API];
  %% WebSite HTTPS cert
  D --> F[Subir WebSite];
  %% GUI Server URL user-secrets
  D --> G[Executar GUI];

  subgraph API_Server [Servidor API]
    E;
    CC[ConsoleClient local];
  end

  subgraph DB_Provisioning [Provisionamento do Banco]
    CC --> I[Database vazio];
    I -- Sim --> J[database init 1 N Structure Data sql];
    I -- Nao --> K[database update versao atual alvo];
    J --> L[settings db_version N];
    K --> L;
    L --> M[Operacional];
  end

  A --> CC;
  M --> E;
  M --> F;
  M --> G;

  subgraph Backup_Restore [Backup Restore]
    N[CC backup backups backup_sql_enc];
    O[CC restore backup_sql_enc];
  end
  CC --> N;
  CC --> O;

  subgraph Plugins [Plugins]
    P[Copiar Plugin dll para API Plugins dir];
    Q[Chamar Plugins reload];
    R[Habilitar Plugins enable name];
  end

  E --> P;
  P --> Q;
  Q --> R;
````
