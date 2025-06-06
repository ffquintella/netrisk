namespace DAL.Enums;

public enum TransactionResult
{
    Success = 0,
    Failed = 1,
    UserNotFound = 2,
    UserNotEnabled = 3,
    UserDeleted = 4,
    UserDisabled = 5,
    UserDoesNotHaveFaceId = 6,
    UserDoesNotHaverAccess = 7,
    RequestTimeout = 8,
    RequestError = 9,
    RequestCancelled = 10,
    Rollback = 11,
    Unknown = 12,
    SuccessfullyStarted = 13,
    SuccessfullyCompleted = 14,
}