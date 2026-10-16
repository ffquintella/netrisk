using System;
using System.Collections.Generic;
using DAL.Enums;

namespace Model.RiskIndex;

// The read and profile contracts of the Consolidated Risk Index API (S40 §8.3; M53, T249/T250). Positional records, so
// System.Text.Json binds them through the constructor; collections never null; enums as numbers with explicit values that
// are only ever appended. The stored vocabularies (category, mode, display state, object kind and state, tab group,
// profile status, marker kind) are the DAL's own enums (DAL.Enums.RiskIndex), not redeclared here. Error codes stay text.

// ---------- enums the DAL does not store ----------

public enum RiskIndexProvenance { Official = 1, OfficialDerived = 2, PartialRestricted = 3, UnofficialPreview = 4 }

public enum RiskIndexDimension
{
    Unit = 1, Process = 2, Activity = 3, Application = 4, AssetClass = 5, Source = 6, Criticality = 7, Environment = 8
}

public enum RiskIndexPublicationState { Shadow = 1, Published = 2 }

public enum RiskIndexDeltaDirection { Worse = 1, Better = 2, Stable = 3 }

public enum RiskIndexDeltaSuppressedReason { BelowThreshold = 1, Discontinuity = 2, MissingBaseline = 3 }

public enum RiskIndexAttributionEffect
{
    Risk = 1, Context = 2, Coverage = 3, Configuration = 4, Unattributed = 5, Floor = 6, Restructuring = 7
}

public enum RiskIndexContributionBranch { Mean = 1, HeldBy = 2, Presumption = 3 }

public enum RiskIndexNavigationModule
{
    Risk = 1, Host = 2, HostFindings = 3, ApplicationFindings = 4, Incident = 5, Assessment = 6, Entity = 7
}

public enum RiskIndexPreviewState { NotRun = 0, Queued = 1, Running = 2, Completed = 3, Failed = 4, Stale = 5 }

public enum RiskIndexRebaselineTrigger { PresetChange = 1, OmegaPace = 2, ParameterCount = 3, ModeChange = 4 }

/// <summary>
/// The segregation-of-duties checks of an approval (S40 §6.4, §11.4). <see cref="ApproverRoleRequired"/> is the
/// <c>approver_role_required</c> refusal of §6.4, appended after the §8.3 list.
/// </summary>
public enum RiskIndexApprovalCheckCode
{
    SodAuthorCannotApprove = 1, SodBusinessReviewer = 2, ApproverPermissionNotExplicit = 3, SodSelfGrantedApprover = 4,
    InvalidTransition = 5, ApproverRoleRequired = 6
}

// ---------- references ----------

public record RiskIndexNodeRef(string Key, string Label, string NodeType);

public record RiskIndexObjectRef(RiskIndexObjectKind Kind, string Ref);

/// <summary>An object's label; a non-null <see cref="RequiresPermission"/> means it was redacted (S40 §11.3).</summary>
public record RiskIndexObjectLabelDto(RiskIndexObjectRef Object, string Label, string? RequiresPermission);

public record RiskIndexDateRange(DateOnly From, DateOnly To);

public record RiskIndexNavigationTargetDto(RiskIndexNavigationModule Module, int RecordId, string LabelKey);

// ---------- dashboard (§8.2) ----------

public record RiskIndexLatestDto(DateOnly Date, int ProfileId, RiskIndexMode Mode, RiskIndexPublicationState Publication);

public record RiskIndexPublicationDto(RiskIndexPublicationState State, DateTime? ChangedAtUtc, string? ChangedBy);

public record RiskIndexPublicationRequest(RiskIndexPublicationState State);

