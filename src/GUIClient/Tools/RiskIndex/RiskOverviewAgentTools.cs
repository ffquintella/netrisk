using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using ClientServices.Interfaces;
using GUIClient.Agents;
using GUIClient.Agents.ReadTools;
using Model.RiskIndex;

namespace GUIClient.Tools.RiskIndex;

/// <summary>
/// The Cyber Risk Overview's agent read tools (T351, S55 "Reports / ICR: view overview, drill-down"). They live with the
/// module's helpers because only the module may name the index contracts (S40 §11.5, <c>RiskOverviewSourceScanTest</c>):
/// like the module's own views they read and never decide. The catalog receives them from the composition root.
/// </summary>
public sealed class RiskOverviewAgentTools : IAgentToolSource
{
    private readonly IRiskIndexService _riskIndex;

    public RiskOverviewAgentTools(IRiskIndexService riskIndex) => _riskIndex = riskIndex;

    public IEnumerable<IAgentTool> Tools => Create(_riskIndex);

    private static IEnumerable<IAgentTool> Create(IRiskIndexService riskIndex)
    {
        // [PermissionAuthorize(risk_index_view)] on RiskIndexController: the permission or the Admin role claim.
        var overview = AgentToolPermission.Granted(RiskIndexPermissions.View);

        yield return new AgentReadTool<AgentRiskOverview>("get_risk_overview",
            "The Cyber Risk Overview of one node (the whole organisation when node_ref is omitted): headline index, " +
            "band, 7-day change, categories and data quality. Informational only: it decides nothing.",
            overview,
            AgentSchema.Object(AgentSchema.Text("node_ref", 200, "A node_ref from an earlier overview answer; omit for the top.")),
            async (_, args, cancellationToken) =>
            {
                var answer = await riskIndex.GetOverviewAsync(args.Text("node_ref", 200), null, cancellationToken)
                             ?? throw AgentToolErrors.NoAnswerError();
                return Overview(answer);
            });

        yield return new AgentReadTool<AgentOverviewChildren>("list_risk_overview_children",
            "Drill down the Cyber Risk Overview: the children of one node along a dimension, each with its index and band.",
            overview,
            AgentSchema.Object(
                AgentSchema.Text("node_ref", 200, "The parent node_ref; omit for the top."),
                ("dimension", new JsonObject
                {
                    ["type"] = "string",
                    ["enum"] = new JsonArray(Enum.GetNames<RiskIndexDimension>()
                        .Select(n => (JsonNode?)JsonValue.Create(n)).ToArray()),
                    ["description"] = "How to break the node down (default Unit)."
                }, false)),
            async (_, args, cancellationToken) =>
            {
                var dimensionText = args.Text("dimension", 32);
                var dimension = RiskIndexDimension.Unit;
                if (dimensionText is not null && (!Enum.TryParse(dimensionText, ignoreCase: true, out dimension)
                                                  || !Enum.IsDefined(dimension)))
                    throw new AgentToolException(AgentToolErrors.Invalid, "Argument 'dimension' is not a known dimension.");
                var answer = await riskIndex.GetNodesAsync(args.Text("node_ref", 200), dimension, null, null,
                    cancellationToken) ?? throw AgentToolErrors.NoAnswerError();
                return Children(answer);
            });
    }

    private static string? Title(string? value) => AgentResultLimits.Clip(value, AgentResultLimits.TitleLength);

    private static AgentOverviewNode Node(RiskIndexNodeRef n) => new(Title(n.Key) ?? "", Title(n.Label) ?? "", Title(n.NodeType) ?? "");

    private static AgentRiskOverview Overview(RiskIndexOverviewDto o) => new(Node(o.Node),
        (o.Breadcrumb ?? Array.Empty<RiskIndexNodeRef>()).Select(Node).ToList(), o.SnapshotDate, o.ComputedAtUtc,
        o.Provenance.ToString(), o.Mode.ToString(),
        o.Headline is { } h
            ? new AgentOverviewHeadline(h.Displayed, Title(h.BandKey), h.DisplayState.ToString(), h.Delta7d?.Points,
                h.Delta7d?.Direction?.ToString())
            : null,
        (o.Categories ?? Array.Empty<RiskIndexCategoryDto>())
            .Select(c => new AgentOverviewCategory(c.Category.ToString(), c.State.ToString(), c.Displayed, Title(c.BandKey), c.Weight))
            .ToList(),
        o.Quality is { } q ? new AgentOverviewQuality(q.Coverage, q.Freshness, q.Quality, Title(q.QualityBandKey)) : null,
        o.ObjectCount);

    private static AgentOverviewChildren Children(RiskIndexNodeChildrenDto c) => new(Node(c.Parent), c.Dimension.ToString(),
        AgentCappedList<AgentOverviewChild>.Of(c.Rows, r => new AgentOverviewChild(Node(r.Node), r.Displayed, Title(r.BandKey),
            r.State.ToString(), r.Delta7d, r.Coverage, r.ObjectCount, r.HasChildren)));
}
