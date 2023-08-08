using System.Collections.ObjectModel;

namespace GUIClient.Models;

public class TreeNode
{
    public ObservableCollection<TreeNode>? SubNodes { get; }
    public string Title { get; }

    public TreeNode(string title)
    {
        Title = title;
    }

    public TreeNode(string title, ObservableCollection<TreeNode> subNodes)
    {
        Title = title;
        SubNodes = subNodes;
    }
}