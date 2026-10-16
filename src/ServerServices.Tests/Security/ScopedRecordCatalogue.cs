using System;
using System.Collections.Generic;
using System.Linq;
using DAL.Context;
using DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace ServerServices.Tests.Security;

/// <summary>
/// Every record type the entity scope filters (<c>NRDbContext.EntityScope</c> and its partials), and a way to
/// plant one row of any of them "belonging to" a given business entity — its own <c>EntityId</c> for a type
/// filtered on its own column, its whole parent chain at that entity for a type filtered through a parent.
///
/// Rows are built from the EF model, not by hand: keys get unique explicit values, required strings a
/// placeholder, foreign keys the parent's key. So the T292 regression tests run over the full list rather than
/// a sample, and <see cref="Uncatalogued"/> fails the day a new filtered type appears without being listed here.
/// </summary>
internal sealed class ScopedRecordCatalogue(DbContext context)
{
    /// <summary>Types whose filter reads their own entity column.</summary>
    public static readonly Type[] DirectTypes =
    [
        typeof(Risk), typeof(Vulnerability), typeof(Host), typeof(Incident), typeof(Assessment), typeof(PendingRisk),
        typeof(Kri), typeof(RiskCommittee), typeof(ThirdParty), typeof(AiModel), typeof(RiskAcceptance),
        typeof(ScanImport), typeof(SlaConfiguration), typeof(RiskAppetite), typeof(EntityRiskReviewer),
        typeof(RiskReviewCampaign), typeof(NrFile), typeof(RiskIndexSnapshot), typeof(RiskIndexObjectDay),
        typeof(RiskIndexObjectAttribution), typeof(RiskIndexClassDay), typeof(RiskIndexBridge)
    ];

    /// <summary>
    /// The direct types whose own entity column is not <c>EntityId</c> (M53, T247): an attribution is filtered by the scope
    /// entity of its source row, a class day by its node. Both name that column to the write guard too.
    /// </summary>
    public static readonly Dictionary<Type, string> ScopeColumns = new()
    {
        [typeof(RiskIndexObjectAttribution)] = nameof(RiskIndexObjectAttribution.ScopeEntityId),
        [typeof(RiskIndexClassDay)] = nameof(RiskIndexClassDay.NodeEntityId)
    };

