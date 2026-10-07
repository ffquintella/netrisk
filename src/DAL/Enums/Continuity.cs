namespace DAL.Enums;

/// <summary>
/// The result of a restoration test (Stage 9.3, S43 §4.3), persisted as <c>restoration_tests.outcome</c>.
///
/// A <see cref="Succeeded"/> test measured at least one objective — the schema refuses one that measured
/// nothing (<c>ck_restoration_tests_measured</c>). A <see cref="Failed"/> one may carry no measure at all:
/// the system never came back.
///
/// Lives in DAL because the entity maps it; Model and Tools reference DAL, never the other way.
/// </summary>
public enum RestorationTestOutcome
{
    Succeeded = 1,
    Failed = 2
}

/// <summary>
/// The two <c>settings</c> rows that tune Stage 9.3 (S43 §4.7). Declared in DAL because the governance
/// audit interceptor, which lives here, audits exactly these keys and nothing else of that table — the
/// table also holds the backup password, which must never reach the trail (S43 §4.4, D18).
/// </summary>
public static class ContinuitySettingKeys
{
    /// <summary>How many days a restoration test stays valid evidence. Default 365, range 1–1 095.</summary>
    public const string RestorationTestValidityDays = "continuity_restoration_test_validity_days";

    /// <summary>The weight of an unverified threat relative to a confirmed one. Default 0.5, range 0.01–1.00.</summary>
    public const string UnverifiedThreatWeight = "continuity_unverified_threat_weight";
}