public record RiskIndexOverviewDto(RiskIndexNodeRef Node, IReadOnlyList<RiskIndexNodeRef> Breadcrumb,
    DateOnly SnapshotDate, DateTime ComputedAtUtc, RiskIndexProvenance Provenance,
    RiskIndexProfileSummaryDto Profile, RiskIndexMode Mode, RiskIndexHeadlineDto? Headline,
    IReadOnlyList<RiskIndexCategoryDto> Categories, IReadOnlyList<RiskIndexSealDto> Seals,
    IReadOnlyList<RiskIndexChipDto> Chips, RiskIndexQualitySummaryDto Quality, int ObjectCount,
    IReadOnlyList<RiskIndexBandDto> Bands, IReadOnlyList<RiskIndexNodeRef> ScopeEntities);

public record RiskIndexHeadlineDto(int? Displayed, double? Unfloored, double? Floored, string? BandKey, int? BandIndex,
    RiskIndexDisplayState DisplayState, double? IntervalLow, double? IntervalHigh, bool ShowIntervalOnCard,
    RiskIndexDeltaDto? Delta7d, IReadOnlyList<RiskIndexFloorReasonDto> FloorReasons,
    bool MeanBranchActive, RiskIndexCategory? HeldByCategory);

public record RiskIndexDeltaDto(int? Points, RiskIndexDeltaDirection? Direction, DateOnly? ComparedWith,
    RiskIndexDeltaSuppressedReason? SuppressedReason, DateOnly? DiscontinuityDate);

public record RiskIndexFloorReasonDto(string GateCode, RiskIndexObjectLabelDto Scenario, string? FlagKey);

public record RiskIndexCategoryDto(RiskIndexCategory Category, RiskIndexCategoryState State, double? Value, int? Displayed,
    string? BandKey, double Weight, double EffectiveWeight, double? Points, RiskIndexHeldByDto? HeldBy,
    double? Coverage, double? Freshness, double? PresumptionPoints, string? UnavailableReasonKey, string? RequiresRef,
    IReadOnlyList<RiskIndexSealDto> Seals);

public record RiskIndexHeldByDto(RiskIndexObjectLabelDto Object, double R, RiskIndexObjectLabelDto? Next, bool IsTie, int TieCount);

public record RiskIndexSealDto(string Key, int? Count, IReadOnlyDictionary<string, string> Args, string? ObjectFilter);

public record RiskIndexChipDto(string Key, IReadOnlyDictionary<string, string> Args, RiskIndexObjectLabelDto? Object,
    string? ObjectFilter);

public record RiskIndexBandDto(string Key, string LabelPtBr, string LabelEnUs, int Index, int RampStep, int LowerInclusive);

public record RiskIndexQualitySummaryDto(double Coverage, double Freshness, double Quality, string QualityBandKey,
    IReadOnlyList<RiskIndexQualityStripItemDto> Strip);

public record RiskIndexQualityStripItemDto(string Key, double? Fraction, int? Count, DateTime? LastReadingUtc,
    bool Warning, string? ObjectFilter);

public record RiskIndexTrendDto(RiskIndexNodeRef Node, int WindowDays, RiskIndexCategory Series,
    IReadOnlyList<RiskIndexTrendPointDto> Points, IReadOnlyList<RiskIndexBandSegmentDto> BandSegments,
    IReadOnlyList<RiskIndexMarkerDto> Markers, IReadOnlyList<RiskIndexAnnotationDto> Annotations,
    IReadOnlyList<RiskIndexDateRange> FlooredRanges, IReadOnlyList<RiskIndexOverlayDto> Overlays,
    double? AppetiteLine, double? TargetLine);

public record RiskIndexTrendPointDto(DateOnly Date, int? Displayed, double? Unfloored, int? DisplayedFloored, double? Ewma,
    RiskIndexDisplayState DisplayState, double? IntervalLow, double? IntervalHigh,
    IReadOnlyList<RiskIndexCategory> UnavailableCategories, bool IsGap, bool IsFloored, int? DeltaFromPrevious,
    IReadOnlyList<RiskIndexAttributionRowDto> TopMovers);

public record RiskIndexBandSegmentDto(DateOnly From, DateOnly To, int ProfileVersion, IReadOnlyList<RiskIndexBandDto> Bands);