    /// <summary>
    /// Types whose filter goes through one or more parents, with the foreign key to each. A type with two
    /// parents (a correlation, a backtest link, a model link) gets both at the same entity, so it is visible
    /// exactly when that entity is.
    /// </summary>
    public static readonly Dictionary<Type, (string ForeignKey, Type Parent)[]> DerivedTypes = new()
    {
        [typeof(MgmtReview)] = [("RiskId", typeof(Risk))],
        [typeof(Mitigation)] = [("RiskId", typeof(Risk))],
        [typeof(MitigationTask)] = [("MitigationId", typeof(Mitigation))],
        [typeof(RiskChainLink)] = [("RiskId", typeof(Risk))],
        [typeof(DAL.Entities.HostsService)] = [("HostId", typeof(Host))],
        [typeof(AssessmentRun)] = [("AssessmentId", typeof(Assessment))],
        [typeof(AssessmentQuestion)] = [("AssessmentId", typeof(Assessment))],
        [typeof(AssessmentAnswer)] = [("AssessmentId", typeof(Assessment))],
        [typeof(AssessmentRunAnswer)] = [("AssessmentRunId", typeof(AssessmentRun))],
        [typeof(AssessmentRunsAnswer)] = [("RunId", typeof(AssessmentRun))],
        [typeof(FixRequest)] = [("VulnerabilityId", typeof(Vulnerability))],
        [typeof(VulnerabilityAttackTechnique)] = [("VulnerabilityId", typeof(Vulnerability))],
        [typeof(FindingStatusHistory)] = [("VulnerabilityId", typeof(Vulnerability))],
        [typeof(SlaNotification)] = [("VulnerabilityId", typeof(Vulnerability))],
        [typeof(RiskAcceptanceFinding)] = [("VulnerabilityId", typeof(Vulnerability))],
        [typeof(RiskAttackTechnique)] = [("RiskId", typeof(Risk))],
        [typeof(RiskFlag)] = [("RiskId", typeof(Risk))],
        [typeof(RiskDecision)] = [("RiskId", typeof(Risk))],
        [typeof(MitigationEconomics)] = [("MitigationId", typeof(Mitigation))],
        [typeof(MitigationDependency)] = [("MitigationId", typeof(Mitigation))],
        [typeof(RiskTarget)] = [("RiskId", typeof(Risk))],
        [typeof(RiskLossComponent)] = [("RiskId", typeof(Risk))],
        [typeof(RiskTailStatistics)] = [("RiskId", typeof(Risk))],
        [typeof(RiskTailComponent)] = [("TailStatisticsId", typeof(RiskTailStatistics))],
        [typeof(RiskCorrelation)] = [("RiskAId", typeof(Risk)), ("RiskBId", typeof(Risk))],
        [typeof(RiskAppetiteTailLimit)] = [("AppetiteId", typeof(RiskAppetite))],
        [typeof(KriReading)] = [("KriId", typeof(Kri))],
        [typeof(KriRisk)] = [("RiskId", typeof(Risk))],
        [typeof(RiskReassessmentTrigger)] = [("RiskId", typeof(Risk))],
        [typeof(ReassessmentEvent)] = [("KriId", typeof(Kri))],
        [typeof(RiskArchive)] = [("RiskId", typeof(Risk))],
        [typeof(RiskArchiveCondition)] = [("ArchiveId", typeof(RiskArchive))],
        [typeof(RiskArchiveReview)] = [("ArchiveId", typeof(RiskArchive))],
        [typeof(IncidentBacktest)] = [("IncidentId", typeof(Incident))],
        [typeof(IncidentBacktestRisk)] = [("BacktestId", typeof(IncidentBacktest)), ("RiskId", typeof(Risk))],
        [typeof(RiskCommitteeMember)] = [("CommitteeId", typeof(RiskCommittee))],
        [typeof(RiskCommitteeDecision)] = [("RiskId", typeof(Risk))],
        [typeof(RiskCommitteeVote)] = [("DecisionId", typeof(RiskCommitteeDecision))],
        [typeof(ThirdPartyLink)] = [("ThirdPartyId", typeof(ThirdParty))],
        [typeof(ThirdPartySubprocessor)] = [("ThirdPartyId", typeof(ThirdParty))],
        [typeof(ThirdPartyDataLocation)] = [("ThirdPartyId", typeof(ThirdParty))],
        [typeof(ThirdPartyAssessment)] = [("ThirdPartyId", typeof(ThirdParty))],
        [typeof(ThirdPartyAssessmentAnswer)] = [("AssessmentId", typeof(ThirdPartyAssessment))],
        [typeof(ThirdPartySbom)] = [("ThirdPartyId", typeof(ThirdParty))],
        [typeof(ThirdPartySbomComponent)] = [("SbomId", typeof(ThirdPartySbom))],
        [typeof(RiskLegalRequirement)] = [("RiskId", typeof(Risk))],
        [typeof(AiModelDataLink)] = [("ModelId", typeof(AiModel))],
        [typeof(AiModelMetricReading)] = [("ModelId", typeof(AiModel))],
        [typeof(AiModelOverride)] = [("ModelId", typeof(AiModel))],
        [typeof(AiModelRisk)] = [("RiskId", typeof(Risk)), ("ModelId", typeof(AiModel))],
        [typeof(RiskReviewCampaignItem)] = [("CampaignId", typeof(RiskReviewCampaign))],
        [typeof(RiskIndexIncidentClosure)] = [("IncidentId", typeof(Incident))],
    };

    public static IEnumerable<Type> AllTypes => DirectTypes.Concat(DerivedTypes.Keys);

    public static Type Resolve(string name) => AllTypes.Single(t => t.Name == name);

    /// <summary>The filtered types of <paramref name="model"/> this catalogue does not list.</summary>
    public static IEnumerable<string> Uncatalogued(IModel model) =>
        model.GetEntityTypes()
            .Where(e => e.GetDeclaredQueryFilters().Count > 0)
            .Select(e => e.ClrType)
            .Where(t => !AllTypes.Contains(t))
            .Select(t => t.Name)
            .OrderBy(n => n, StringComparer.Ordinal);

    private static int _nextKey = 100_000;

    private readonly Dictionary<(Type, int), object> _rows = new();

