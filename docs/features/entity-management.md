# Entity Management

Schema-driven model for organizational "entities" (business units, departments, systems, suppliers, etc.) used as a scoping dimension for risks, incidents, and response-plan task assignments. Entity types and their properties are defined via configuration rather than hardcoded.

## Key Model Classes

- [Entity.cs](../../src/DAL/Entities/Entity.cs) — entity instance
- [EntitiesProperty.cs](../../src/DAL/Entities/EntitiesProperty.cs) — dynamic property value
- [EntityDefinition.cs](../../src/Model/Entities/EntityDefinition.cs) — schema
- [EntityType.cs](../../src/Model/Entities/EntityType.cs)
- [EntityDto.cs](../../src/Model/Entities/EntityDto.cs), [EntitiesPropertyDto.cs](../../src/Model/Entities/EntitiesPropertyDto.cs)

## Server Service

[`IEntitiesService`](../../src/ServerServices/Interfaces/IEntitiesService.cs):

- Schema: `GetEntitiesConfigurationAsync` (loads entity definitions from disk)
- Validation: `ValidatePropertyList`
- Instances: `CreateInstance`, `GetEntities`, `GetEntity`, `UpdateEntity`, `DeleteEntity`
- Properties: `CreateProperty`, `UpdateProperty`, `UpdateEntitiesProperty`, `DeleteEntitiesProperty`, `TryDeleteEntitiesProperty`

## API

[`EntitiesController`](../../src/API/Controllers/EntitiesController.cs):

| Verb | Route | Purpose |
|------|-------|---------|
| GET | `/entities` | List |
| GET | `/entities/{id}` | Get one |
| POST | `/entities` | Create |
| PUT | `/entities/{id}` | Update |
| DELETE | `/entities/{id}` | Delete |
| GET | `/entities/Definitions` | Schema (entity type definitions) |
| GET/POST/PUT/DELETE | `/entities/{id}/Properties` | Manage dynamic properties |

## Client

[`EntitiesRestService`](../../src/ClientServices/Services/EntitiesRestService.cs).

## Capabilities

- Configuration-driven entity types (no code change to add a new type)
- Dynamic typed properties validated against the schema
- Hierarchical parent/child relationships
- Used as scoping dimension across Risks and IRP task assignments

## Tests

No dedicated test file was found in the current suite.

## Common Exceptions

`EntityDefinitionNotFoundException`, `InvalidParameterException`, `DataNotFoundException`