public record RiskIndexMarkerDto(DateOnly Date, RiskIndexMarkerKind Kind, IReadOnlyDictionary<string, string> Args,
    double? VersionDelta);

public record RiskIndexAnnotationDto(int Id, DateOnly Date, int Kind, string Source, string Label, string? Details,
    string? CreatedBy);

public record RiskIndexOverlayDto(int FromProfileVersion, int ToProfileVersion, bool IsPartial,
    IReadOnlyList<RiskIndexOverlayPointDto> Points);

public record RiskIndexOverlayPointDto(DateOnly Date, double Value);

public record RiskIndexNodeChildrenDto(RiskIndexNodeRef Parent, RiskIndexDimension Dimension,
    IReadOnlyList<RiskIndexComparabilityGroupDto> Groups, IReadOnlyList<RiskIndexNodeRowDto> Rows,
    int ObjectsOutsideDimension);

public record RiskIndexComparabilityGroupDto(string Key, string NodeType, IReadOnlyList<RiskIndexCategory> ApplicableCategories,
    RiskIndexProvenance Provenance, string? NotComparableNoteKey);

public record RiskIndexNodeRowDto(RiskIndexNodeRef Node, int? Displayed, string? BandKey, RiskIndexDisplayState State,
    RiskIndexProvenance Provenance, int? Delta7d, double? Coverage, int ObjectCount, int SharedObjects, bool HasChildren,
    string ComparabilityGroup, double? IntervalLow, double? IntervalHigh, double? UnassignedWeightFraction);

public record RiskIndexObjectRowDto(RiskIndexObjectLabelDto Object, string? AssetClass, RiskIndexCategory Category, double R,
    int Criticality, string? CriticalitySource, RiskIndexObjectState State, double? Validity, DateTime? LastReadingUtc,
    double ContributionPoints, bool IsPresumed, int SharedWith, IReadOnlyList<string> Sources, string? Environment);

public record RiskIndexObjectDetailDto(RiskIndexObjectLabelDto Object, string? AssetClass, DateOnly SnapshotDate,
    RiskIndexCriticalityDto Criticality, bool? InternetFacing, IReadOnlyList<RiskIndexObjectCategoryDto> Categories,
    RiskIndexScenarioInputsDto? Scenario, RiskIndexDecisionStateDto? DecisionState,
    IReadOnlyList<RiskIndexNodeRef> Attribution, IReadOnlyList<RiskIndexChipDto> Chips,
    IReadOnlyList<RiskIndexNavigationTargetDto> Navigation);

public record RiskIndexCriticalityDto(int? Own, int? Inherited, int DataDelta, int Effective, string Source,
    string? AuthorName, DateTime? ChangedAtUtc, bool DeclaredNotBia);

public record RiskIndexObjectCategoryDto(RiskIndexCategory Category, double R, RiskIndexObjectState State, string Population,
    double Weight, RiskIndexContributionBranch Branch, IReadOnlyList<RiskIndexFactorDto> Factors);

public record RiskIndexFactorDto(string FactorKey, string Type, double X, double V, double UF, double FEff,
    DateTime? ReadingUtc, string SourceKey, IReadOnlyList<RiskIndexSignalDto> Signals);

/// <summary>A signal; <see cref="Text"/> and <see cref="Link"/> are null when redacted (S40 §11.3).</summary>
public record RiskIndexSignalDto(double S, IReadOnlyList<string> Flags, string? Text, RiskIndexNavigationTargetDto? Link);

public record RiskIndexScenarioInputsDto(int ImpactLevel, int? Rank, int? CampaignSize, double? RRes, double RInh,
    int CadenceDays, int DelayDays, double? Credit, double SigmaRef, double REff);

public record RiskIndexDecisionStateDto(string? AcceptanceStateKey, DateTime? AcceptanceValidUntilUtc,
    string? LastCampaignDecisionKey, DateTime? LastCampaignDecisionAtUtc, int OpenTasks, int OverdueTasks,
    IReadOnlyList<string> SealKeys, string GatesStateKey, double? NetBenefit);

