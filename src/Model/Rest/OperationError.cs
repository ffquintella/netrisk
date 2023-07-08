using System.Collections.Generic;

namespace Model.Rest;

public class OperationError
{

    public string Title { get; set; } = "";
    public int Status { get; set; }
    public Dictionary<string, string[]> Errors { get; set; } = new Dictionary<string, string[]>();
}