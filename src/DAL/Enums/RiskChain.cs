namespace DAL.Enums;

/// <summary>
/// The five levels of the MIGR-TI/IA linkage chain a risk can point at (Stage 9.1, S41 §4.2):
/// strategic objective → business process → IT service → data → asset.
///
/// Persisted as <c>risk_chain_links.chain_level</c>. The level is <b>derived</b> from the link's
/// target — the entity definition, or "host" — and never taken from a request payload, so there is
/// no "level does not match the target" error to handle (S41 §11, D3). It is stored anyway so the
/// table can be indexed and read as evidence without re-resolving every target.
///
/// Lives in DAL because the entity maps it; Model and Tools reference DAL, never the other way.
/// </summary>
public enum RiskChainLevel
{
    Objective = 1,
    Process = 2,
    ItService = 3,
    Data = 4,
    Asset = 5
}

/// <summary>
/// Where a chain link came from (S41 §5.4).
///
/// <see cref="Legacy"/> rows mirror <c>risk_to_entity</c> during the create-copy-coexist cycle and
/// that table is their source of truth, the way <c>risks.status</c> is for <c>status_id</c>: they
/// are created, and removed, by the legacy <c>/Risks/{id}/Entity</c> path. <see cref="Declared"/>
/// rows are written through the chain API and are never removed by the legacy path.
/// </summary>
public enum RiskChainLinkOrigin
{
    Declared = 1,
    Legacy = 2
}