public record RiskIndexChangesDto(RiskIndexNodeRef Node, DateOnly From, DateOnly To, double DeltaUnfloored, int DeltaDisplayed,
    IReadOnlyList<RiskIndexAttributionRowDto> Rows, IReadOnlyList<RiskIndexChangeEventDto> Events, double Residual,
    IReadOnlyList<DateOnly> CrossedMarkers);

public record RiskIndexAttributionRowDto(RiskIndexAttributionEffect Effect, RiskIndexCategory? Category,
    RiskIndexObjectLabelDto? Object, string LabelKey, double Points, int PointsRounded, double? PercentOfChange,
    string? AuthorName, bool IsReattribution);

public record RiskIndexChangeEventDto(string Kind, RiskIndexObjectLabelDto? Object, IReadOnlyDictionary<string, string> Args);

public record RiskIndexContributionsDto(RiskIndexNodeRef Node, double EffectiveRegisterWeight,
    IReadOnlyList<RiskIndexContributionRowDto> Rows);

public record RiskIndexContributionRowDto(RiskIndexObjectLabelDto Object, RiskIndexCategory Category, double Points,
    double ShareOfCategory, RiskIndexContributionBranch Branch, double? Sensitivity, RiskIndexObjectLabelDto? NextHolder,
    RiskIndexDecisionStateDto? DecisionState);

public record RiskIndexInventoryDto(RiskIndexTabGroup Tab, int InventoryCount, string InventoryBasisKey,
    IReadOnlyList<RiskIndexSourceVisibilityDto> Sources, RiskIndexStateCountsDto Overall,
    IReadOnlyDictionary<string, int> RDistributionByBand);

public record RiskIndexSourceVisibilityDto(string SourceKey, string Availability, bool Enabled, int Current, int Stale,
    int Expired, int NoReading, DateTime? LastReadingUtc, string? DisabledReasonKey);

public record RiskIndexStateCountsDto(int Assessed, int Stale, int Presumed, int NotAssessed);

public record RiskIndexGovernanceIndicatorsDto(int AcceptancesExpiringSoon, int ExpiryWindowDays, int SegregationOverrides90d,
    double? CampaignCompletion, double? MeanDaysToDecide, int OverdueCampaigns, double? SlaCompliance,
    string FlagsStateKey, string GateBStateKey);

// ---------- profiles (§8.4) ----------

public record RiskIndexProfileSummaryDto(int Id, int Version, string Name, string? Preset, RiskIndexProfileStatus Status,
    DateTime? EffectiveFrom, DateTime? RetiredAt, string CreatedBy, string? ApprovedBy, bool IsRebaseline,
    string? CommitteeDecisionRef);

public record RiskIndexProfileDto(int Id, int Version, string Name, string? Preset, RiskIndexMode Mode,
    RiskIndexProfileStatus Status, RiskIndexParameters Parameters, string? Justification, string? CommitteeDecisionRef,
    string? BoardApprovalRef, bool IsRebaseline, string? RejectedReason, string CreatedBy, DateTime CreatedAtUtc,
    DateTime? SubmittedAtUtc, string? ApprovedBy, DateTime? ApprovedAtUtc, string? ApprovalGrantSource,
    DateTime? EffectiveFrom, DateTime? RetiredAt, string? RetireJustification, string? RetireDecisionRef,
    string? RetiredBy, int Revision, bool HasSensitivityReport);

public record RiskIndexProfileCreateRequest(string Name, int? FromProfileId, string? Preset);

public record RiskIndexProfileUpdateRequest(string Name, RiskIndexMode Mode, RiskIndexParameters Parameters);

public record RiskIndexProfileDiffDto(int A, int B, IReadOnlyList<RiskIndexParameterChangeDto> Changes);

public record RiskIndexParameterChangeDto(string Group, string Key, string? ValueA, string? ValueB, bool Changed);

