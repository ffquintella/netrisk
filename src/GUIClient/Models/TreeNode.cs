using System.Collections.ObjectModel;

namespace GUIClient.Models;

public class TreeNode
{
    public ObservableCollection<TreeNode>? SubNodes { get; }
    public string Title { get; }
    
    public int EntityId { get; }

    public TreeNode(string title)
    {
        Title = title;
    }

    public TreeNode(string title, int entityId, ObservableCollection<TreeNode> subNodes)
    {
        EntityId = entityId;
        Title = title;
        SubNodes = subNodes;
    }
}