    /// <summary>The entity each planted row was planted for, by type and primary-key value.</summary>
    public Dictionary<(Type, string), int> EntityOfRow { get; } = new();

    /// <summary>A row of <paramref name="type"/> belonging to <paramref name="entityId"/>, planted with its parents.</summary>
    public object Plant(Type type, int entityId)
    {
        if (_rows.TryGetValue((type, entityId), out var existing)) return existing;

        var entityType = context.Model.FindEntityType(type)
                         ?? throw new InvalidOperationException($"{type.Name} is not mapped");

        var row = Activator.CreateInstance(type)!;
        var assigned = new HashSet<string>(StringComparer.Ordinal);

        if (DerivedTypes.TryGetValue(type, out var parents))
        {
            foreach (var (foreignKey, parentType) in parents)
            {
                var parent = Plant(parentType, entityId);
                Set(row, foreignKey, KeyOf(parent));
                assigned.Add(foreignKey);
            }
        }
        else
        {
            var column = ScopeColumns.GetValueOrDefault(type, "EntityId");
            Set(row, column, entityId);
            assigned.Add(column);
        }

        foreach (var key in entityType.FindPrimaryKey()!.Properties.Where(p => !assigned.Contains(p.Name)))
        {
            var next = System.Threading.Interlocked.Increment(ref _nextKey);
            var keyType = Nullable.GetUnderlyingType(key.ClrType) ?? key.ClrType;
            object value = keyType switch
            {
                _ when keyType == typeof(int) => next,
                _ when keyType == typeof(long) => (long)next,
                _ when keyType == typeof(short) => (short)(next % short.MaxValue),
                _ when keyType == typeof(Guid) => Guid.NewGuid(),
                _ when keyType == typeof(string) => $"k{next}",
                _ => throw new InvalidOperationException($"{type.Name}: key type {keyType.Name} not handled")
            };
            Set(row, key.Name, value);
            assigned.Add(key.Name);
        }

        foreach (var property in entityType.GetProperties())
        {
            if (assigned.Contains(property.Name) || property.PropertyInfo is null || property.IsNullable) continue;

            var current = property.PropertyInfo.GetValue(row);
            if (current is not null) continue;

            if (property.ClrType == typeof(string)) property.PropertyInfo.SetValue(row, "x");
            else if (property.ClrType == typeof(byte[])) property.PropertyInfo.SetValue(row, Array.Empty<byte>());
        }

        context.Add(row);
        _rows[(type, entityId)] = row;
        EntityOfRow[(type, KeyString(row))] = entityId;
        return row;
    }

    /// <summary>The single key value of <paramref name="row"/>, for a child's foreign key.</summary>
    public object KeyOf(object row)
    {
        var key = context.Model.FindEntityType(row.GetType())!.FindPrimaryKey()!.Properties;
        if (key.Count != 1) throw new InvalidOperationException($"{row.GetType().Name} has a composite key; it cannot be a parent here");
        return key[0].PropertyInfo!.GetValue(row)!;
    }

    public string KeyString(object row) =>
        string.Join("|", context.Model.FindEntityType(row.GetType())!.FindPrimaryKey()!.Properties
            .Select(p => p.PropertyInfo!.GetValue(row)?.ToString()));

    private static void Set(object row, string property, object value)
    {
        var info = row.GetType().GetProperty(property)
                   ?? throw new InvalidOperationException($"{row.GetType().Name} has no property {property}");
        var target = Nullable.GetUnderlyingType(info.PropertyType) ?? info.PropertyType;
        info.SetValue(row, Convert.ChangeType(value, target, System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>The entities whose planted row of <paramref name="type"/> <paramref name="reader"/> can see.</summary>
    public static IReadOnlySet<int> VisibleEntities(AuditableContext reader, Type type, ScopedRecordCatalogue planted)
    {
        var set = (IQueryable<object>)typeof(DbContext).GetMethod(nameof(DbContext.Set), Type.EmptyTypes)!
            .MakeGenericMethod(type).Invoke(reader, null)!;

        var probe = new ScopedRecordCatalogue(reader);
        return set.ToList()
            .Select(row => planted.EntityOfRow.TryGetValue((type, probe.KeyString(row)), out var entity) ? entity : -1)
            .Where(entity => entity >= 0)
            .ToHashSet();
    }
}
