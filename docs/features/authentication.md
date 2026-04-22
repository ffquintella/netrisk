# Authentication

Covers user accounts, roles, fine-grained permissions, MFA, FaceID biometric auth, login audit, and the new-user registration/approval workflow.

## Key Model Classes

- [User.cs](../../src/DAL/Entities/User.cs)
- [Role.cs](../../src/DAL/Entities/Role.cs)
- [Permission.cs](../../src/DAL/Entities/Permission.cs)
- [UserMfa.cs](../../src/DAL/Entities/UserMfa.cs)
- [FaceIDUser.cs](../../src/DAL/Entities/FaceIDUser.cs), [BiometricTransaction.cs](../../src/DAL/Entities/BiometricTransaction.cs)
- [FaceData.cs](../../src/Model/FaceID/FaceData.cs), [FaceToken.cs](../../src/Model/FaceID/FaceToken.cs), [FaceTransactionData.cs](../../src/Model/FaceID/FaceTransactionData.cs)
- [ClientRegistration.cs](../../src/DAL/Entities/ClientRegistration.cs)
- [AuthenticationMethod.cs](../../src/Model/Authentication/AuthenticationMethod.cs), [AuthenticationType.cs](../../src/Model/Authentication/AuthenticationType.cs)

## Server Services

- [`IUsersService`](../../src/ServerServices/Interfaces/IUsersService.cs) — `GetUser`/`Async`, `GetUserById`, `FindEnabledActiveUser*`, `GetAllAsync`, `GetByTeamIdAsync`, `ListActiveUsersAsync`, `VerifyPassword`, `ChangePassword`, `CheckPasswordComplexity`, `SaveUser`, `CreateUser`, `DeleteUser`, `RegisterLoginAsync`
- [`IRolesService`](../../src/ServerServices/Interfaces/IRolesService.cs) — role CRUD, `GetRolePermissions`, `UpdatePermissions`
- [`IPermissionsService`](../../src/ServerServices/Interfaces/IPermissionsService.cs) — `UserHasPermission`, `GetUserPermissions*`, `SaveUserPermissionsById*`, `GetAllPermissions`, `GetDefaultPermissions`, `GetByKey`
- [`IFaceIDService`](../../src/ServerServices/Interfaces/IFaceIDService.cs) — enrollment (`SaveFaceIdAsync`), transaction lifecycle (`StartTransactionAsync`, `CommitTransactionAsync`, `CleanUpExpiredTransactionsAsync`), `FaceTokenIsValidAsync`, `ValidateTokenAndLocateTransaction`, `GetUserOpenTransactionsAsync`, plugin-gated via `IsFaceIDPluginEnabled`

## API

- [`AuthenticationController`](../../src/API/Controllers/AuthenticationController.cs) — `Login`, `Logout`, `RefreshToken`, `GetCurrentUser`
- [`UsersController`](../../src/API/Controllers/UsersController.cs) — CRUD + `Active`, `{id}/Permissions`, `{id}/Roles`, `{id}/Password`
- [`RolesController`](../../src/API/Controllers/RolesController.cs) — role CRUD + `{id}/Permissions`
- [`FaceIDController`](../../src/API/Controllers/FaceIDController.cs) — `Info`, `UserEnabled`, `PluginEnabled`, `UserHasFaceSet`, `SaveFace`, `StartTransaction`, `CommitTransaction`, `ValidateToken`, `OpenTransactions`
- [`RegistrationController`](../../src/API/Controllers/RegistrationController.cs) — `RequestAccess`, `Status/{requestId}`, `ApproveRequest`, `RejectRequest`

## Authorization Policies

- `RequireValidUser` — authenticated user
- `RequireRiskmanagement` — risk module role
- `PermissionAuthorize` — attribute-based fine-grained permission check (see `ValidUserRequirementHandler`)

## Client

- [`AuthenticationRestService`](../../src/ClientServices/Services/AuthenticationRestService.cs)
- [`UsersRestService`](../../src/ClientServices/Services/UsersRestService.cs)
- [`RolesRestService`](../../src/ClientServices/Services/RolesRestService.cs)
- [`FaceIDRestService`](../../src/ClientServices/Services/FaceIDRestService.cs)
- [`RegistrationService`](../../src/ClientServices/Services/RegistrationService.cs)

## Capabilities

- Role + direct-permission hybrid authorization
- Password complexity enforcement
- Login audit trail (`RegisterLoginAsync`)
- MFA-capable user records
- FaceID as a plugin-provided authentication method with short-lived transaction tokens
- Registration approval workflow (request → admin approve/reject → account enabled)

## Tests

- `UsersServiceTest` (ServerServices.Tests)
- `ClientRegistrationServiceTest` (ServerServices.Tests)
- `ValidUserRequirementHandlerTest` (API.Tests)
- Shared mocks: `MockedUsersService` etc.

## Common Exceptions

`UserNotAuthorizedException`, `UserNotFoundException`, `PermissionInvalidException`, `RoleNotFoundException`