public record RiskIndexParameterSchemaDto(IReadOnlyList<RiskIndexParameterSchemaItemDto> Items);

public record RiskIndexParameterSchemaItemDto(string Key, string Group, string Type, string? Min, string? Max, string Default,
    bool Locked, string? LockedReasonKey, string HelpKey, string S39Ref);

public record RiskIndexPresetDto(string Key, string NameKey, RiskIndexParameters Parameters, bool Activatable);

public record RiskIndexValidationResultDto(bool IsValid, IReadOnlyList<RiskIndexValidationIssueDto> Errors,
    IReadOnlyList<RiskIndexValidationIssueDto> Warnings, IReadOnlyList<string> ChangedKeys, bool RequiresRebaseline,
    IReadOnlyList<RiskIndexRebaselineTrigger> RebaselineTriggers, bool RequiresBoardRef, int? TrimesterBaselineProfileId);

public record RiskIndexValidationIssueDto(string Code, string? ParameterKey, IReadOnlyDictionary<string, string> Args);

public record RiskIndexPreviewStatusDto(int? RequestId, RiskIndexPreviewState State, int Progress, string? ParametersHash,
    DateTime? FinishedAtUtc, string? ErrorMessage);

/// <summary>
/// The sensitivity preview (S39 §13.2; S40 §6.3). <see cref="Overlay"/> is appended to the §8.3 shape: the root's headline
/// replayed under the draft over the stored days of the last 90 (S39 §9.5), what the 90-day replay produces.
/// </summary>
public record RiskIndexPreviewDto(RiskIndexPreviewStatusDto Status, IReadOnlyList<RiskIndexPreviewNodeDto> Nodes,
    double? KendallTau, int ComparedUnits, IReadOnlyList<RiskIndexTornadoRowDto> Tornado,
    IReadOnlyList<RiskIndexPresetValueDto> Presets, double RegisterWeightActive, double RegisterWeightDraft,
    IReadOnlyList<RiskIndexMovingObjectDto> TopMovers, IReadOnlyList<RiskIndexPresetValueDto> PureMean, bool OverlayPartial,
    RiskIndexOverlayDto? Overlay = null);

public record RiskIndexPreviewNodeDto(RiskIndexNodeRef Node, double? Active, double Draft, string? BandKeyActive,
    string? BandKeyDraft, bool BandChanged);

public record RiskIndexTornadoRowDto(string ParameterKey, double Minus, double Plus);

public record RiskIndexPresetValueDto(string PresetKey, RiskIndexNodeRef Node, double Value);

public record RiskIndexMovingObjectDto(RiskIndexObjectLabelDto Object, RiskIndexCategory Category, double DeltaRw);

public record RiskIndexSubmitRequest(string Justification);

public record RiskIndexApproveRequest(string CommitteeDecisionRef, string? BoardApprovalRef, bool ConfirmRebaseline, string? Note);

public record RiskIndexRejectRequest(string Reason);

public record RiskIndexRetireRequest(string Justification, string CommitteeDecisionRef);

/// <summary>Links a file the caller uploaded through <c>/Files</c> to a profile as the committee's minutes (S40 §6.4, §8.4).</summary>
public record RiskIndexAttachRequest(int FileId);

public record RiskIndexApprovalEligibilityDto(IReadOnlyList<RiskIndexApprovalCheckDto> Checks, string? GrantSource,
    bool RequiresBoardRef, IReadOnlyList<RiskIndexRebaselineTrigger> RebaselineTriggers);

public record RiskIndexApprovalCheckDto(RiskIndexApprovalCheckCode Code, bool Passed, string Message);

public record RiskIndexBridgeDto(int FromVersion, int ToVersion, DateOnly BridgeDate, IReadOnlyList<RiskIndexBridgeNodeDto> Nodes,
    bool OverlayPartial);

public record RiskIndexBridgeNodeDto(RiskIndexNodeRef Node, RiskIndexCategory Category, double Old, double New,
    IReadOnlyList<RiskIndexOverlayPointDto> Overlay);
