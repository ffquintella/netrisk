using System;
using System.Collections.Generic;

namespace DAL.Entities
{
    public partial class RiskScoring
    {
        public int Id { get; set; }
        public int ScoringMethod { get; set; }
        public float CalculatedRisk { get; set; }
        public float ClassicLikelihood { get; set; }
        public float ClassicImpact { get; set; }
        public string CvssAccessVector { get; set; } = null!;
        public string CvssAccessComplexity { get; set; } = null!;
        public string CvssAuthentication { get; set; } = null!;
        public string CvssConfImpact { get; set; } = null!;
        public string CvssIntegImpact { get; set; } = null!;
        public string CvssAvailImpact { get; set; } = null!;
        public string CvssExploitability { get; set; } = null!;
        public string CvssRemediationLevel { get; set; } = null!;
        public string CvssReportConfidence { get; set; } = null!;
        public string CvssCollateralDamagePotential { get; set; } = null!;
        public string CvssTargetDistribution { get; set; } = null!;
        public string CvssConfidentialityRequirement { get; set; } = null!;
        public string CvssIntegrityRequirement { get; set; } = null!;
        public string CvssAvailabilityRequirement { get; set; } = null!;
        public int? DreadDamagePotential { get; set; }
        public int? DreadReproducibility { get; set; }
        public int? DreadExploitability { get; set; }
        public int? DreadAffectedUsers { get; set; }
        public int? DreadDiscoverability { get; set; }
        public int? OwaspSkillLevel { get; set; }
        public int? OwaspMotive { get; set; }
        public int? OwaspOpportunity { get; set; }
        public int? OwaspSize { get; set; }
        public int? OwaspEaseOfDiscovery { get; set; }
        public int? OwaspEaseOfExploit { get; set; }
        public int? OwaspAwareness { get; set; }
        public int? OwaspIntrusionDetection { get; set; }
        public int? OwaspLossOfConfidentiality { get; set; }
        public int? OwaspLossOfIntegrity { get; set; }
        public int? OwaspLossOfAvailability { get; set; }
        public int? OwaspLossOfAccountability { get; set; }
        public int? OwaspFinancialDamage { get; set; }
        public int? OwaspReputationDamage { get; set; }
        public int? OwaspNonCompliance { get; set; }
        public int? OwaspPrivacyViolation { get; set; }
        public float? Custom { get; set; }
        public int? ContributingLikelihood { get; set; }
    }
}
