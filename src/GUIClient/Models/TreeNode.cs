using System.Collections.ObjectModel;
using ReactiveUI;

namespace GUIClient.Models;

public class TreeNode: ReactiveObject
{
    
    private ObservableCollection<TreeNode>? _subNodes;

    public ObservableCollection<TreeNode>? SubNodes
    {
        get => _subNodes;
        set
        {
            if (value == null) return;
            this.RaiseAndSetIfChanged(ref _subNodes, value);
        }
    }
    
    public string Title { get; set; }
    
    public int EntityId { get; set; }

    public bool IsVisible { get; set; } = true;

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