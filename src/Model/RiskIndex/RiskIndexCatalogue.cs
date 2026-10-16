using System.Collections.Generic;

namespace Model.RiskIndex;

/// <summary>
/// The factor keys of S39 §2.3 that carry a ceiling <c>u_f</c> in a profile (<c>u.&lt;factor&gt;</c>, S39 §12.2; S40 §6.2.2
/// group 1). REG.R is fixed at 1.0 and AME.Q's ceiling is <c>fator_quase_incidente</c>, so neither is listed.
/// </summary>
public static class RiskIndexFactors
{
    public const string ExpDevice = "EXP.D";
    public const string ExpVulnerabilities = "EXP.V";
    public const string ExpExternal = "EXP.X";
    public const string ExpIdentities = "EXP.I";
    public const string ExpCloud = "EXP.N";
    public const string AmeIncidents = "AME.I";
    public const string CtlAssessments = "CTL.A";
    public const string CtlResponse = "CTL.P";
    public const string CtlConfiguration = "CTL.C";
    public const string GovAppetite = "GOV.A";
    public const string GovCampaign = "GOV.C";
    public const string GovTreatment = "GOV.T";
    public const string GovReview = "GOV.R";
    public const string GovDivergence = "GOV.G";
    public const string Ter = "TER";

    /// <summary>The default ceilings (S39 §2.3).</summary>
    public static Dictionary<string, double> DefaultCeilings() => new()
    {
        [ExpDevice] = 1.0, [ExpVulnerabilities] = 1.0, [ExpExternal] = 1.0, [ExpIdentities] = 1.0, [ExpCloud] = 0.8,
        [AmeIncidents] = 1.0,
        [CtlAssessments] = 0.8, [CtlResponse] = 0.6, [CtlConfiguration] = 0.8,
        [GovAppetite] = 1.0, [GovCampaign] = 0.8, [GovTreatment] = 0.8, [GovReview] = 1.0, [GovDivergence] = 0.8,
        [Ter] = 1.0
    };
}

/// <summary>
/// The source ids of the S39 §3.2 catalogue a profile enables (<c>fontes_habilitadas</c>) and dates (<c>frescor_W</c>).
/// "Demais importadores" (sarif, semgrep, zap, trivy, …) has no id in S39; it is <see cref="Importers"/> here.
/// Indicators outside the sum (V1-WB, V1-CRI, TEN-CES) and panel-only rows (REG-ALE, REG-P95) are not sources.
/// </summary>
public static class RiskIndexSources
{
    public const string RegResidual = "REG-RES";
    public const string RegImpact = "REG-IMP";
    public const string RegRank = "REG-RANK";
    public const string V1Device = "V1-DEV";
    public const string V1Cve = "V1-CVE";
    public const string V1InternetFacing = "V1-IFA";
    public const string V1Accounts = "V1-ACC";
    public const string V1Cloud = "V1-CLD";
    public const string V1LocalApps = "V1-APP";
    public const string V1SecurityConfiguration = "V1-SCS";
    public const string SscTotal = "SSC-TOT";
    public const string TenableNessus = "TEN-NES";
    public const string TenableVm = "TEN-VM";
    public const string TenableAes = "TEN-AES";
    public const string TenableAcr = "TEN-ACR";
    public const string Importers = "IMPORTERS";
    public const string Incidents = "INC";
    public const string NearMisses = "INC-NM";
    public const string IncidentResponsePlans = "IRP";
    public const string Assessments = "ASM";
    public const string CmdbCriticality = "CMDB-CRIT";
    public const string CmdbEnvironment = "CMDB-ENV";
    public const string CmdbInternetFacing = "CMDB-INET";
    public const string CmdbRelations = "CMDB-REL";
    public const string EntityCriticality = "ENT-CRIT";
    public const string EntityClassification = "ENT-CLASS";
    public const string EntityInternetFacing = "ENT-INET";

    /// <summary>
    /// The default of <c>fontes_habilitadas</c>, "every [H] source" (S39 §12.2) read against the installed version (S40
    /// §6.5): the sources that feed a factor, a weight or the criticality and need no new integration. The [N] ones
    /// (V1-IFA/ACC/CLD/APP/SCS, TEN-VM/AES/ACR, INC-NM, CMDB-INET/REL) are enabled by a later profile version.
    /// </summary>
    public static List<string> DefaultEnabled() =>
    [
        RegResidual, RegImpact, RegRank, V1Device, V1Cve, SscTotal, TenableNessus, Importers, Incidents,
        IncidentResponsePlans, Assessments, CmdbCriticality, CmdbEnvironment, EntityCriticality, EntityClassification,
        EntityInternetFacing
    ];

    /// <summary>The default validity window W per source, in days (S39 §3.2, §12.2: V1 7 · SSC 7 · scanners, Tenable, CMDB 30 · assessments 365).</summary>
    public static Dictionary<string, int> DefaultFreshnessDays() => new()
    {
        [V1Device] = 7, [V1Cve] = 7, [V1InternetFacing] = 7, [V1Accounts] = 7, [V1Cloud] = 7, [V1LocalApps] = 7,
        [V1SecurityConfiguration] = 7,
        [SscTotal] = 7,
        [TenableNessus] = 30, [TenableVm] = 30, [TenableAes] = 30, [TenableAcr] = 30, [Importers] = 30,
        [CmdbCriticality] = 30, [CmdbEnvironment] = 30, [CmdbInternetFacing] = 30, [CmdbRelations] = 30,
        [Assessments] = 365
    };
}
